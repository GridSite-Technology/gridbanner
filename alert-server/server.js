const express = require('express');
const bodyParser = require('body-parser');
const fs = require('fs').promises;
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;
const CONFIG_FILE = path.join(__dirname, 'config.json');
const ALERT_FILE = process.env.ALERT_FILE || path.join(__dirname, 'alerts', 'current.json');
const DATA_FILE = path.join(__dirname, 'data.json');

// Ensure alerts directory exists
const alertsDir = path.dirname(ALERT_FILE);
fs.mkdir(alertsDir, { recursive: true }).catch(() => {});

// In-memory data storage
let dataStore = {
  systems: {},  // Key: workstation_name, Value: { last_seen, workstation_name, username, classification, location, company }
  sites: [],   // Array of { id, name }
  templates: [], // Array of { id, name, level, summary, message, background_color, foreground_color, site, contact fields }
  settings: {   // Default settings
    default_contact_name: '',
    default_contact_phone: '',
    default_contact_email: '',
    default_contact_teams: ''
  }
};

// Load data store from file
async function loadDataStore() {
  try {
    const data = await fs.readFile(DATA_FILE, 'utf8');
    dataStore = JSON.parse(data);
    // Ensure all required fields exist
    if (!dataStore.systems) dataStore.systems = {};
    if (!dataStore.sites) dataStore.sites = [];
    if (!dataStore.templates) dataStore.templates = [];
    if (!dataStore.settings) dataStore.settings = {
      default_contact_name: '',
      default_contact_phone: '',
      default_contact_email: '',
      default_contact_teams: ''
    };
  } catch (err) {
    if (err.code !== 'ENOENT') {
      console.error('Error loading data store:', err);
    }
    // Use defaults
    dataStore = { 
      systems: {}, 
      sites: [], 
      templates: [],
      settings: {
        default_contact_name: '',
        default_contact_phone: '',
        default_contact_email: '',
        default_contact_teams: ''
      }
    };
  }
}

// Save data store to file
async function saveDataStore() {
  try {
    await fs.writeFile(DATA_FILE, JSON.stringify(dataStore, null, 2), 'utf8');
  } catch (err) {
    console.error('Error saving data store:', err);
  }
}

// Initialize data store on startup
loadDataStore().catch(() => {});

// Load or initialize admin key
let ADMIN_KEY = process.env.ADMIN_KEY || 'adminkey';

async function loadAdminKey() {
  try {
    const data = await fs.readFile(CONFIG_FILE, 'utf8');
    const config = JSON.parse(data);
    if (config.admin_key && config.admin_key.trim().length > 0) {
      ADMIN_KEY = config.admin_key.trim();
    }
  } catch (err) {
    // Config file doesn't exist or is invalid, use default
    await saveAdminKey(ADMIN_KEY);
  }
}

async function saveAdminKey(key) {
  try {
    const config = { admin_key: key.trim() };
    await fs.writeFile(CONFIG_FILE, JSON.stringify(config, null, 2), 'utf8');
    ADMIN_KEY = key.trim();
  } catch (err) {
    console.error('Error saving admin key:', err);
  }
}

// Initialize admin key on startup
loadAdminKey().catch(() => {});

// Simple key authentication middleware
function requireAdminKey(req, res, next) {
  const providedKey = req.headers['x-admin-key'] || req.query.admin_key;
  
  // Strict check: must be provided and must match exactly
  if (!providedKey || typeof providedKey !== 'string' || providedKey.trim() !== ADMIN_KEY) {
    return res.status(401).json({ error: 'Unauthorized: Invalid admin key' });
  }
  
  next();
}

// Middleware
app.use(bodyParser.json());

// API routes (must be before static middleware)
// GET /api/alert - Get current alert (public, for GridBanner clients)
app.get('/api/alert', async (req, res) => {
  try {
    const data = await fs.readFile(ALERT_FILE, 'utf8');
    const trimmed = data.trim();
    
    // Empty file = no alert
    if (trimmed.length === 0) {
      return res.status(204).send();
    }
    
    const alert = JSON.parse(trimmed);
    res.json(alert);
  } catch (err) {
    if (err.code === 'ENOENT') {
      // File doesn't exist = no alert
      return res.status(204).send();
    }
    console.error('Error reading alert:', err);
    res.status(500).json({ error: 'Failed to read alert' });
  }
});

