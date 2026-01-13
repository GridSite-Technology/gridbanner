const express = require('express');
const fs = require('fs').promises;
const path = require('path');
const multer = require('multer');
const crypto = require('crypto');
const jwt = require('jsonwebtoken');
const jwksClient = require('jwks-rsa');
const { Client } = require('@microsoft/microsoft-graph-client');
const { ClientSecretCredential } = require('@azure/identity');

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

// Azure AD configuration (will be initialized after config is loaded)
let AZURE_TENANT_ID;
let AZURE_CLIENT_ID;
let AZURE_CLIENT_SECRET;
let AZURE_AUTH_ENABLED = false;
let graphClient = null;
let azureJwksClient = null;

// Initialize Azure AD configuration
function initializeAzureAD() {
    AZURE_TENANT_ID = process.env.AZURE_TENANT_ID || config?.azure_tenant_id;
    AZURE_CLIENT_ID = process.env.AZURE_CLIENT_ID || config?.azure_client_id;
    AZURE_CLIENT_SECRET = process.env.AZURE_CLIENT_SECRET || config?.azure_client_secret;
    AZURE_AUTH_ENABLED = (process.env.AZURE_AUTH_ENABLED === 'true' || config?.azure_auth_enabled === true) && AZURE_TENANT_ID && AZURE_CLIENT_ID;

    // Azure Graph API client (for querying groups)
    graphClient = null;
    if (AZURE_AUTH_ENABLED && AZURE_CLIENT_SECRET) {
        try {
            const credential = new ClientSecretCredential(
                AZURE_TENANT_ID,
                AZURE_CLIENT_ID,
                AZURE_CLIENT_SECRET
            );
            
            // Create Graph client with authentication provider
            graphClient = Client.initWithMiddleware({
                authProvider: {
                    getAccessToken: async () => {
                        const tokenResponse = await credential.getToken(['https://graph.microsoft.com/.default']);
                        return tokenResponse.token;
                    }
                }
            });
            console.log('Azure Graph API client initialized');
        } catch (error) {
            console.error('Failed to initialize Azure Graph API client:', error.message);
            console.log('Group queries will be unavailable');
        }
    }

    // Azure AD token validation setup
    azureJwksClient = null;
    if (AZURE_AUTH_ENABLED) {
        const JWKS_URI = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/discovery/v2.0/keys`;
        azureJwksClient = jwksClient({
            jwksUri: JWKS_URI,
            requestHeaders: {},
            timeout: 30000
        });
        console.log('Azure AD authentication enabled');
        console.log(`  Tenant ID: ${AZURE_TENANT_ID}`);
        console.log(`  Client ID: ${AZURE_CLIENT_ID}`);
        console.log(`  Graph API: ${graphClient ? 'Enabled' : 'Disabled (missing client secret)'}`);
    } else {
        console.log('Azure AD authentication disabled');
        if (!AZURE_TENANT_ID || !AZURE_CLIENT_ID) {
            console.log('  Reason: Missing tenant ID or client ID in config');
        } else if (config?.azure_auth_enabled !== true && process.env.AZURE_AUTH_ENABLED !== 'true') {
            console.log('  Reason: azure_auth_enabled is not set to true');
        }
    }
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
    
    const expectedAudience = `api://${AZURE_CLIENT_ID}`;
    const expectedIssuerV2 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`;
    const expectedIssuerV1 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/`;
    const expectedIssuerV1Alt = `https://sts.windows.net/${AZURE_TENANT_ID}/`;
    
    // Accept both v1.0 and v2.0 issuers (Azure AD can issue tokens from either endpoint)
    // We'll validate issuer manually after verification since jsonwebtoken only accepts string/array
    const options = {
        audience: expectedAudience,
        issuer: false, // Disable issuer validation, we'll check it manually
        algorithms: ['RS256']
    };
    
    // Debug: Log expected values on first token validation attempt
    if (!validateAzureToken._debugLogged) {
        console.log(`Token validation configured:`);
        console.log(`  Expected audience: ${expectedAudience}`);
        console.log(`  Accepted issuers:`);
        console.log(`    - ${expectedIssuerV2} (v2.0)`);
        console.log(`    - ${expectedIssuerV1} (v1.0)`);
        console.log(`    - ${expectedIssuerV1Alt} (v1.0 alt)`);
        console.log(`    - Any issuer starting with https://login.microsoftonline.com/${AZURE_TENANT_ID}/`);
        console.log(`    - Any issuer starting with https://sts.windows.net/${AZURE_TENANT_ID}/`);
        console.log(`  API Client ID (Application ID): ${AZURE_CLIENT_ID}`);
        console.log(`  Tenant ID: ${AZURE_TENANT_ID}`);
        validateAzureToken._debugLogged = true;
    }
    
    jwt.verify(token, getAzureSigningKey, options, (err, decoded) => {
        if (err) {
            console.error('Token validation error:', err.message);
            // Decode token without verification to see what it contains
            try {
                const decodedUnverified = jwt.decode(token, { complete: true });
                if (decodedUnverified?.payload) {
                    const payload = decodedUnverified.payload;
                    console.error('Token details:');
                    console.error(`  Expected audience: api://${AZURE_CLIENT_ID}`);
                    console.error(`  Token audience: ${payload.aud || 'none'}`);
                    console.error(`  Expected issuer: https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`);
                    console.error(`  Token issuer: ${payload.iss || 'none'}`);
                    console.error(`  Token tenant (tid): ${payload.tid || 'none'}`);
                    console.error(`  Token scopes: ${payload.scp || payload.roles || 'none'}`);
                    console.error(`  Token appid: ${payload.appid || 'none'}`);
                    console.error(`  Token azp: ${payload.azp || 'none'}`);
                    
                    // Check if issuer is v1.0 instead of v2.0
                    if (payload.iss && payload.iss.includes('/v1.0') && !payload.iss.includes('/v2.0')) {
                        console.error('  NOTE: Token uses v1.0 endpoint, but server expects v2.0');
                    }
                    
                    // Check if tenant ID matches
                    if (payload.tid && payload.tid !== AZURE_TENANT_ID) {
                        console.error(`  WARNING: Token tenant (${payload.tid}) does not match expected tenant (${AZURE_TENANT_ID})`);
                    }
                }
            } catch (decodeErr) {
                console.error(`  Could not decode token for debugging: ${decodeErr.message}`);
            }
            return res.status(401).json({ error: 'Invalid or expired token' });
        }
        
        // Manually validate issuer (accept v1.0, v2.0, and alternative formats)
        const tokenIssuer = decoded.iss;
        const isValidIssuer = tokenIssuer === expectedIssuerV2 || 
                             tokenIssuer === expectedIssuerV1 ||
                             tokenIssuer === expectedIssuerV1Alt ||
                             (tokenIssuer && tokenIssuer.startsWith(`https://login.microsoftonline.com/${AZURE_TENANT_ID}/`)) ||
                             (tokenIssuer && tokenIssuer.startsWith(`https://sts.windows.net/${AZURE_TENANT_ID}/`));
        
        if (!isValidIssuer) {
            console.error(`Token issuer validation failed:`);
            console.error(`  Token issuer: ${tokenIssuer}`);
            console.error(`  Expected: ${expectedIssuerV2} or ${expectedIssuerV1} or ${expectedIssuerV1Alt}`);
            return res.status(401).json({ error: 'Invalid token issuer' });
        }
        
        // Attach user info to request
        req.azureUser = {
            upn: decoded.upn || decoded.preferred_username,
            email: decoded.email,
            oid: decoded.oid, // Object ID (unique user identifier)
            name: decoded.name
        };
        
            console.log(`[${new Date().toISOString()}] Token validated for user: ${req.azureUser.upn} (OID: ${req.azureUser.oid})`);
            console.log(`[${new Date().toISOString()}]   Token audience: ${decoded.aud}`);
            console.log(`[${new Date().toISOString()}]   Token issuer: ${decoded.iss}`);
            console.log(`[${new Date().toISOString()}]   Token scopes: ${decoded.scp || decoded.roles || 'none'}`);
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
// Store active admin sessions (token -> user info)
const adminSessions = new Map();

// Authenticate middleware - accepts either admin key OR Azure AD token (for Global Admins)
async function authenticate(req, res, next) {
    const apiKey = req.headers['x-api-key'];
    const authHeader = req.headers.authorization;
    
    // Check admin key first (backward compatible)
    if (apiKey && apiKey === config.admin_key) {
            req.isAdmin = true;
            req.authMethod = 'admin_key';
            console.log(`[${new Date().toISOString()}] Authenticated request using admin key from IP: ${req.ip || req.connection.remoteAddress}`);
            return next();
    }
    
    // Check for Azure AD token (for Global Administrators)
    if (AZURE_AUTH_ENABLED && authHeader && authHeader.startsWith('Bearer ')) {
        const token = authHeader.substring(7);
        
        // Check if we have a cached session
        if (adminSessions.has(token)) {
            const session = adminSessions.get(token);
            req.isAdmin = true;
            req.authMethod = 'azure_ad';
            req.azureUser = session.user;
            console.log(`[${new Date().toISOString()}] Authenticated request from cached session: ${session.user.name || session.user.upn || session.user.email} (OID: ${session.user.oid})`);
            return next();
        }
        
        // Validate token and check if user is Global Administrator
        try {
            const expectedAudience = `api://${AZURE_CLIENT_ID}`;
            const options = {
                audience: expectedAudience,
                issuer: false,
                algorithms: ['RS256']
            };
            
            const decoded = await new Promise((resolve, reject) => {
                jwt.verify(token, getAzureSigningKey, options, (err, decoded) => {
                    if (err) reject(err);
                    else resolve(decoded);
                });
            });
            
            // Validate issuer
            const tokenIssuer = decoded.iss;
            const expectedIssuerV2 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`;
            const expectedIssuerV1 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/`;
            const expectedIssuerV1Alt = `https://sts.windows.net/${AZURE_TENANT_ID}/`;
            
            const isValidIssuer = tokenIssuer === expectedIssuerV2 || 
                                 tokenIssuer === expectedIssuerV1 ||
                                 tokenIssuer === expectedIssuerV1Alt ||
                                 (tokenIssuer && tokenIssuer.startsWith(`https://login.microsoftonline.com/${AZURE_TENANT_ID}/`)) ||
                                 (tokenIssuer && tokenIssuer.startsWith(`https://sts.windows.net/${AZURE_TENANT_ID}/`));
            
            if (!isValidIssuer) {
                return res.status(401).json({ error: 'Invalid token issuer' });
            }
            
            // Check if user is Global Administrator
            const userOid = decoded.oid;
            const userName = decoded.upn || decoded.preferred_username || decoded.email || decoded.name || 'Unknown';
            const userDisplayName = decoded.name || userName;
            
            console.log(`[${new Date().toISOString()}] Checking Global Administrator status for user: ${userDisplayName} (${userName}, OID: ${userOid})`);
            
            const isGlobalAdmin = await isGlobalAdministrator(userOid);
            
            if (!isGlobalAdmin) {
                console.log(`[${new Date().toISOString()}] Access denied for user: ${userDisplayName} (${userName}, OID: ${userOid}) - Not a Global Administrator`);
                return res.status(403).json({ error: 'Access denied. Global Administrator role required.' });
            }
            
            console.log(`[${new Date().toISOString()}] Access granted to Global Administrator: ${userDisplayName} (${userName}, OID: ${userOid})`);
            
            // Cache the session
            const userInfo = {
                upn: decoded.upn || decoded.preferred_username,
                email: decoded.email,
                oid: userOid,
                name: decoded.name
            };
            
            adminSessions.set(token, {
                user: userInfo,
                expiresAt: decoded.exp * 1000 // Convert to milliseconds
            });
            
            // Clean up expired sessions periodically
            const now = Date.now();
            for (const [sessionToken, session] of adminSessions.entries()) {
                if (session.expiresAt < now) {
                    adminSessions.delete(sessionToken);
                }
            }
            
            req.isAdmin = true;
            req.authMethod = 'azure_ad';
            req.azureUser = userInfo;
            
            console.log(`[${new Date().toISOString()}] Admin access granted via Azure AD for Global Administrator: ${userInfo.name || userInfo.upn || userInfo.email} (OID: ${userInfo.oid})`);
            return next();
            
        } catch (error) {
            console.error('Azure AD admin authentication failed:', error.message);
            return res.status(401).json({ error: 'Invalid or expired token' });
        }
    }
    
    // No valid authentication method found
    return res.status(401).json({ error: 'Unauthorized: Invalid API key or token' });
}

// Health check (no auth required)
app.get('/api/health', (req, res) => {
    res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// Get Azure AD config for frontend (public, no auth required)
app.get('/api/auth/config', (req, res) => {
    if (!AZURE_AUTH_ENABLED) {
        return res.json({ enabled: false });
    }
    
    // Return config needed for frontend authentication
    // We need the DESKTOP client ID for MSAL (public client)
    // This should be configured in config.json as azure_desktop_client_id
    const desktopClientId = config?.azure_desktop_client_id || process.env.AZURE_DESKTOP_CLIENT_ID;
    
    res.json({
        enabled: true,
        tenantId: AZURE_TENANT_ID,
        clientId: desktopClientId, // Desktop client ID for MSAL
        apiClientId: AZURE_CLIENT_ID, // API client ID for token audience
    });
});

// Admin login endpoint - validates Azure AD token and returns session info
app.post('/api/admin/login', async (req, res) => {
    try {
        if (!AZURE_AUTH_ENABLED) {
            return res.status(400).json({ error: 'Azure AD authentication is not enabled' });
        }
        
        const authHeader = req.headers.authorization;
        if (!authHeader || !authHeader.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Missing or invalid Authorization header' });
        }
        
        const token = authHeader.substring(7);
        const expectedAudience = `api://${AZURE_CLIENT_ID}`;
        const options = {
            audience: expectedAudience,
            issuer: false,
            algorithms: ['RS256']
        };
        
        const decoded = await new Promise((resolve, reject) => {
            jwt.verify(token, getAzureSigningKey, options, (err, decoded) => {
                if (err) reject(err);
                else resolve(decoded);
            });
        });
        
        // Validate issuer
        const tokenIssuer = decoded.iss;
        const expectedIssuerV2 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`;
        const expectedIssuerV1 = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/`;
        const expectedIssuerV1Alt = `https://sts.windows.net/${AZURE_TENANT_ID}/`;
        
        const isValidIssuer = tokenIssuer === expectedIssuerV2 || 
                             tokenIssuer === expectedIssuerV1 ||
                             tokenIssuer === expectedIssuerV1Alt ||
                             (tokenIssuer && tokenIssuer.startsWith(`https://login.microsoftonline.com/${AZURE_TENANT_ID}/`)) ||
                             (tokenIssuer && tokenIssuer.startsWith(`https://sts.windows.net/${AZURE_TENANT_ID}/`));
        
        if (!isValidIssuer) {
            return res.status(401).json({ error: 'Invalid token issuer' });
        }
        
        // Check if user is Global Administrator
        const userOid = decoded.oid;
        const userName = decoded.upn || decoded.preferred_username || decoded.email || decoded.name || 'Unknown';
        const userDisplayName = decoded.name || userName;
        
        console.log(`[${new Date().toISOString()}] Admin login attempt for user: ${userDisplayName} (${userName}, OID: ${userOid})`);
        
        const isGlobalAdmin = await isGlobalAdministrator(userOid);
        
        if (!isGlobalAdmin) {
            console.log(`[${new Date().toISOString()}] Admin login denied for user: ${userDisplayName} (${userName}, OID: ${userOid}) - Not a Global Administrator`);
            return res.status(403).json({ error: 'Access denied. Global Administrator role required.' });
        }
        
        console.log(`[${new Date().toISOString()}] Admin login successful for Global Administrator: ${userDisplayName} (${userName}, OID: ${userOid})`);
        
        // Cache the session
        const userInfo = {
            upn: decoded.upn || decoded.preferred_username,
            email: decoded.email,
            oid: userOid,
            name: decoded.name
        };
        
        adminSessions.set(token, {
            user: userInfo,
            expiresAt: decoded.exp * 1000
        });
        
        res.json({
            success: true,
            user: userInfo,
            token: token, // Return token for client to use in subsequent requests
            expiresAt: decoded.exp * 1000
        });
        
    } catch (error) {
        console.error(`[${new Date().toISOString()}] Admin login failed:`, error.message);
        res.status(401).json({ error: 'Invalid or expired token' });
    }
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

// Get all systems with their Azure groups (admin only)
app.get('/api/systems', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const systems = data.systems || {};
        
        // Return list of systems with groups
        const systemList = await Promise.all(
            Object.entries(systems).map(async ([workstationName, systemData]) => {
                let groups = [];
                // Try to get groups from system's Azure object ID first
                if (systemData.azure_object_id) {
                    groups = await getSystemGroups(systemData.azure_object_id);
                }
                // If no groups from system object ID, try to get from user's email/UPN
                if (groups.length === 0 && systemData.username) {
                    // Try to find user's email/UPN - could be in username field or we need to look it up
                    // For now, assume username might be an email
                    groups = await getUserGroups(systemData.username);
                }
                return {
                    workstation_name: workstationName,
                    ...systemData,
                    groups: groups
                };
            })
        );
        
        res.json(systemList);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get system's Azure groups
app.get('/api/systems/:workstationName/groups', authenticate, async (req, res) => {
    try {
        const workstationName = decodeURIComponent(req.params.workstationName);
        const data = await loadData();
        const system = data.systems?.[workstationName];
        
        if (!system) {
            return res.status(404).json({ error: 'System not found' });
        }
        
        let groups = [];
        if (system.azure_object_id) {
            groups = await getSystemGroups(system.azure_object_id);
        }
        
        res.json({ groups });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get all Azure/Entra groups (for admin interface)
app.get('/api/groups', authenticate, async (req, res) => {
    try {
        const groups = await getAllGroups();
        res.json({ groups });
    } catch (error) {
        res.status(500).json({ error: error.message });
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
// Azure Graph API Helpers
// ============================================

/**
 * Get Azure/Entra groups for a user by their email/UPN
 */
async function getUserGroups(userEmail) {
    if (!graphClient || !userEmail) {
        console.log(`getUserGroups: graphClient=${!!graphClient}, userEmail=${userEmail}`);
        return [];
    }
    
    try {
        console.log(`Fetching groups for user: ${userEmail}`);
        
        // First, find the user by email/UPN
        const users = await graphClient.api('/users')
            .filter(`userPrincipalName eq '${userEmail}' or mail eq '${userEmail}'`)
            .select('id,userPrincipalName,mail')
            .get();
        
        if (!users.value || users.value.length === 0) {
            console.log(`User not found in Azure AD: ${userEmail}`);
            return [];
        }
        
        const userId = users.value[0].id;
        console.log(`Found user ${userEmail} with ID: ${userId}`);
        
        // Get user's group memberships (use transitive to get all groups including nested)
        try {
            const groups = await graphClient.api(`/users/${userId}/transitiveMemberOf`)
                .select('id,displayName,mailEnabled,securityEnabled')
                .get();
            
            const groupList = (groups.value || []).map(g => ({
                id: g.id,
                displayName: g.displayName,
                mailEnabled: g.mailEnabled || false,
                securityEnabled: g.securityEnabled !== false // Default to true if not specified
            }));
            
            console.log(`Found ${groupList.length} groups for user ${userEmail}:`, groupList.map(g => g.displayName).join(', '));
            if (groupList.length === 0) {
                console.log(`  NOTE: If you just added the user to a group, Azure AD changes can take 1-5 minutes to propagate.`);
            }
            return groupList;
        } catch (memberOfError) {
            // Fall back to direct memberOf if transitiveMemberOf fails
            console.log(`transitiveMemberOf failed, trying memberOf: ${memberOfError.message}`);
            const groups = await graphClient.api(`/users/${userId}/memberOf`)
                .select('id,displayName,mailEnabled,securityEnabled')
                .get();
            
            const groupList = (groups.value || []).map(g => ({
                id: g.id,
                displayName: g.displayName,
                mailEnabled: g.mailEnabled || false,
                securityEnabled: g.securityEnabled !== false
            }));
            
            console.log(`Found ${groupList.length} direct groups for user ${userEmail}:`, groupList.map(g => g.displayName).join(', '));
            return groupList;
        }
    } catch (error) {
        console.error(`Error fetching groups for user ${userEmail}:`, error.message);
        if (error.statusCode) {
            console.error(`  HTTP Status: ${error.statusCode}`);
        }
        if (error.body) {
            console.error(`  Error body:`, JSON.stringify(error.body, null, 2));
        }
        return [];
    }
}

/**
 * Get Azure/Entra groups for a system by its Azure object ID
 * Systems can be associated with Azure AD device objects
 */
async function getSystemGroups(azureObjectId) {
    if (!graphClient || !azureObjectId) {
        return [];
    }
    
    try {
        // Get device's group memberships
        const groups = await graphClient.api(`/devices/${azureObjectId}/memberOf`)
            .select('id,displayName,mailEnabled,securityEnabled')
            .get();
        
        return (groups.value || []).map(g => ({
            id: g.id,
            displayName: g.displayName,
            mailEnabled: g.mailEnabled || false,
            securityEnabled: g.securityEnabled !== false
        }));
    } catch (error) {
        console.error(`Error fetching groups for system ${azureObjectId}:`, error.message);
        return [];
    }
}

/**
 * Get all users in specified Azure/Entra groups
 */
async function getUsersInGroups(groupIds) {
    if (!graphClient || !groupIds || groupIds.length === 0) {
        return [];
    }
    
    try {
        const allUsers = new Set();
        
        // Get users from each group (use transitiveMembers to get nested groups)
        for (const groupId of groupIds) {
            try {
                // Try transitiveMembers first to get all users including nested groups
                let members;
                try {
                    members = await graphClient.api(`/groups/${groupId}/transitiveMembers`)
                        .select('id,userPrincipalName,mail')
                        .get();
                } catch (transitiveError) {
                    // Fall back to direct members if transitiveMembers fails
                    console.log(`transitiveMembers failed for group ${groupId}, using members: ${transitiveError.message}`);
                    members = await graphClient.api(`/groups/${groupId}/members`)
                        .select('id,userPrincipalName,mail')
                        .get();
                }
                
                (members.value || []).forEach(user => {
                    // Only include actual users (not groups or other objects)
                    if (user.userPrincipalName || user.mail) {
                        allUsers.add(user.userPrincipalName || user.mail);
                    }
                });
            } catch (error) {
                console.error(`Error fetching members of group ${groupId}:`, error.message);
                if (error.statusCode) {
                    console.error(`  HTTP Status: ${error.statusCode}`);
                }
            }
        }
        
        const userList = Array.from(allUsers);
        console.log(`Found ${userList.length} users in selected groups:`, userList.join(', '));
        if (userList.length === 0) {
            console.log(`  NOTE: If you just added users to groups, Azure AD changes can take 1-5 minutes to propagate.`);
        }
        return userList;
    } catch (error) {
        console.error('Error fetching users in groups:', error.message);
        return [];
    }
}

/**
 * Find a group by its display name
 */
async function findGroupByName(groupName) {
    if (!graphClient || !groupName) {
        return null;
    }
    
    try {
        // Search for group by display name
        const groups = await graphClient.api('/groups')
            .filter(`displayName eq '${groupName}'`)
            .select('id,displayName')
            .get();
        
        if (groups.value && groups.value.length > 0) {
            return groups.value[0];
        }
        
        return null;
    } catch (error) {
        console.error(`Error finding group by name "${groupName}":`, error.message);
        return null;
    }
}

/**
 * Check if a user is a Global Administrator
 */
async function isGlobalAdministrator(userOid) {
    if (!graphClient || !userOid) {
        return false;
    }
    
    try {
        // Method 1: Check via directoryRoles endpoint (most reliable)
        try {
            // Get the Global Administrator directory role
            // Note: directoryRoles only returns activated roles
            const directoryRoles = await graphClient.api('/directoryRoles')
                .filter("displayName eq 'Global Administrator'")
                .select('id,displayName')
                .get();
            
            if (directoryRoles.value && directoryRoles.value.length > 0) {
                const globalAdminRoleId = directoryRoles.value[0].id;
                console.log(`[${new Date().toISOString()}] Found Global Administrator role ID: ${globalAdminRoleId}`);
                
                // Check if user is a member of this role
                // Use $count=false to avoid issues with empty results
                try {
                    const members = await graphClient.api(`/directoryRoles/${globalAdminRoleId}/members`)
                        .filter(`id eq '${userOid}'`)
                        .select('id')
                        .get();
                    
                    if (members.value && members.value.length > 0) {
            console.log(`[${new Date().toISOString()}] User ${userOid} is a Global Administrator (via directory role ${globalAdminRoleId})`);
                    return true;
                    }
                } catch (memberError) {
                    // If the role exists but querying members fails, try alternative method
                    console.log(`Could not query role members directly: ${memberError.message}`);
                    // Fall through to next method
                }
            } else {
                console.log(`[${new Date().toISOString()}] Global Administrator directory role not found or not activated`);
            }
        } catch (roleError) {
            console.log(`Directory role check failed: ${roleError.message}`);
            if (roleError.statusCode) {
                console.log(`  HTTP Status: ${roleError.statusCode}`);
            }
            // Fall through to next method
        }
        
        // Method 2: Check via user's memberOf (includes directory roles)
        try {
            const roles = await graphClient.api(`/users/${userOid}/memberOf`)
                .select('id,displayName,@odata.type')
                .get();
            
            // Check if user has Global Administrator role
            const globalAdminRole = (roles.value || []).find(role => 
                role.displayName === 'Global Administrator' ||
                role.id === '62e90394-69f5-4237-9190-012177145e10' // Role template ID
            );
            
            if (globalAdminRole) {
                console.log(`[${new Date().toISOString()}] User ${userOid} is a Global Administrator (via memberOf)`);
                return true;
            }
        } catch (memberOfError) {
            console.log(`MemberOf check failed: ${memberOfError.message}`);
        }
        
        // Method 3: Check via roleManagement API (most comprehensive)
        try {
            // Get user's directory role assignments directly
            const roleAssignments = await graphClient.api(`/roleManagement/directory/roleAssignments`)
                .filter(`principalId eq '${userOid}'`)
                .expand('roleDefinition')
                .get();
            
            if (roleAssignments.value && roleAssignments.value.length > 0) {
                const globalAdminAssignment = roleAssignments.value.find(assignment => 
                    assignment.roleDefinition?.displayName === 'Global Administrator' ||
                    assignment.roleDefinition?.roleTemplateId === '62e90394-69f5-4237-9190-012177145e10'
                );
                
                if (globalAdminAssignment) {
                    console.log(`[${new Date().toISOString()}] User ${userOid} is a Global Administrator (via role assignments)`);
                    return true;
                }
            }
        } catch (roleAssignmentError) {
            console.log(`Role assignment check failed: ${roleAssignmentError.message}`);
            if (roleAssignmentError.statusCode) {
                console.log(`  HTTP Status: ${roleAssignmentError.statusCode}`);
            }
        }
        
        console.log(`[${new Date().toISOString()}] User ${userOid} is NOT a Global Administrator`);
        return false;
    } catch (error) {
        console.error(`Error checking Global Administrator status for user ${userOid}:`, error.message);
        if (error.statusCode) {
            console.error(`  HTTP Status: ${error.statusCode}`);
        }
        if (error.body) {
            console.error(`  Error body:`, JSON.stringify(error.body, null, 2));
        }
        return false;
    }
}

/**
 * Get all Azure/Entra groups (for admin interface)
 */
async function getAllGroups() {
    if (!graphClient) {
        return [];
    }
    
    try {
        const groups = await graphClient.api('/groups')
            .select('id,displayName,mailEnabled,securityEnabled')
            .orderby('displayName')
            .get();
        
        return (groups.value || []).map(g => ({
            id: g.id,
            displayName: g.displayName,
            mailEnabled: g.mailEnabled || false,
            securityEnabled: g.securityEnabled !== false
        }));
    } catch (error) {
        console.error('Error fetching all groups:', error.message);
        return [];
    }
}

// ============================================
// User Keyring Management Endpoints
// ============================================

// Get user's Azure/Entra groups
app.get('/api/users/:username/groups', authenticate, async (req, res) => {
    try {
        const username = decodeURIComponent(req.params.username);
        const groups = await getUserGroups(username);
        res.json({ groups });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get all users with their key counts and Azure groups (admin only)
app.get('/api/users', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const users = data.users || {};
        
        // Return list of users with key counts and groups
        const userList = await Promise.all(
            Object.entries(users).map(async ([username, userData]) => {
                const groups = await getUserGroups(username);
                return {
                    username,
                    display_name: userData.display_name || username,
                    key_count: (userData.public_keys || []).length,
                    last_seen: userData.last_seen,
                    groups: groups
                };
            })
        );
        
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

// ============================================
// Authorized Keys Endpoint
// ============================================

// Generate authorized_keys file
// Query params: ?groups=groupId1,groupId2 (optional - filter by Azure groups)
// If no groups specified, returns all users' keys
app.get('/api/authorized-keys', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const users = data.users || {};
        
        let targetUsers = Object.keys(users);
        
        // If groups specified, filter users by group membership
        if (req.query.groups) {
            const groupIds = req.query.groups.split(',').map(g => g.trim()).filter(g => g);
            if (groupIds.length > 0) {
                const usersInGroups = await getUsersInGroups(groupIds);
                targetUsers = targetUsers.filter(username => usersInGroups.includes(username));
            }
        }
        
        // Collect all public keys from target users
        const authorizedKeys = [];
        for (const username of targetUsers) {
            const user = users[username];
            if (user && user.public_keys) {
                for (const key of user.public_keys) {
                    if (key.key_data && key.verified) {
                        // Format: key_data comment (username)
                        authorizedKeys.push(`${key.key_data} ${username}`);
                    }
                }
            }
        }
        
        res.setHeader('Content-Type', 'text/plain');
        res.send(authorizedKeys.join('\n') + '\n');
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Generate authorized_keys file by group name (for automation/wget)
// Endpoint: /api/authorized-keys/group/:groupName
// Optional query param: ?key=adminKey (or use X-API-Key header)
// Example: wget http://server:3000/api/authorized-keys/group/Developers?key=adminkey
app.get('/api/authorized-keys/group/:groupName', async (req, res) => {
    try {
        // Check authentication (admin key in query param or header)
        const providedKey = req.query.key || req.headers['x-api-key'];
        if (!providedKey || providedKey !== config?.admin_key) {
            return res.status(401).json({ error: 'Unauthorized. Provide admin key via ?key= parameter or X-API-Key header.' });
        }
        
        const groupName = decodeURIComponent(req.params.groupName);
        console.log(`Generating authorized_keys for group: ${groupName}`);
        
        // Find the group by name
        const group = await findGroupByName(groupName);
        if (!group) {
            return res.status(404).json({ error: `Group "${groupName}" not found` });
        }
        
        console.log(`Found group "${groupName}" with ID: ${group.id}`);
        
        // Get users in the group
        const usersInGroup = await getUsersInGroups([group.id]);
        if (usersInGroup.length === 0) {
            console.log(`No users found in group "${groupName}"`);
            res.setHeader('Content-Type', 'text/plain');
            return res.send('# No users found in group\n');
        }
        
        // Get keys for users in the group
        const data = await loadData();
        const users = data.users || {};
        
        const authorizedKeys = [];
        for (const username of usersInGroup) {
            const user = users[username];
            if (user && user.public_keys) {
                for (const key of user.public_keys) {
                    if (key.key_data && key.verified) {
                        // Format: key_data comment (username)
                        authorizedKeys.push(`${key.key_data} ${username}`);
                    }
                }
            }
        }
        
        console.log(`Generated authorized_keys with ${authorizedKeys.length} keys for ${usersInGroup.length} users in group "${groupName}"`);
        
        res.setHeader('Content-Type', 'text/plain');
        res.setHeader('Content-Disposition', `inline; filename="authorized_keys_${groupName.replace(/[^a-zA-Z0-9]/g, '_')}.txt"`);
        res.send(authorizedKeys.join('\n') + '\n');
    } catch (error) {
        console.error(`Error generating authorized_keys for group:`, error);
        res.status(500).json({ error: error.message });
    }
});

// ============================================
// Azure Settings Management Endpoints
// ============================================

// Get GridBanner URL from Azure settings
app.get('/api/admin/gridbanner-url', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const gridbannerUrl = data.settings?.gridbanner_url || '';
        res.json({ gridbanner_url: gridbannerUrl });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Set GridBanner URL in Azure settings
app.post('/api/admin/gridbanner-url', authenticate, async (req, res) => {
    try {
        const { gridbanner_url } = req.body;
        
        if (!gridbanner_url || typeof gridbanner_url !== 'string') {
            return res.status(400).json({ error: 'gridbanner_url is required and must be a string' });
        }
        
        // Validate URL format
        try {
            new URL(gridbanner_url);
        } catch {
            return res.status(400).json({ error: 'Invalid URL format' });
        }
        
        const data = await loadData();
        if (!data.settings) {
            data.settings = {};
        }
        data.settings.gridbanner_url = gridbanner_url;
        await saveData(data);
        
        console.log(`GridBanner URL updated to: ${gridbanner_url}`);
        res.json({ 
            success: true, 
            gridbanner_url: gridbanner_url,
            message: 'GridBanner URL updated successfully'
        });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Get global settings from Azure
app.get('/api/admin/global-settings', authenticate, async (req, res) => {
    try {
        const data = await loadData();
        const globalSettings = data.settings?.global_settings || {
            triple_click_enabled: null,
            terminate_enabled: null,
            keyring_enabled: null,
            tray_only_mode: null
        };
        res.json(globalSettings);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// Set global settings in Azure
app.post('/api/admin/global-settings', authenticate, async (req, res) => {
    try {
        const globalSettings = req.body;
        
        // Validate settings structure
        const validSettings = {
            triple_click_enabled: globalSettings.triple_click_enabled,
            terminate_enabled: globalSettings.terminate_enabled,
            keyring_enabled: globalSettings.keyring_enabled,
            tray_only_mode: globalSettings.tray_only_mode
        };
        
        // Remove null/undefined values
        Object.keys(validSettings).forEach(key => {
            if (validSettings[key] === null || validSettings[key] === undefined) {
                delete validSettings[key];
            }
        });
        
        const data = await loadData();
        if (!data.settings) {
            data.settings = {};
        }
        data.settings.global_settings = validSettings;
        await saveData(data);
        
        console.log('Global settings updated:', validSettings);
        res.json({ 
            success: true, 
            global_settings: validSettings,
            message: 'Global settings updated successfully'
        });
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
    initializeAzureAD(); // Initialize Azure AD after config is loaded
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

