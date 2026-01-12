# Azure AD Authentication Proposal for Keyring API

## Current Security Gap

The current implementation has a critical security flaw:
- Client sends username in URL: `/api/keyring/:username/keys`
- Server trusts the username without verification
- Only proof-of-possession (SSH key signature) is verified
- **Anyone could claim to be any user**

## Proposed Solution: Azure AD OAuth 2.0

### Architecture Overview

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│   Client    │─────────│  Azure AD   │─────────│   Server    │
│  (WPF App)  │         │  (OAuth 2.0) │         │  (Node.js)  │
└─────────────┘         └──────────────┘         └─────────────┘
      │                        │                        │
      │ 1. Request Device Code │                        │
      │───────────────────────>│                        │
      │                        │                        │
      │ 2. Device Code + URL   │                        │
      │<───────────────────────│                        │
      │                        │                        │
      │ 3. User authenticates  │                        │
      │    in browser          │                        │
      │                        │                        │
      │ 4. Poll for token      │                        │
      │───────────────────────>│                        │
      │                        │                        │
      │ 5. Access Token        │                        │
      │<───────────────────────│                        │
      │                        │                        │
      │ 6. API Call + Token     │                        │
      │───────────────────────────────────────────────>│
      │                        │                        │
      │                        │ 7. Validate Token      │
      │                        │    Extract User ID     │
      │                        │                        │
      │                        │ 8. Verify User Match   │
      │                        │                        │
      │ 9. Response             │                        │
      │<───────────────────────────────────────────────│
```

## Implementation Details

### 1. Client-Side (WPF/.NET)

#### Required NuGet Packages
```xml
<PackageReference Include="Microsoft.Identity.Client" Version="4.60.0" />
```

#### Authentication Flow: Device Code Flow
- **Why Device Code Flow?** 
  - Best for desktop apps without embedded browser
  - User authenticates in their default browser
  - No need to handle browser redirects in WPF
  - Works with MFA, conditional access, etc.

#### Implementation Steps

1. **Azure AD App Registration**
   - Register a "Public client/native" application
   - Note: Client ID, Tenant ID
   - Add redirect URI: `http://localhost` (not used but required)
   - API permissions: Request access to your API

2. **Client Code Structure**
```csharp
public class AzureAuthManager
{
    private readonly IPublicClientApplication _app;
    private AuthenticationResult? _authResult;
    
    public AzureAuthManager(string clientId, string tenantId)
    {
        _app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithDefaultRedirectUri()
            .Build();
    }
    
    public async Task<bool> AuthenticateAsync()
    {
        var scopes = new[] { "api://your-api-id/.default" };
        
        try
        {
            // Try silent authentication first (if cached)
            var accounts = await _app.GetAccountsAsync();
            if (accounts.Any())
            {
                _authResult = await _app.AcquireTokenSilent(scopes, accounts.First())
                    .ExecuteAsync();
                return true;
            }
        }
        catch { /* No cached token */ }
        
        // Device code flow
        _authResult = await _app.AcquireTokenWithDeviceCode(
            scopes,
            deviceCodeResult =>
            {
                // Show device code to user
                var dialog = new DeviceCodeWindow(deviceCodeResult);
                dialog.ShowDialog();
                return Task.CompletedTask;
            })
            .ExecuteAsync();
        
        return _authResult != null;
    }
    
    public string? GetAccessToken()
    {
        return _authResult?.AccessToken;
    }
    
    public string? GetUserPrincipalName()
    {
        return _authResult?.Account?.Username;
    }
}
```

3. **Update KeyringManager**
```csharp
public class KeyringManager
{
    private readonly AzureAuthManager? _authManager;
    private readonly HttpClient _httpClient;
    
    public async Task<KeyUploadResult> UploadKeyAsync(...)
    {
        // Ensure authenticated
        if (_authManager != null && !await _authManager.EnsureAuthenticatedAsync())
        {
            return new KeyUploadResult 
            { 
                Success = false, 
                Error = "Authentication required" 
            };
        }
        
        var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(_username)}/keys";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        
        // Add Azure AD token to Authorization header
        if (_authManager != null)
        {
            var token = _authManager.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }
        
        // ... rest of upload logic
    }
}
```

### 2. Server-Side (Node.js)

#### Required NPM Packages
```json
{
  "dependencies": {
    "passport": "^0.6.0",
    "passport-azure-ad": "^4.3.1",
    "jsonwebtoken": "^9.0.2",
    "jwks-rsa": "^3.0.1"
  }
}
```

#### Implementation

1. **Token Validation Middleware**
```javascript
const jwt = require('jsonwebtoken');
const jwksClient = require('jwks-rsa');

// Azure AD configuration
const AZURE_TENANT_ID = process.env.AZURE_TENANT_ID;
const AZURE_CLIENT_ID = process.env.AZURE_CLIENT_ID; // Your API's client ID
const JWKS_URI = `https://login.microsoftonline.com/${AZURE_TENANT_ID}/discovery/v2.0/keys`;

