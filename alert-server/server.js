const express = require('express');
const bodyParser = require('body-parser');
const fs = require('fs').promises;
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;
const CONFIG_FILE = path.join(__dirname, 'config.json');
const ALERT_FILE = process.env.ALERT_FILE || path.join(__dirname, 'alerts', 'current.json');

// Ensure alerts directory exists
const alertsDir = path.dirname(ALERT_FILE);
fs.mkdir(alertsDir, { recursive: true }).catch(() => {});

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

// Middleware
app.use(bodyParser.json());
app.use(express.static('public'));

// Simple key authentication middleware
function requireAdminKey(req, res, next) {
  const providedKey = req.headers['x-admin-key'] || req.query.admin_key;
  
  // Strict check: must be provided and must match exactly
  if (!providedKey || typeof providedKey !== 'string' || providedKey.trim() !== ADMIN_KEY) {
    return res.status(401).json({ error: 'Unauthorized: Invalid admin key' });
  }
  
  next();
}

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

// POST /api/alert - Create or update alert (requires admin key)
app.post('/api/alert', requireAdminKey, async (req, res) => {
  try {
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
    console.error('Error writing alert:', err);
    res.status(500).json({ error: 'Failed to write alert' });
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

// Start server
app.listen(PORT, async () => {
  // Ensure admin key is loaded
  await loadAdminKey();
  
  console.log(`GridBanner Alert Server running on http://localhost:${PORT}`);
  console.log(`Admin key: ${ADMIN_KEY}`);
  console.log(`Config file: ${CONFIG_FILE}`);
  console.log(`Alert file: ${ALERT_FILE}`);
  console.log('\nAPI Endpoints:');
  console.log(`  GET  /api/alert          - Get current alert (public)`);
  console.log(`  POST /api/alert          - Create/update alert (admin)`);
  console.log(`  DELETE /api/alert        - Clear alert (admin)`);
  console.log(`  GET  /api/alerts/list    - List alerts (admin)`);
  console.log(`  POST /api/admin/key      - Change admin key (admin)`);
  console.log(`  GET  /api/admin/key      - Get admin key status (admin)`);
  console.log(`  GET  /                   - Admin web interface`);
});

