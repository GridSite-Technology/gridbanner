# Azure AD Setup Guide

This guide walks you through registering your applications in Azure AD and getting the configuration values needed for GridBanner.

## Overview

You need to register **two applications** in Azure AD:
1. **Desktop Client App** (GridBanner WPF application) - Public client/native app
2. **Web API** (Your alert server) - Web API app

## Step 1: Register the Desktop Client Application

### 1.1 Navigate to Azure Portal

1. Go to [https://portal.azure.com](https://portal.azure.com)
2. Sign in with your Azure AD account
3. Search for "Azure Active Directory" or "Microsoft Entra ID" in the search bar
4. Click on **Azure Active Directory** (or **Microsoft Entra ID**)

### 1.2 Register the Client App

1. In the left sidebar, click on **App registrations**
2. Click **+ New registration** at the top
3. Fill in the details:
   - **Name**: `GridBanner Desktop Client` (or any name you prefer)
   - **Supported account types**: 
     - Choose **"Accounts in this organizational directory only"** for single-tenant
     - Or **"Accounts in any organizational directory"** for multi-tenant
   - **Redirect URI**: 
     - Platform: **Public client/native (mobile & desktop)**
     - URI: `http://localhost` (required but not used for device code flow)
4. Click **Register**

### 1.3 Configure as Public Client

**CRITICAL**: After registering, you MUST configure it as a public client:

1. In the left sidebar, click **Authentication**
2. Scroll down to **Advanced settings**
3. Under **Allow public client flows**, click **Yes**
4. Click **Save** at the top

**This is required** - without this, you'll get `AADSTS7000218` errors!

### 1.4 Get Client Configuration Values

After registration, you'll see the **Overview** page. Here's what you need:

1. **Application (client) ID** - Copy this value
   - This is your `azure_client_id` for the GridBanner config
   - Example: `12345678-1234-1234-1234-123456789abc`

2. **Directory (tenant) ID** - Copy this value
   - This is your `azure_tenant_id` for both client and server configs
   - Example: `87654321-4321-4321-4321-cba987654321`

### 1.4 Configure API Permissions

1. In the left sidebar, click **API permissions**
2. Click **+ Add a permission**
3. Select **APIs my organization uses**
4. Search for your API (the one you'll register in Step 2) or use the search box
5. Select your API and choose the scope (e.g., `user_impersonation` or custom scope)
6. Click **Add permissions**
7. **Important**: Click **Grant admin consent** if you're an admin (otherwise users will need to consent)

## Step 2: Register the Web API Application

### 2.1 Register the API App

1. Still in **App registrations**, click **+ New registration**
2. Fill in the details:
   - **Name**: `GridBanner API` (or any name you prefer)
   - **Supported account types**: Same as your client app
   - **Redirect URI**: Leave blank (not needed for API)
3. Click **Register**

### 2.2 Expose the API

1. In the left sidebar, click **Expose an API**
2. Click **Set** next to "Application ID URI"
3. Accept the default or customize it:
   - Default format: `api://{client-id}`
   - Example: `api://12345678-1234-1234-1234-123456789abc`
   - **Save this value** - this is your `azure_api_scope` base
4. **IMPORTANT**: Under **Authorized client applications**, click **+ Add a client application**
   - Enter the **Desktop Client App ID** (from Step 1.4)
   - Check the scope you're about to create (or check all scopes)
   - Click **Add**
   - This allows the desktop client to call the API
5. Click **+ Add a scope**
5. Fill in the scope details:
   - **Scope name**: `user_impersonation` (or `access_as_user`)
   - **Who can consent**: Admins and users (or Admins only)
   - **Admin consent display name**: `Access GridBanner API`
   - **Admin consent description**: `Allow the application to access GridBanner API on behalf of the signed-in user`
   - **User consent display name**: `Access GridBanner API`
   - **User consent description**: `Allow the application to access GridBanner API on your behalf`
   - **State**: Enabled
6. Click **Add scope**

### 2.3 Get API Configuration Values

1. Go to **Overview** page
2. **Application (client) ID** - Copy this value
   - This is your `azure_client_id` for the server config
   - This is different from the desktop client ID!

3. **Application ID URI** - This is what you set in step 2.2
   - Format: `api://{api-client-id}`
   - Your full scope will be: `api://{api-client-id}/.default` or `api://{api-client-id}/user_impersonation`

## Step 3: Configure Client App to Access API

### 3.1 Add API Permission to Client App

1. Go back to your **Desktop Client App** registration
2. Click **API permissions**
3. Click **+ Add a permission**
4. Select **APIs my organization uses**
5. Find and select your **GridBanner API** app
6. Select **Delegated permissions**
7. Check the scope you created (e.g., `user_impersonation`)
8. Click **Add permissions**
9. **Important**: Click **Grant admin consent** if you're an admin

## Step 4: Configure GridBanner

### 4.1 Client Configuration (GridBanner config file)

Add these values to your GridBanner configuration file:

```ini
azure_auth_enabled=1
azure_client_id={Desktop Client App ID from Step 1.3}
azure_tenant_id={Tenant ID from Step 1.3}
azure_api_scope=api://{API Client ID from Step 2.3}/.default
```

**Example:**
```ini
azure_auth_enabled=1
azure_client_id=12345678-1234-1234-1234-123456789abc
azure_tenant_id=87654321-4321-4321-4321-cba987654321
azure_api_scope=api://98765432-5678-5678-5678-987654321def/.default
```

### 4.2 Server Configuration (alert-server/config.json)

Add these values to your server's config.json:

```json
{
  "azure_auth_enabled": true,
  "azure_tenant_id": "87654321-4321-4321-4321-cba987654321",
  "azure_client_id": "98765432-5678-5678-5678-987654321def"
}
```

**Note**: The server's `azure_client_id` is the **API app's client ID**, not the desktop client's ID.

## Step 5: Install Server Dependencies

In your `alert-server` directory, run:

```bash
npm install
```

This will install the new dependencies (`jsonwebtoken` and `jwks-rsa`).

## Quick Reference: Where to Find Each Value

| Value | Where to Find | Used In |
|-------|---------------|---------|
| **Desktop Client ID** | Desktop Client App → Overview → Application (client) ID | GridBanner config: `azure_client_id` |
| **API Client ID** | API App → Overview → Application (client) ID | Server config: `azure_client_id` |
| **Tenant ID** | Either app → Overview → Directory (tenant) ID | Both configs: `azure_tenant_id` |
| **API Scope** | API App → Expose an API → Application ID URI + `/.default` | GridBanner config: `azure_api_scope` |

## Troubleshooting

### "Invalid client" error
- Check that the client ID in your config matches the Desktop Client App ID
- Verify the tenant ID is correct

### "Invalid audience" error
- Check that the API scope matches: `api://{API-client-id}/.default`
- Verify the API Client ID in server config matches the API app registration

### "Insufficient permissions" error
- Make sure you've added the API permission to the client app
- Grant admin consent if required
- Check that the scope name matches what you exposed

### "User mismatch" error
- The username in the API call must match the authenticated user's UPN/email
- Check that the user is authenticated correctly

## Testing

1. Start your alert server
2. Start GridBanner
3. Try to upload a key - you should see the device code authentication dialog
4. Complete authentication in your browser
5. The key upload should proceed with authentication

## Security Notes

- **Never commit** your Azure AD configuration values to public repositories
- Use environment variables or secure configuration management in production
- The tenant ID can be shared, but client IDs should be kept secure
- Consider using Azure Key Vault for production deployments

