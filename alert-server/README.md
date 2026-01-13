# GridBanner Alert Server

Node.js/Express server for managing GridBanner alerts, user keyrings, and system information.

## Features

- **Alert Management**: Create and manage alerts that display on GridBanner clients
- **User Keyring**: Centralized SSH public key management with Azure AD integration
- **System Registration**: Track GridBanner client systems and their status
- **Azure AD Integration**: Group-based targeting and user authentication
- **Authorized Keys Generation**: Generate `authorized_keys` files for groups

## Installation

1. Install Node.js (v16 or higher)
2. Install dependencies:
```bash
npm install
```

3. Configure the server by creating `config.json`:
```json
{
  "admin_key": "your-admin-key-here",
  "azure_auth_enabled": true,
  "azure_tenant_id": "your-tenant-id",
  "azure_client_id": "your-api-client-id",
  "azure_client_secret": "your-client-secret"
}
```

4. Start the server:
```bash
npm start
```

The server will run on port 3000 by default.

## Azure AD Configuration

### Required Microsoft Graph API Permissions

The alert-server requires **Application permissions** (not Delegated) to read user and group information from Azure AD. These permissions must be granted with **admin consent**.

#### Step-by-Step Permission Setup

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Find your **GridBanner API** app registration (the one with the client secret)
3. Click **API permissions** in the left sidebar
4. Click **+ Add a permission**
5. Select **Microsoft Graph**
6. Select **Application permissions** (NOT Delegated permissions - this is critical!)
7. Add the following permissions:
   - `User.Read.All` - Read all users' full profiles
   - `GroupMember.Read.All` - Read all group memberships
   - `Group.Read.All` - Read all groups
   - `Directory.Read.All` - Read directory data (recommended for full functionality)
8. Click **Add permissions**
9. **CRITICAL**: Click **Grant admin consent for [Your Organization]**
   - This button is at the top of the API permissions page
   - You must be a Global Administrator or have permission to grant consent
   - Confirm the consent
10. Verify all permissions show "Granted for [Your Organization]" with a green checkmark

#### Why Application Permissions?

The alert-server uses **service principal authentication** (client secret) to access Microsoft Graph API. This requires Application permissions, not Delegated permissions. Application permissions allow the server to read directory information without a signed-in user.

#### Troubleshooting Permission Issues

If you see errors like "Insufficient privileges to complete the operation":
- Verify you added **Application permissions** (not Delegated)
- Verify you clicked **Grant admin consent**
- Check that all permissions show green checkmarks
- Wait 1-2 minutes after granting consent for changes to propagate
- Restart the alert-server after granting permissions

## API Endpoints

### Authentication

Most endpoints require authentication via:
- **Admin Key**: Pass as `X-API-Key` header or `?key=` query parameter
- **Azure AD Token**: For user-specific endpoints, pass as `Authorization: Bearer <token>` header

### Authorized Keys Endpoints

#### Generate Authorized Keys by Group Name

**Endpoint**: `GET /api/authorized-keys/group/:groupName`

Generate an `authorized_keys` file containing all verified SSH public keys from users in a specific Azure AD group.

**Authentication**: Admin key required (via query param or header)

**Parameters**:
- `groupName` (URL parameter): The display name of the Azure AD group
- `key` (query parameter, optional): Admin key (alternative to header)

**Headers**:
- `X-API-Key`: Admin key (alternative to query parameter)

**Example with wget**:
```bash
wget "http://your-server:3000/api/authorized-keys/group/Developers?key=adminkey" -O authorized_keys
```

**Example with curl**:
```bash
curl -H "X-API-Key: adminkey" "http://your-server:3000/api/authorized-keys/group/Developers" > authorized_keys
```

**Example with query parameter**:
```bash
curl "http://your-server:3000/api/authorized-keys/group/Developers?key=adminkey" > authorized_keys
```

**Response**:
- **Success (200)**: Plain text file in `authorized_keys` format
- **Not Found (404)**: JSON error if group name not found
- **Unauthorized (401)**: JSON error if admin key is missing or invalid

**Response Format**:
```
ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAA... user1@example.com
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI... user2@example.com
```

**Features**:
- Includes only verified keys (keys that have been validated via challenge/response)
- Uses transitive group membership (includes users from nested groups)
- Returns empty file with comment if group has no users or no keys
- Suitable for automation scripts and cron jobs

**Use Cases**:
- Automated `authorized_keys` file updates on servers
- Group-based SSH access control
- CI/CD pipeline key management
- Server provisioning scripts

