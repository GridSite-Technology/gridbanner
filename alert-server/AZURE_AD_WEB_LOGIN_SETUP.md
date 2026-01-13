# Azure AD Web Login Setup

This guide explains how to configure Azure AD authentication for the admin web interface.

## Overview

The admin interface supports two authentication methods:
1. **Admin Key** - Traditional password-based authentication
2. **Azure AD** - SSO for Global Administrators

## Prerequisites

- Azure AD tenant with Global Administrators
- Desktop Client app already registered (for MSAL.js)
- API app already registered and configured

## Step 1: Add Single-Page Application (SPA) Redirect URI

**CRITICAL**: The admin interface uses MSAL.js which requires a **Single-Page Application (SPA)** platform type, NOT "Web". This is required for cross-origin token redemption.

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Find your **Desktop Client App** (the one with client ID used in `azure_desktop_client_id`)
4. Click on it, then go to **Authentication** in the left sidebar
5. Under **Platform configurations**, check if you have a **Single-page application** entry
   - If you have a "Web" entry, you may need to remove it first (or keep both)
6. Click **+ Add a platform** if you don't have SPA configured
7. Select **Single-page application**
8. Add your redirect URI:
   - For local development: `http://localhost:3000`
   - For production: `https://your-domain.com` (or your ngrok URL)
   - **Important**: The redirect URI must match exactly, including the path (e.g., `https://umbonic-roseanna-cuppy.ngrok-free.dev`)
9. Click **Configure**
10. **Save** the changes

**Note**: If you see error "AADSTS9002326: Cross-origin token redemption is permitted only for the 'Single-Page Application' client-type", you need to use SPA platform, not Web platform.

## Step 2: Configure Server

Add the desktop client ID to your `alert-server/config.json`:

```json
{
  "admin_key": "your-admin-key",
  "azure_auth_enabled": true,
  "azure_tenant_id": "your-tenant-id",
  "azure_client_id": "your-api-client-id",
  "azure_client_secret": "your-api-client-secret",
  "azure_desktop_client_id": "your-desktop-client-id"
}
```

## Step 3: Test Login

1. Restart the alert-server
2. Open the admin interface
3. Click **Login with Azure AD**
4. Authenticate with a Global Administrator account
5. You should be logged in successfully

## Troubleshooting

### Error: "AADSTS500113: No reply address is registered"

This means the redirect URI is not registered in Azure AD.

**Solution:**
1. Check the error message - it will show the exact redirect URI needed
2. Go to Azure Portal → App registrations → Your Desktop Client App
3. Go to **Authentication**
4. Under **Web** platform, add the redirect URI shown in the error
5. Save and try again

### Error: "Access denied. Global Administrator role required"

This means the user is not a Global Administrator.

**Solution:**
- Only users with the Global Administrator role can use Azure AD login
- Use the admin key login for other users, or grant Global Administrator role

### Redirect URI Mismatch

The redirect URI must match **exactly**:
- Protocol (http vs https)
- Domain
- Port (if specified)
- Path (if specified)

For example:
- ✅ `https://umbonic-roseanna-cuppy.ngrok-free.dev` (matches)
- ❌ `https://umbonic-roseanna-cuppy.ngrok-free.dev/` (trailing slash mismatch)
- ❌ `http://umbonic-roseanna-cuppy.ngrok-free.dev` (protocol mismatch)

### Multiple Redirect URIs

You can register multiple redirect URIs for different environments:
- `http://localhost:3000` (local development)
- `https://your-domain.com` (production)
- `https://your-ngrok-url.ngrok-free.dev` (temporary/testing)

## Security Notes

- Only Global Administrators can use Azure AD login
- Admin key login still works for backward compatibility
- Azure AD tokens are cached in sessionStorage (cleared on browser close)
- Tokens expire based on Azure AD token lifetime (typically 1 hour)

## See Also

- [Azure AD Setup Guide](../AZURE_AD_SETUP_GUIDE.md) - General Azure AD setup
- [Azure AD Troubleshooting](../AZURE_AD_TROUBLESHOOTING.md) - Common issues