// POST /api/alert - Either create/update alert (admin) OR report system info (client)
app.post('/api/alert', async (req, res) => {
  try {
    const providedKey = req.headers['x-admin-key'];
    const isAdmin = providedKey && typeof providedKey === 'string' && providedKey.trim() === ADMIN_KEY;
    
    // Check if this is system info reporting
    // System info has workstation_name but NO alert fields (level, summary, etc.)
    // Admin requests have alert fields (level, summary, message, etc.)
    const hasSystemInfo = req.body.workstation_name && 
                          !req.body.level && 
                          !req.body.summary && 
                          !req.body.message;
    const isSystemReport = !isAdmin && hasSystemInfo;
    
    if (isSystemReport) {
      // Client reporting system info
      const systemInfo = {
        workstation_name: req.body.workstation_name || '',
        username: req.body.username || '',
        classification: req.body.classification || '',
        location: req.body.location || '',
        company: req.body.company || ''
      };
      
      // Update system info
      const key = systemInfo.workstation_name || 'unknown';
      dataStore.systems[key] = {
        ...systemInfo,
        last_seen: new Date().toISOString()
      };
      
      // Save data store (async, don't wait)
      saveDataStore().catch(() => {});
      
      console.log(`System reported: ${key} (${systemInfo.username}@${systemInfo.company})`);
      
      // Return current alert (if any)
      try {
        const data = await fs.readFile(ALERT_FILE, 'utf8');
        const trimmed = data.trim();
        if (trimmed.length === 0) {
          return res.status(204).send();
        }
        const alert = JSON.parse(trimmed);
        return res.json(alert);
      } catch (err) {
        if (err.code === 'ENOENT') {
          return res.status(204).send();
        }
        return res.status(204).send(); // On error, return no alert
      }
    }
    
    // Admin creating/updating alert
    if (!isAdmin) {
      return res.status(401).json({ error: 'Unauthorized: Invalid admin key' });
    }
    
    const alert = req.body;
    
    // Basic validation
    if (!alert.level || !alert.summary || !alert.message || 
        !alert.background_color || !alert.foreground_color) {
      return res.status(400).json({ 
        error: 'Missing required fields: level, summary, message, background_color, foreground_color' 
      });
    }
    
    // Ensure alerts directory exists
    await fs.mkdir(alertsDir, { recursive: true });
    
    // Write alert file
    await fs.writeFile(ALERT_FILE, JSON.stringify(alert, null, 2), 'utf8');
    
    res.json({ success: true, message: 'Alert updated' });
  } catch (err) {
    console.error('Error in POST /api/alert:', err);
    res.status(500).json({ error: 'Failed to process request' });
  }
});

// DELETE /api/alert - Clear alert (requires admin key)
app.delete('/api/alert', requireAdminKey, async (req, res) => {
  try {
    // Write empty file to clear alert
    await fs.writeFile(ALERT_FILE, '', 'utf8');
    res.json({ success: true, message: 'Alert cleared' });
  } catch (err) {
    console.error('Error clearing alert:', err);
    res.status(500).json({ error: 'Failed to clear alert' });
  }
});

// GET /api/alerts/list - List all alerts (admin only, for future use)
app.get('/api/alerts/list', requireAdminKey, async (req, res) => {
  try {
    const data = await fs.readFile(ALERT_FILE, 'utf8');
    const trimmed = data.trim();
    
    if (trimmed.length === 0) {
      return res.json({ alert: null });
    }
    
    const alert = JSON.parse(trimmed);
    res.json({ alert });
  } catch (err) {
    if (err.code === 'ENOENT') {
      return res.json({ alert: null });
    }
    console.error('Error reading alert:', err);
    res.status(500).json({ error: 'Failed to read alert' });
  }
});

// POST /api/admin/key - Change admin key (requires current admin key)
app.post('/api/admin/key', requireAdminKey, async (req, res) => {
  try {
    const { new_key } = req.body;
    
    if (!new_key || typeof new_key !== 'string' || new_key.trim().length === 0) {
      return res.status(400).json({ error: 'New admin key is required and must not be empty' });
    }
    
    if (new_key.trim().length < 4) {
      return res.status(400).json({ error: 'Admin key must be at least 4 characters' });
    }
    
    await saveAdminKey(new_key.trim());
    res.json({ success: true, message: 'Admin key updated' });
  } catch (err) {
    console.error('Error updating admin key:', err);
    res.status(500).json({ error: 'Failed to update admin key' });
  }
});

// GET /api/admin/key - Get current admin key status (requires admin key, doesn't reveal the key)
app.get('/api/admin/key', requireAdminKey, async (req, res) => {
  res.json({ 
    success: true, 
    key_set: ADMIN_KEY.length > 0,
    key_length: ADMIN_KEY.length 
  });
});