#### Generate Authorized Keys by Group IDs

**Endpoint**: `GET /api/authorized-keys`

Generate an `authorized_keys` file with optional group filtering.

**Authentication**: Admin key required

**Query Parameters**:
- `groups` (optional): Comma-separated list of group IDs to filter by
  - Example: `?groups=groupId1,groupId2`
  - If omitted, returns keys for all users

**Example**:
```bash
curl -H "X-API-Key: adminkey" "http://your-server:3000/api/authorized-keys?groups=abc-123,def-456" > authorized_keys
```

### User Keyring Endpoints

#### Get User's Keys
**Endpoint**: `GET /api/keyring/:username`

Get all public keys for a specific user. Requires Azure AD authentication matching the username.

#### Upload Key
**Endpoint**: `POST /api/keyring/:username/keys`

Upload a new public key. Requires proof of possession via challenge/response.

#### Delete Key
**Endpoint**: `DELETE /api/keyring/:username/keys/:keyId`

Delete a specific key. Requires Azure AD authentication.

### Admin Endpoints

#### Get All Users
**Endpoint**: `GET /api/users`

Get list of all users with key counts and Azure AD group memberships. Requires admin key.

#### Get User Groups
**Endpoint**: `GET /api/users/:username/groups`

Get Azure AD groups for a specific user. Requires admin key.

#### Get All Groups
**Endpoint**: `GET /api/groups`

Get all Azure AD groups. Requires admin key.

### Alert Endpoints

#### Create Alert
**Endpoint**: `POST /api/alert`

Create a new alert. Supports group-based targeting via `target_groups` array.

#### Get Current Alert
**Endpoint**: `GET /api/alert/current`

Get the currently active alert.

## Configuration

### config.json

The server configuration file (`config.json`) should contain:

```json
{
  "admin_key": "your-secure-admin-key",
  "azure_auth_enabled": true,
  "azure_tenant_id": "your-tenant-id",
  "azure_client_id": "your-api-app-client-id",
  "azure_client_secret": "your-api-app-client-secret"
}
```

**Important**: 
- Never commit `config.json` to version control (it's in `.gitignore`)
- Keep the `admin_key` and `azure_client_secret` secure
- The `azure_client_id` should be the **API app's Application ID**, not the desktop client's ID

### Environment Variables

You can also configure via environment variables:
- `AZURE_TENANT_ID`
- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`
- `AZURE_AUTH_ENABLED` (set to `"true"` to enable)

## Security Notes

- The admin key should be strong and kept secret
- Azure AD client secrets should be rotated regularly
- Use HTTPS in production (configure reverse proxy with SSL)
- The `authorized_keys` endpoints require admin key authentication
- User keyring endpoints require Azure AD token authentication

## Troubleshooting

### "Insufficient privileges to complete the operation"

This error means the API app doesn't have the required Microsoft Graph permissions:
1. Go to Azure Portal → App registrations → Your API app
2. Click **API permissions**
3. Verify you added **Application permissions** (not Delegated)
4. Verify you clicked **Grant admin consent**
5. Restart the alert-server

### "Group not found" when using authorized-keys endpoint

- Verify the group name matches exactly (case-sensitive)
- Check that the group exists in Azure AD
- Verify the API app has `Group.Read.All` permission

### Groups showing as empty

- Verify `GroupMember.Read.All` permission is granted
- Check that admin consent was granted
- Wait 1-2 minutes after granting permissions
- Restart the alert-server

### User not appearing in group after adding them

**Important**: The server does NOT cache group memberships - it queries Azure AD directly every time. However, Azure AD has propagation delays:

- **Direct group membership**: Usually appears within 1-3 minutes
- **Nested group membership**: Can take 3-5 minutes
- **Complex scenarios**: May take up to 15 minutes

If you just added a user to a group:
1. Wait 2-3 minutes for Azure AD to propagate the change
2. Refresh the admin interface (it queries fresh each time)
3. Check server logs to see what groups are actually being returned
4. Verify in Azure Portal that the user is actually in the group

See [AZURE_GROUPS_PROPAGATION.md](AZURE_GROUPS_PROPAGATION.md) for detailed information.

## Development

### Running in Development

```bash
npm start
```

The server will reload automatically on file changes if using `nodemon`.

### Testing Endpoints

You can test endpoints using curl:

```bash
# Health check
curl http://localhost:3000/api/health

# Get authorized keys for a group
curl -H "X-API-Key: adminkey" "http://localhost:3000/api/authorized-keys/group/Developers"
```

## License

See main project LICENSE file.