const client = jwksClient({
  jwksUri: JWKS_URI,
  requestHeaders: {},
  timeout: 30000
});

function getKey(header, callback) {
  client.getSigningKey(header.kid, (err, key) => {
    const signingKey = key.publicKey || key.rsaPublicKey;
    callback(null, signingKey);
  });
}

// Middleware to validate Azure AD tokens
function validateAzureToken(req, res, next) {
  const authHeader = req.headers.authorization;
  
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Missing or invalid Authorization header' });
  }
  
  const token = authHeader.substring(7);
  
  const options = {
    audience: `api://${AZURE_CLIENT_ID}`, // Your API's identifier
    issuer: `https://login.microsoftonline.com/${AZURE_TENANT_ID}/v2.0`,
    algorithms: ['RS256']
  };
  
  jwt.verify(token, getKey, options, (err, decoded) => {
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
    
    next();
  });
}

// Middleware to verify user matches request
function verifyUserMatch(req, res, next) {
  const requestedUsername = decodeURIComponent(req.params.username);
  const authenticatedUser = req.azureUser.upn || req.azureUser.email;
  
  // Normalize usernames (handle email vs UPN)
  const normalizedRequested = requestedUsername.toLowerCase().trim();
  const normalizedAuthenticated = authenticatedUser.toLowerCase().trim();
  
  if (normalizedRequested !== normalizedAuthenticated) {
    return res.status(403).json({ 
      error: 'Forbidden: You can only manage your own keys',
      requested: requestedUsername,
      authenticated: authenticatedUser
    });
  }
  
  next();
}
```

2. **Update API Routes**
```javascript
// Upload key - now with authentication
app.post('/api/keyring/:username/keys', 
  validateAzureToken,      // Validate Azure AD token
  verifyUserMatch,         // Ensure user matches username in URL
  async (req, res) => {
    try {
      const username = req.azureUser.upn; // Use authenticated user, not URL param
      const { key_type, key_data, key_name, fingerprint, challenge, signature } = req.body;
      
      // ... rest of existing logic
      
      // Log authenticated user
      console.log(`Key upload by authenticated user: ${req.azureUser.upn} (OID: ${req.azureUser.oid})`);
      
      // ... save key logic
    } catch (error) {
      res.status(500).json({ error: error.message });
    }
  }
);

// Get user's keys - also needs authentication
app.get('/api/keyring/:username',
  validateAzureToken,
  verifyUserMatch,
  async (req, res) => {
    // ... existing logic
  }
);

// Delete key - also needs authentication
app.delete('/api/keyring/:username/keys/:keyId',
  validateAzureToken,
  verifyUserMatch,
  async (req, res) => {
    // ... delete logic
  }
);
```

### 3. Configuration

#### Client Configuration (app.config or config.json)
```json
{
  "azure_auth": {
    "enabled": true,
    "client_id": "your-client-id",
    "tenant_id": "your-tenant-id",
    "api_scope": "api://your-api-id/.default"
  }
}
```

#### Server Environment Variables
```bash
AZURE_TENANT_ID=your-tenant-id
AZURE_CLIENT_ID=your-api-client-id
```

## Security Benefits

1. **Identity Verification**: Server verifies user identity via Azure AD
2. **Token Validation**: JWT signature verified against Azure AD's public keys
3. **User Matching**: Server ensures token's user matches requested username
4. **Audit Trail**: Can log authenticated user's OID for audit purposes
5. **MFA Support**: Works with Azure AD MFA, conditional access, etc.
6. **Token Expiry**: Tokens expire automatically (typically 1 hour)

## User Experience

1. **First Time**: User sees device code dialog, authenticates in browser
2. **Subsequent Calls**: MSAL caches tokens, silent authentication
3. **Token Refresh**: MSAL handles token refresh automatically
4. **Offline**: Cached tokens work offline (until expiry)

## Alternative: Authorization Code Flow with PKCE

If you want a more seamless UX (embedded browser), consider:
- **Authorization Code Flow with PKCE**
- Requires `Microsoft.Identity.Client` with browser support
- More complex but better UX

## Migration Path

1. **Phase 1**: Add Azure AD auth alongside existing (make it optional)
2. **Phase 2**: Require Azure AD auth for new key uploads
3. **Phase 3**: Migrate existing keys to authenticated users
4. **Phase 4**: Remove username-based trust entirely

## Questions to Consider

1. **User Mapping**: How do you map Azure AD users to your username system?
   - Use UPN (user@domain.com) as username?
   - Use email as username?
   - Maintain a mapping table?

2. **Existing Keys**: What happens to keys uploaded before auth?
   - Grandfather them in?
   - Require re-upload with authentication?

3. **Admin Operations**: Do you need admin endpoints?
   - Use Azure AD roles/groups?
   - Separate admin API key?

4. **Token Storage**: Where to store tokens securely?
   - Windows Credential Manager (recommended)
   - Encrypted file
   - MSAL handles this automatically

## Next Steps

1. Register applications in Azure AD
2. Implement client-side authentication
3. Implement server-side token validation
4. Test with a single user
5. Roll out gradually

