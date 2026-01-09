const express = require('express');
const fs = require('fs').promises;
const path = require('path');
const multer = require('multer');

const app = express();
const PORT = process.env.PORT || 3001; // Changed default to 3001 to avoid conflicts

// Middleware
app.use(express.json());
app.use(express.static('public'));

// CORS middleware
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type, X-API-Key');
    if (req.method === 'OPTIONS') {
        return res.sendStatus(200);
    }
    next();
});

// Load configuration
let config;
async function loadConfig() {
    try {
        const configData = await fs.readFile('config.json', 'utf8');
        config = JSON.parse(configData);
    } catch (error) {
        console.error('Error loading config:', error);
        config = { admin_key: 'adminkey' };
    }
}

// File paths
const DATA_FILE = path.join(__dirname, 'data.json');
const ALERT_FILE = path.join(__dirname, 'alerts', 'current.json');
const AUDIO_DIR = path.join(__dirname, 'audio');

// Ensure directories exist
async function ensureDirectories() {
    await fs.mkdir(path.dirname(ALERT_FILE), { recursive: true });
    await fs.mkdir(AUDIO_DIR, { recursive: true });
}

// Load data
async function loadData() {
    try {
        const data = await fs.readFile(DATA_FILE, 'utf8');
        return JSON.parse(data);
    } catch (error) {
        // Return default structure if file doesn't exist
        return {
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

// Save data
async function saveData(data) {
    await fs.writeFile(DATA_FILE, JSON.stringify(data, null, 2), 'utf8');
}

// Load alert
async function loadAlert() {
    try {
        const data = await fs.readFile(ALERT_FILE, 'utf8');
        const alert = JSON.parse(data);
        // Return empty object if alert is effectively empty
        if (!alert || Object.keys(alert).length === 0) {
            return null;
        }
        return alert;
    } catch (error) {
        return null;
    }
}

// Save alert
async function saveAlert(alert) {
    await fs.mkdir(path.dirname(ALERT_FILE), { recursive: true });
    await fs.writeFile(ALERT_FILE, JSON.stringify(alert, null, 2), 'utf8');
}

// API key authentication middleware
function authenticate(req, res, next) {
    const apiKey = req.headers['x-api-key'];
    if (!apiKey || apiKey !== config.admin_key) {
        return res.status(401).json({ error: 'Unauthorized: Invalid API key' });
    }
    next();
}

// Health check (no auth required)
app.get('/api/health', (req, res) => {
    res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// Alert endpoints
app.get('/api/alert', async (req, res) => {
    try {
        const alert = await loadAlert();
        if (!alert) {
            return res.json({});
        }
        res.json(alert);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.post('/api/alert', authenticate, async (req, res) => {
    try {
        const alert = req.body;
        await saveAlert(alert);
        res.json({ success: true, alert });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.delete('/api/alert', authenticate, async (req, res) => {
    try {
        await saveAlert({});
        res.status(204).send();
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Data endpoint (get all data)
app.get('/api/data', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        res.json(data);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Template endpoints
app.get('/api/templates', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        res.json(data.templates || []);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.post('/api/templates', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const template = {
            id: Date.now().toString(),
            ...req.body
        };
        if (!data.templates) {
            data.templates = [];
        }
        data.templates.push(template);
        await saveData(data);
        res.json(template);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.put('/api/templates/:id', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const templateId = req.params.id;
        const templateIndex = data.templates.findIndex(t => t.id === templateId);
        
        if (templateIndex === -1) {
            return res.status(404).json({ error: 'Template not found' });
        }
        
        data.templates[templateIndex] = {
            ...data.templates[templateIndex],
            ...req.body,
            id: templateId
        };
        await saveData(data);
        res.json(data.templates[templateIndex]);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.delete('/api/templates/:id', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const templateId = req.params.id;
        const templateIndex = data.templates.findIndex(t => t.id === templateId);
        
        if (templateIndex === -1) {
            return res.status(404).json({ error: 'Template not found' });
        }
        
        data.templates.splice(templateIndex, 1);
        await saveData(data);
        res.status(204).send();
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Site endpoints
app.get('/api/sites', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        res.json(data.sites || []);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.post('/api/sites', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const site = {
            id: Date.now().toString(),
            name: req.body.name
        };
        if (!data.sites) {
            data.sites = [];
        }
        data.sites.push(site);
        await saveData(data);
        res.json(site);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.put('/api/sites/:id', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const siteId = req.params.id;
        const siteIndex = data.sites.findIndex(s => s.id === siteId);
        
        if (siteIndex === -1) {
            return res.status(404).json({ error: 'Site not found' });
        }
        
        data.sites[siteIndex] = {
            ...data.sites[siteIndex],
            name: req.body.name,
            id: siteId
        };
        await saveData(data);
        res.json(data.sites[siteIndex]);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.delete('/api/sites/:id', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const siteId = req.params.id;
        const siteIndex = data.sites.findIndex(s => s.id === siteId);
        
        if (siteIndex === -1) {
            return res.status(404).json({ error: 'Site not found' });
        }
        
        data.sites.splice(siteIndex, 1);
        await saveData(data);
        res.status(204).send();
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Settings endpoints
app.get('/api/settings', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        res.json(data.settings || {});
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.put('/api/settings', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        data.settings = {
            ...data.settings,
            ...req.body
        };
        await saveData(data);
        res.json(data.settings);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Audio file endpoints
app.get('/api/audio', authenticate, async (req, res) => {
    try {
        const files = await fs.readdir(AUDIO_DIR);
        const audioFiles = await Promise.all(
            files
                .filter(file => /\.(mp3|wav|ogg|m4a)$/i.test(file))
                .map(async (file) => {
                    const stats = await fs.stat(path.join(AUDIO_DIR, file));
                    return {
                        name: file,
                        size: stats.size,
                        modified: stats.mtime.toISOString()
                    };
                })
        );
        res.json(audioFiles);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Configure multer for file uploads
const storage = multer.diskStorage({
    destination: (req, file, cb) => {
        cb(null, AUDIO_DIR);
    },
    filename: (req, file, cb) => {
        const fileName = req.body.name || file.originalname;
        cb(null, fileName);
    }
});

const upload = multer({ storage });

app.post('/api/audio', authenticate, upload.single('file'), async (req, res) => {
    try {
        if (!req.file) {
            return res.status(400).json({ error: 'No file uploaded' });
        }
        res.json({
            success: true,
            name: req.file.filename,
            size: req.file.size
        });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

app.put('/api/audio/:name', authenticate, async (req, res) => {
    try {
        const oldName = decodeURIComponent(req.params.name);
        const newName = req.body.name;
        
        if (!newName) {
            return res.status(400).json({ error: 'New name is required' });
        }
        
        const oldPath = path.join(AUDIO_DIR, oldName);
        const newPath = path.join(AUDIO_DIR, newName);
        
        await fs.access(oldPath); // Check if file exists
        await fs.rename(oldPath, newPath);
        
        res.json({ success: true, name: newName });
    } catch (error) {
        if (error.code === 'ENOENT') {
            return res.status(404).json({ error: 'Audio file not found' });
        }
        res.status(500).json({ error: error.message });
    }
});

app.delete('/api/audio/:name', authenticate, async (req, res) => {
    try {
        const fileName = decodeURIComponent(req.params.name);
        const filePath = path.join(AUDIO_DIR, fileName);
        
        await fs.unlink(filePath);
        res.status(204).send();
    } catch (error) {
        if (error.code === 'ENOENT') {
            return res.status(404).json({ error: 'Audio file not found' });
        }
        res.status(500).json({ error: error.message });
    }
});

// Serve audio files
app.get('/api/audio/:name', async (req, res) => {
    try {
        const fileName = decodeURIComponent(req.params.name);
        const filePath = path.join(AUDIO_DIR, fileName);
        
        // Security: ensure file is in audio directory
        const resolvedPath = path.resolve(filePath);
        const resolvedDir = path.resolve(AUDIO_DIR);
        if (!resolvedPath.startsWith(resolvedDir)) {
            return res.status(403).json({ error: 'Access denied' });
        }
        
        res.sendFile(filePath);
    } catch (error) {
        res.status(404).json({ error: 'Audio file not found' });
    }
});

// System registration endpoint (for GridBanner clients)
app.post('/api/systems', async (req, res) => {
    try {
        const systemInfo = req.body;
        const workstationName = systemInfo.workstation_name;
        
        if (!workstationName) {
            return res.status(400).json({ error: 'workstation_name is required' });
        }
        
        const data = await loadData();
        if (!data.systems) {
            data.systems = {};
        }
        
        data.systems[workstationName] = {
            ...systemInfo,
            last_seen: new Date().toISOString()
        };
        
        await saveData(data);
        res.json({ success: true });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Start server
async function startServer() {
    await loadConfig();
    await ensureDirectories();
    
    const server = app.listen(PORT, () => {
        console.log(`GridBanner Alert Server running on port ${PORT}`);
        console.log(`Admin API Key: ${config.admin_key}`);
        console.log(`Health check: http://localhost:${PORT}/api/health`);
    });
    
    server.on('error', (error) => {
        if (error.code === 'EADDRINUSE') {
            console.error(`\nError: Port ${PORT} is already in use.`);
            console.error(`Please either:`);
            console.error(`  1. Stop the process using port ${PORT}`);
            console.error(`  2. Set PORT environment variable to use a different port (e.g., set PORT=3001)`);
            console.error(`\nTo find what's using port ${PORT}, run:`);
            console.error(`  netstat -ano | findstr :${PORT}`);
            process.exit(1);
        } else {
            throw error;
        }
    });
}

startServer().catch(console.error);

