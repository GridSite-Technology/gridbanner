const express = require('express');
const fs = require('fs').promises;
const path = require('path');
const multer = require('multer');
const crypto = require('crypto');
const jwt = require('jsonwebtoken');
const jwksClient = require('jwks-rsa');

const app = express();
const PORT = process.env.PORT || 3000;

// Challenge store (in-memory, expires after 5 minutes)
const pendingChallenges = new Map();
const CHALLENGE_EXPIRY_MS = 5 * 60 * 1000; // 5 minutes

// Clean up expired challenges periodically
setInterval(() => {
    const now = Date.now();
    for (const [key, value] of pendingChallenges.entries()) {
        if (now - value.timestamp > CHALLENGE_EXPIRY_MS) {
            pendingChallenges.delete(key);
        }
    }
}, 60 * 1000); // Clean every minute

// Middleware
app.use(express.json());
app.use(express.static('public'));

// CORS middleware
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.header('Access-Control-Allow-Headers', 'Content-Type, X-API-Key, Authorization');
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

// Azure AD configuration
const AZURE_TENANT_ID = process.env.AZURE_TENANT_ID || config?.azure_tenant_id;
const AZURE_CLIENT_ID = process.env.AZURE_CLIENT_ID || config?.azure_client_id;
const AZURE_AUTH_ENABLED = (process.env.AZURE_AUTH_ENABLED === 'true' || config?.azure_auth_enabled === true) && AZURE_TENANT_ID && AZURE_CLIENT_ID;

// Azure AD token validation setup
let azureJwksClient = null;
if (AZURE_AUTH_ENABLED) {
    const JWKS_URI = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/discovery/v2.0/keys`;
    azureJwksClient = jwksClient({
        jwksUri: JWKS_URI,
        requestHeaders: {},
        timeout: 30000
    });
    console.log('Azure AD authentication enabled');
} else {
    console.log('Azure AD authentication disabled');
}

// Function to get signing key for JWT verification
function getAzureSigningKey(header, callback) {
    if (!azureJwksClient) {
        return callback(new Error('Azure AD not configured'));
    }
    azureJwksClient.getSigningKey(header.kid, (err, key) => {
        if (err) {
            return callback(err);
        }
        const signingKey = key.publicKey || key.rsaPublicKey;
        callback(null, signingKey);
    });
}

// Middleware to validate Azure AD tokens (optional if auth is enabled)
function validateAzureToken(req, res, next) {
    // If Azure AD is disabled, skip validation
    if (!AZURE_AUTH_ENABLED) {
        return next();
    }
    
    const authHeader = req.headers.authorization;
    
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
        return res.status(401).json({ error: 'Missing or invalid Authorization header. Azure AD authentication required.' });
    }
    
    const token = authHeader.substring(7);
    
    const options = {
        audience: `api://${AZURE_CLIENT_ID}`,
        issuer: `https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`,
        algorithms: ['RS256']
    };
    
    jwt.verify(token, getAzureSigningKey, options, (err, decoded) => {
        if (err) {
            console.error('Token validation error:', err);
            return res.status(401).json({ error: 'Invalid or expired token' });
        }
        
        // Attach user info to request
        req.azureUser = {
            upn: decoded.upn || decoded.preferred_username,
            email: decoded.email,
            oid: decoded.oid, // Object ID (unique user identifier)
            name: decoded.name
        };
        
        console.log(`Token validated for user: ${req.azureUser.upn} (OID: ${req.azureUser.oid})`);
        next();
    });
}