// GET /api/systems - Get all systems (admin only)
app.get('/api/systems', requireAdminKey, async (req, res) => {
  try {
    const systems = Object.values(dataStore.systems);
    res.json({ systems });
  } catch (err) {
    console.error('Error getting systems:', err);
    res.status(500).json({ error: 'Failed to get systems' });
  }
});

// GET /api/sites - Get all sites (admin only)
app.get('/api/sites', requireAdminKey, async (req, res) => {
  try {
    res.json({ sites: dataStore.sites });
  } catch (err) {
    console.error('Error getting sites:', err);
    res.status(500).json({ error: 'Failed to get sites' });
  }
});

// POST /api/sites - Create a site (admin only)
app.post('/api/sites', requireAdminKey, async (req, res) => {
  try {
    const { name } = req.body;
    if (!name || typeof name !== 'string' || name.trim().length === 0) {
      return res.status(400).json({ error: 'Site name is required' });
    }
    
    const id = Date.now().toString();
    const site = { id, name: name.trim() };
    dataStore.sites.push(site);
    await saveDataStore();
    
    res.json({ success: true, site });
  } catch (err) {
    console.error('Error creating site:', err);
    res.status(500).json({ error: 'Failed to create site' });
  }
});

// PUT /api/sites/:id - Update a site (admin only)
app.put('/api/sites/:id', requireAdminKey, async (req, res) => {
  try {
    const { id } = req.params;
    const { name } = req.body;
    
    if (!name || typeof name !== 'string' || name.trim().length === 0) {
      return res.status(400).json({ error: 'Site name is required' });
    }
    
    const site = dataStore.sites.find(s => s.id === id);
    if (!site) {
      return res.status(404).json({ error: 'Site not found' });
    }
    
    site.name = name.trim();
    await saveDataStore();
    
    res.json({ success: true, site });
  } catch (err) {
    console.error('Error updating site:', err);
    res.status(500).json({ error: 'Failed to update site' });
  }
});

// DELETE /api/sites/:id - Delete a site (admin only)
app.delete('/api/sites/:id', requireAdminKey, async (req, res) => {
  try {
    const { id } = req.params;
    const index = dataStore.sites.findIndex(s => s.id === id);
    
    if (index === -1) {
      return res.status(404).json({ error: 'Site not found' });
    }
    
    dataStore.sites.splice(index, 1);
    await saveDataStore();
    
    res.json({ success: true });
  } catch (err) {
    console.error('Error deleting site:', err);
    res.status(500).json({ error: 'Failed to delete site' });
  }
});

// GET /api/templates - Get all templates (admin only)
app.get('/api/templates', requireAdminKey, async (req, res) => {
  try {
    res.json({ templates: dataStore.templates });
  } catch (err) {
    console.error('Error getting templates:', err);
    res.status(500).json({ error: 'Failed to get templates' });
  }
});

// POST /api/templates - Create a template (admin only)
app.post('/api/templates', requireAdminKey, async (req, res) => {
  try {
    const template = req.body;
    
    if (!template.name || !template.level || !template.summary || !template.message || 
        !template.background_color || !template.foreground_color) {
      return res.status(400).json({ 
        error: 'Missing required fields: name, level, summary, message, background_color, foreground_color' 
      });
    }
    
    const id = Date.now().toString();
    const newTemplate = {
      id,
      name: template.name.trim(),
      level: template.level.trim(),
      summary: template.summary.trim(),
      message: template.message.trim(),
      background_color: template.background_color.trim(),
      foreground_color: template.foreground_color.trim(),
      site: template.site?.trim() || null,
      alert_contact_name: template.alert_contact_name?.trim() || null,
      alert_contact_phone: template.alert_contact_phone?.trim() || null,
      alert_contact_email: template.alert_contact_email?.trim() || null,
      alert_contact_teams: template.alert_contact_teams?.trim() || null
    };
    
    dataStore.templates.push(newTemplate);
    await saveDataStore();
    
    res.json({ success: true, template: newTemplate });
  } catch (err) {
    console.error('Error creating template:', err);
    res.status(500).json({ error: 'Failed to create template' });
  }
});

