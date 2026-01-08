const express = require('express');
const bodyParser = require('body-parser');
const fs = require('fs').promises;
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;
const ADMIN_KEY = process.env.ADMIN_KEY || 'changeme';
const ALERT_FILE = process.env.ALERT_FILE || path.join(__dirname, 'alerts', 'current.json');

// Ensure alerts directory exists
const alertsDir = path.dirname(ALERT_FILE);
fs.mkdir(alertsDir, { recursive: true }).catch(() => {});

// Middleware
app.use(bodyParser.json());
app.use(express.static('public'));

// Simple key authentication middleware
function requireAdminKey(req, res, next) {
  const providedKey = req.headers['x-admin-key'] || req.query.admin_key;
  if (providedKey === ADMIN_KEY) {
    next();
  } else {
    res.status(401).json({ error: 'Unauthorized: Invalid admin key' });
  }
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

// Start server
app.listen(PORT, () => {
  console.log(`GridBanner Alert Server running on http://localhost:${PORT}`);
  console.log(`Admin key: ${ADMIN_KEY}`);
  console.log(`Alert file: ${ALERT_FILE}`);
  console.log('\nAPI Endpoints:');
  console.log(`  GET  /api/alert          - Get current alert (public)`);
  console.log(`  POST /api/alert          - Create/update alert (admin)`);
  console.log(`  DELETE /api/alert        - Clear alert (admin)`);
  console.log(`  GET  /api/alerts/list    - List alerts (admin)`);
  console.log(`  GET  /                   - Admin web interface`);
});