// Middleware to verify user matches request
function verifyUserMatch(req, res, next) {
    // If Azure AD is disabled, skip verification
    if (!AZURE_AUTH_ENABLED || !req.azureUser) {
        return next();
    }
    
    const requestedUsername = decodeURIComponent(req.params.username);
    const authenticatedUser = req.azureUser.upn || req.azureUser.email;
    
    if (!authenticatedUser) {
        return res.status(401).json({ error: 'Unable to determine authenticated user identity' });
    }
    
    // Normalize usernames (handle email vs UPN)
    const normalizedRequested = requestedUsername.toLowerCase().trim();
    const normalizedAuthenticated = authenticatedUser.toLowerCase().trim();
    
    if (normalizedRequested !== normalizedAuthenticated) {
        console.warn(`User mismatch: requested=${requestedUsername}, authenticated=${authenticatedUser}`);
        return res.status(403).json({ 
            error: 'Forbidden: You can only manage your own keys',
            requested: requestedUsername,
            authenticated: authenticatedUser
        });
    }
    
    next();
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

// Get alert server URL for organization (public endpoint, returns configured URL)
app.get('/api/alert-server-url', async (req, res) => {
    try {
        const data = await loadData();
        const alertServerUrl = data.settings?.alert_server_url || null;
        res.json({ 
            alert_server_url: alertServerUrl,
            configured: alertServerUrl !== null
        });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Set alert server URL (admin only)
app.post('/api/admin/alert-server-url', authenticate, async (req, res) => {
    try {
        const { alert_server_url } = req.body;
        
        if (!alert_server_url || typeof alert_server_url !== 'string') {
            return res.status(400).json({ error: 'alert_server_url is required and must be a string' });
        }
        
        // Validate URL format
        try {
            new URL(alert_server_url);
        } catch {
            return res.status(400).json({ error: 'Invalid URL format' });
        }
        
        const data = await loadData();
        if (!data.settings) {
            data.settings = {};
        }
        data.settings.alert_server_url = alert_server_url;
        await saveData(data);
        
        console.log(`Alert server URL updated to: ${alert_server_url}`);
        res.json({ 
            success: true, 
            alert_server_url: alert_server_url,
            message: 'Alert server URL updated successfully'
        });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
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

// Serve audio files (for web preview)
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

// Audio download endpoint (for GridBanner clients)
app.get('/api/audio/:name/download', async (req, res) => {
    try {
        const fileName = decodeURIComponent(req.params.name);
        const filePath = path.join(AUDIO_DIR, fileName);
        
        // Security: ensure file is in audio directory
        const resolvedPath = path.resolve(filePath);
        const resolvedDir = path.resolve(AUDIO_DIR);
        if (!resolvedPath.startsWith(resolvedDir)) {
            return res.status(403).json({ error: 'Access denied' });
        }
        
        // Check file exists
        await fs.access(filePath);
        
        // Set download headers
        res.setHeader('Content-Disposition', `attachment; filename="${fileName}"`);
        res.sendFile(resolvedPath);
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

// ============================================
// User Keyring Management Endpoints
// ============================================

// Get all users with their key counts (admin only)
app.get('/api/users', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const users = data.users || {};
        
        // Return list of users with key counts
        const userList = Object.entries(users).map(([username, userData]) => ({
            username,
            display_name: userData.display_name || username,
            key_count: (userData.public_keys || []).length,
            last_seen: userData.last_seen
        }));
        
        res.json(userList);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get a specific user's public keys (admin only)
app.get('/api/users/:username/keys', authenticate, async (req, res) => {
    try {
        const username = decodeURIComponent(req.params.username);
        const data = await loadData();
        const users = data.users || {};
        const user = users[username];
        
        if (!user) {
            return res.json([]);
        }
        
        res.json(user.public_keys || []);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get all public keys for a user
app.get('/api/keyring/:username', 
    validateAzureToken,      // Validate Azure AD token if enabled
    verifyUserMatch,         // Ensure user matches username in URL
    async (req, res) => {
    try {
        // Use authenticated user if available, otherwise fall back to URL param
        const username = req.azureUser?.upn || req.azureUser?.email || decodeURIComponent(req.params.username);
        const data = await loadData();
        const users = data.users || {};
        const user = users[username];
        
        if (!user) {
            return res.json({ keys: [] });
        }
        
        res.json({ 
            username,
            keys: user.public_keys || []
        });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Request a challenge for key ownership verification
app.post('/api/keyring/:username/challenge', 
    validateAzureToken,      // Validate Azure AD token if enabled (optional)
    async (req, res) => {
    try {
        // Use authenticated user if available, otherwise fall back to URL param
        const username = req.azureUser?.upn || req.azureUser?.email || decodeURIComponent(req.params.username);
        const { fingerprint } = req.body;
        
        if (!fingerprint) {
            return res.status(400).json({ error: 'fingerprint is required' });
        }
        
        // Generate a random challenge (32 bytes, base64 encoded)
        const challengeBytes = crypto.randomBytes(32);
        const challenge = challengeBytes.toString('base64');
        
        // Store the challenge with username+fingerprint as key
        const challengeKey = `${username}:${fingerprint}`;
        pendingChallenges.set(challengeKey, {
            challenge,
            timestamp: Date.now()
        });
        
        console.log(`Challenge generated for ${username} key ${fingerprint}: ${challenge.substring(0, 20)}...`);
        
        res.json({ challenge });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Upload a public key with proof of possession (from GridBanner client)
app.post('/api/keyring/:username/keys', 
    validateAzureToken,      // Validate Azure AD token if enabled
    verifyUserMatch,         // Ensure user matches username in URL
    async (req, res) => {
    try {
        console.log(`[KEYRING] Upload request received for user: ${req.params.username}`);
        console.log(`[KEYRING] Authenticated user: ${req.azureUser?.upn || req.azureUser?.email || 'none'}`);
        
        // Use authenticated user if available, otherwise fall back to URL param
        const username = req.azureUser?.upn || req.azureUser?.email || decodeURIComponent(req.params.username);
        const { key_type, key_data, key_name, fingerprint, challenge, signature } = req.body;
        
        console.log(`[KEYRING] Key upload request: type=${key_type}, name=${key_name}, fingerprint=${fingerprint?.substring(0, 20)}..., hasChallenge=${!!challenge}, hasSignature=${!!signature}`);
        
        if (!key_type || !key_data) {
            console.log(`[KEYRING] Missing required fields: key_type=${!!key_type}, key_data=${!!key_data}`);
            return res.status(400).json({ error: 'key_type and key_data are required' });
        }
        
        // Verify proof of possession if challenge/signature provided
        if (challenge && signature && fingerprint) {
            console.log(`[KEYRING] Verifying proof of possession for key ${fingerprint.substring(0, 20)}...`);
            const challengeKey = `${username}:${fingerprint}`;
            const storedChallenge = pendingChallenges.get(challengeKey);
            
            if (!storedChallenge) {
                console.log(`[KEYRING] No pending challenge found for ${challengeKey}`);
                return res.status(400).json({ error: 'No pending challenge found. Request a new challenge.' });
            }
            
            // Check if challenge expired
            if (Date.now() - storedChallenge.timestamp > CHALLENGE_EXPIRY_MS) {
                console.log(`[KEYRING] Challenge expired for ${challengeKey}`);
                pendingChallenges.delete(challengeKey);
                return res.status(400).json({ error: 'Challenge expired. Request a new challenge.' });
            }
            
            // Verify the challenge matches
            if (storedChallenge.challenge !== challenge) {
                console.log(`[KEYRING] Challenge mismatch for ${challengeKey}`);
                return res.status(400).json({ error: 'Challenge mismatch.' });
            }
            
            // Verify the signature using the public key
            try {
                console.log(`[KEYRING] Verifying signature...`);
                const verifyStartTime = Date.now();
                const isValid = verifySSHSignature(key_data, challenge, signature);
                const verifyDuration = Date.now() - verifyStartTime;
                console.log(`[KEYRING] Signature verification ${isValid ? 'PASSED' : 'FAILED'} in ${verifyDuration}ms`);
                
                if (!isValid) {
                    return res.status(400).json({ error: 'Invalid signature. Proof of possession failed.' });
                }
                console.log(`[KEYRING] Signature verified for ${username} key ${fingerprint.substring(0, 20)}...`);
            } catch (verifyError) {
                console.error(`[KEYRING] Signature verification error:`, verifyError);
                return res.status(400).json({ error: `Signature verification failed: ${verifyError.message}` });
            }
            
            // Clean up the used challenge
            pendingChallenges.delete(challengeKey);
            console.log(`[KEYRING] Challenge cleaned up for ${challengeKey}`);
        } else {
            console.log(`[KEYRING] No challenge/signature provided - uploading without verification`);
        }
        
        const data = await loadData();
        if (!data.users) {
            data.users = {};
        }
        if (!data.users[username]) {
            data.users[username] = {
                display_name: username,
                public_keys: [],
                last_seen: new Date().toISOString()
            };
        }
        
        // Check if key already exists (by fingerprint or key_data)
        const existingKeys = data.users[username].public_keys || [];
        const keyExists = existingKeys.some(k => 
            (fingerprint && k.fingerprint === fingerprint) || 
            k.key_data === key_data
        );
        
        if (keyExists) {
            return res.json({ success: true, message: 'Key already exists' });
        }
        
        // Add new key (mark as verified if signature was provided)
        const newKey = {
            id: Date.now().toString(),
            key_type,
            key_data,
            key_name: key_name || `${key_type} key`,
            fingerprint: fingerprint || null,
            verified: !!(challenge && signature),
            uploaded_at: new Date().toISOString()
        };
        
        data.users[username].public_keys.push(newKey);
        data.users[username].last_seen = new Date().toISOString();
        
        await saveData(data);
        console.log(`[KEYRING] Key ${key_name} (${fingerprint?.substring(0, 20)}...) successfully uploaded for ${username}`);
        res.json({ success: true, key: newKey });
    } catch (error) {
        console.error(`[KEYRING] Error uploading key:`, error);
        res.status(500).json({ error: error.message });
    }
});

// Delete a public key (authenticated users only)
app.delete('/api/keyring/:username/keys/:keyId',
    validateAzureToken,      // Validate Azure AD token if enabled
    verifyUserMatch,         // Ensure user matches username in URL
    async (req, res) => {
    try {
        // Use authenticated user if available, otherwise fall back to URL param
        const username = req.azureUser?.upn || req.azureUser?.email || decodeURIComponent(req.params.username);
        const keyId = req.params.keyId;
        
        const data = await loadData();
        if (!data.users || !data.users[username]) {
            return res.status(404).json({ error: 'User not found' });
        }
        
        const keys = data.users[username].public_keys || [];
        const keyIndex = keys.findIndex(k => k.id === keyId);
        
        if (keyIndex === -1) {
            return res.status(404).json({ error: 'Key not found' });
        }
        
        const deletedKey = keys[keyIndex];
        keys.splice(keyIndex, 1);
        await saveData(data);
        
        console.log(`Key ${keyId} deleted for user ${username}`);
        res.status(204).send();
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Helper function to verify SSH signature
function verifySSHSignature(publicKeyData, challenge, signatureBase64) {
    // Parse the SSH public key format: "type base64data comment"
    const parts = publicKeyData.trim().split(' ');
    if (parts.length < 2) {
        throw new Error('Invalid SSH public key format');
    }
    
    const keyType = parts[0];
    const keyDataBase64 = parts[1];
    
    // Convert challenge and signature
    const challengeBuffer = Buffer.from(challenge, 'base64');
    const signatureBuffer = Buffer.from(signatureBase64, 'base64');
    
    // Handle different key types
    if (keyType === 'ssh-rsa') {
        // Parse RSA public key from SSH format
        const keyBuffer = Buffer.from(keyDataBase64, 'base64');
        const pemKey = sshRsaToPem(keyBuffer);
        
        const verify = crypto.createVerify('SHA256');
        verify.update(challengeBuffer);
        return verify.verify(pemKey, signatureBuffer);
    } else if (keyType === 'ssh-ed25519') {
        // Ed25519 verification
        const keyBuffer = Buffer.from(keyDataBase64, 'base64');
        const publicKey = parseEd25519PublicKey(keyBuffer);
        
        // Use crypto.verify for Ed25519
        return crypto.verify(null, challengeBuffer, {
            key: publicKey,
            format: 'der',
            type: 'spki'
        }, signatureBuffer);
    } else if (keyType.startsWith('ecdsa-sha2-')) {
        // ECDSA verification
        const keyBuffer = Buffer.from(keyDataBase64, 'base64');
        const pemKey = sshEcdsaToPem(keyBuffer, keyType);
        
        const verify = crypto.createVerify('SHA256');
        verify.update(challengeBuffer);
        return verify.verify(pemKey, signatureBuffer);
    } else {
        throw new Error(`Unsupported key type: ${keyType}`);
    }
}

// Convert SSH RSA public key to PEM format
function sshRsaToPem(keyBuffer) {
    let offset = 0;
    
    // Read key type length and type
    const typeLen = keyBuffer.readUInt32BE(offset);
    offset += 4 + typeLen;
    
    // Read exponent
    const eLen = keyBuffer.readUInt32BE(offset);
    offset += 4;
    const e = keyBuffer.slice(offset, offset + eLen);
    offset += eLen;
    
    // Read modulus
    const nLen = keyBuffer.readUInt32BE(offset);
    offset += 4;
    const n = keyBuffer.slice(offset, offset + nLen);
    
    // Create DER-encoded RSA public key
    const rsaKey = crypto.createPublicKey({
        key: {
            kty: 'RSA',
            n: n.toString('base64url'),
            e: e.toString('base64url')
        },
        format: 'jwk'
    });
    
    return rsaKey.export({ type: 'spki', format: 'pem' });
}

// Parse Ed25519 public key from SSH format to DER SPKI
function parseEd25519PublicKey(keyBuffer) {
    let offset = 0;
    
    // Read key type length and type
    const typeLen = keyBuffer.readUInt32BE(offset);
    offset += 4 + typeLen;
    
    // Read public key (32 bytes)
    const pkLen = keyBuffer.readUInt32BE(offset);
    offset += 4;
    const publicKeyBytes = keyBuffer.slice(offset, offset + pkLen);
    
    // Create Ed25519 public key object
    const key = crypto.createPublicKey({
        key: {
            kty: 'OKP',
            crv: 'Ed25519',
            x: publicKeyBytes.toString('base64url')
        },
        format: 'jwk'
    });
    
    return key.export({ type: 'spki', format: 'der' });
}

// Convert SSH ECDSA public key to PEM format
function sshEcdsaToPem(keyBuffer, keyType) {
    let offset = 0;
    
    // Read key type
    const typeLen = keyBuffer.readUInt32BE(offset);
    offset += 4 + typeLen;
    
    // Read curve identifier
    const curveLen = keyBuffer.readUInt32BE(offset);
    offset += 4;
    const curve = keyBuffer.slice(offset, offset + curveLen).toString();
    offset += curveLen;
    
    // Read public key point
    const pointLen = keyBuffer.readUInt32BE(offset);
    offset += 4;
    const point = keyBuffer.slice(offset, offset + pointLen);
    
    // Map curve names
    const curveMap = {
        'nistp256': 'P-256',
        'nistp384': 'P-384',
        'nistp521': 'P-521'
    };
    
    const jwkCurve = curveMap[curve];
    if (!jwkCurve) {
        throw new Error(`Unsupported ECDSA curve: ${curve}`);
    }
    
    // Parse the uncompressed point (0x04 + x + y)
    if (point[0] !== 0x04) {
        throw new Error('Expected uncompressed point format');
    }
    
    const coordLen = (point.length - 1) / 2;
    const x = point.slice(1, 1 + coordLen);
    const y = point.slice(1 + coordLen);
    
    const key = crypto.createPublicKey({
        key: {
            kty: 'EC',
            crv: jwkCurve,
            x: x.toString('base64url'),
            y: y.toString('base64url')
        },
        format: 'jwk'
    });
    
    return key.export({ type: 'spki', format: 'pem' });
}

// Delete a public key (admin only)
app.delete('/api/users/:username/keys/:keyId', authenticate, async (req, res) => {
    try {
        const username = decodeURIComponent(req.params.username);
        const keyId = req.params.keyId;
        
        const data = await loadData();
        if (!data.users || !data.users[username]) {
            return res.status(404).json({ error: 'User not found' });
        }
        
        const keys = data.users[username].public_keys || [];
        const keyIndex = keys.findIndex(k => k.id === keyId);
        
        if (keyIndex === -1) {
            return res.status(404).json({ error: 'Key not found' });
        }
        
        keys.splice(keyIndex, 1);
        await saveData(data);
        res.status(204).send();
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Delete a user (admin only)
app.delete('/api/users/:username', authenticate, async (req, res) => {
    try {
        const username = decodeURIComponent(req.params.username);
        
        const data = await loadData();
        if (!data.users || !data.users[username]) {
            return res.status(404).json({ error: 'User not found' });
        }
        
        delete data.users[username];
        await saveData(data);
        res.status(204).send();
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