// PUT /api/templates/:id - Update a template (admin only)
app.put('/api/templates/:id', requireAdminKey, async (req, res) => {
  try {
    const { id } = req.params;
    const template = req.body;
    
    const existing = dataStore.templates.find(t => t.id === id);
    if (!existing) {
      return res.status(404).json({ error: 'Template not found' });
    }
    
    if (template.name) existing.name = template.name.trim();
    if (template.level) existing.level = template.level.trim();
    if (template.summary) existing.summary = template.summary.trim();
    if (template.message) existing.message = template.message.trim();
    if (template.background_color) existing.background_color = template.background_color.trim();
    if (template.foreground_color) existing.foreground_color = template.foreground_color.trim();
    if (template.site !== undefined) existing.site = template.site?.trim() || null;
    if (template.alert_contact_name !== undefined) existing.alert_contact_name = template.alert_contact_name?.trim() || null;
    if (template.alert_contact_phone !== undefined) existing.alert_contact_phone = template.alert_contact_phone?.trim() || null;
    if (template.alert_contact_email !== undefined) existing.alert_contact_email = template.alert_contact_email?.trim() || null;
    if (template.alert_contact_teams !== undefined) existing.alert_contact_teams = template.alert_contact_teams?.trim() || null;
    
    await saveDataStore();
    
    res.json({ success: true, template: existing });
  } catch (err) {
    console.error('Error updating template:', err);
    res.status(500).json({ error: 'Failed to update template' });
  }
});

// DELETE /api/templates/:id - Delete a template (admin only)
app.delete('/api/templates/:id', requireAdminKey, async (req, res) => {
  try {
    const { id } = req.params;
    const index = dataStore.templates.findIndex(t => t.id === id);
    
    if (index === -1) {
      return res.status(404).json({ error: 'Template not found' });
    }
    
    dataStore.templates.splice(index, 1);
    await saveDataStore();
    
    res.json({ success: true });
  } catch (err) {
    console.error('Error deleting template:', err);
    res.status(500).json({ error: 'Failed to delete template' });
  }
});

// GET /api/settings - Get settings (admin only)
app.get('/api/settings', requireAdminKey, async (req, res) => {
  try {
    res.json(dataStore.settings || {});
  } catch (err) {
    console.error('Error getting settings:', err);
    res.status(500).json({ error: 'Failed to get settings' });
  }
});

// POST /api/settings - Update settings (admin only)
app.post('/api/settings', requireAdminKey, async (req, res) => {
  try {
    const { default_contact_name, default_contact_phone, default_contact_email, default_contact_teams } = req.body;
    
    if (!dataStore.settings) {
      dataStore.settings = {};
    }
    
    if (default_contact_name !== undefined) dataStore.settings.default_contact_name = default_contact_name || '';
    if (default_contact_phone !== undefined) dataStore.settings.default_contact_phone = default_contact_phone || '';
    if (default_contact_email !== undefined) dataStore.settings.default_contact_email = default_contact_email || '';
    if (default_contact_teams !== undefined) dataStore.settings.default_contact_teams = default_contact_teams || '';
    
    await saveDataStore();
    
    res.json({ success: true, settings: dataStore.settings });
  } catch (err) {
    console.error('Error updating settings:', err);
    res.status(500).json({ error: 'Failed to update settings' });
  }
});

// Static files (must be after API routes)
app.use(express.static('public'));

// Start server
app.listen(PORT, async () => {
  // Ensure admin key and data store are loaded
  await loadAdminKey();
  await loadDataStore();
  
  console.log(`GridBanner Alert Server running on http://localhost:${PORT}`);
  console.log(`Admin key: ${ADMIN_KEY}`);
  console.log(`Config file: ${CONFIG_FILE}`);
  console.log(`Alert file: ${ALERT_FILE}`);
  console.log(`Data file: ${DATA_FILE}`);
  console.log(`Systems tracked: ${Object.keys(dataStore.systems).length}`);
  console.log(`Sites: ${dataStore.sites.length}`);
  console.log(`Templates: ${dataStore.templates.length}`);
  console.log('\nAPI Endpoints:');
  console.log(`  GET  /api/alert          - Get current alert (public)`);
  console.log(`  POST /api/alert          - Create/update alert (admin) OR report system info (client)`);
  console.log(`  DELETE /api/alert        - Clear alert (admin)`);
  console.log(`  GET  /api/systems        - Get all systems (admin)`);
  console.log(`  GET  /api/sites          - Get all sites (admin)`);
  console.log(`  POST /api/sites          - Create site (admin)`);
  console.log(`  PUT  /api/sites/:id      - Update site (admin)`);
  console.log(`  DELETE /api/sites/:id    - Delete site (admin)`);
  console.log(`  GET  /api/templates      - Get all templates (admin)`);
  console.log(`  POST /api/templates      - Create template (admin)`);
  console.log(`  PUT  /api/templates/:id  - Update template (admin)`);
  console.log(`  DELETE /api/templates/:id - Delete template (admin)`);
  console.log(`  POST /api/admin/key     - Change admin key (admin)`);
  console.log(`  GET  /api/admin/key      - Get admin key status (admin)`);
  console.log(`  GET  /                   - Admin web interface`);
});

