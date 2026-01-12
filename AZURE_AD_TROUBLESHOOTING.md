# Azure AD Authentication Troubleshooting

## Error: AADSTS7000218 - client_assertion or client_secret required

This error means Azure AD is treating your desktop app as a confidential client instead of a public client.

### Solution 1: Enable Public Client Flows (Most Common Fix)

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Find your **Desktop Client App** (Client ID: `{your-desktop-client-id}`)
4. Click on it, then go to **Authentication** in the left sidebar
5. Scroll down to **Advanced settings**
6. Under **Allow public client flows**, change it to **Yes**
7. Click **Save** at the top
8. Wait 1-2 minutes for changes to propagate
9. Try authenticating again

### Solution 2: Verify App Type

1. In your Desktop Client App registration
2. Go to **Overview**
3. Check that it shows **"Public client/native (mobile & desktop)"** under **Supported account types**
4. If it shows "Web", you registered it incorrectly - you need to register a new app as "Public client/native"

### Solution 3: Authorize Desktop Client in API App

1. Go to your **API App** registration (Client ID: `{your-api-client-id}`)
2. Click **Expose an API**
3. Under **Authorized client applications**, make sure your Desktop Client ID (`{your-desktop-client-id}`) is listed
4. If not, click **+ Add a client application** and add it

### Solution 4: Verify Scope Format

Make sure your `azure_api_scope` in `conf.ini` is:
```
azure_api_scope = api://{your-api-client-id}/.default
```

**NOT**:
- `api://{your-api-client-id}/user_impersonation` ❌
- `{your-api-client-id}/.default` ❌ (missing `api://`)

### Verification Steps

After making changes:
1. Wait 1-2 minutes for Azure AD changes to propagate
2. Restart GridBanner
3. Try authenticating again
4. Check the logs at `%USERPROFILE%\userdata\gridbanner\gridbanner.log`

### Still Not Working?

If you've done all of the above and it still doesn't work:

1. **Double-check the Client IDs**:
   - Desktop Client ID in GridBanner config: `{your-desktop-client-id}`
   - API Client ID in server config: `{your-api-client-id}`
   - Make sure you're using the correct IDs in the correct places

2. **Check API Permissions**:
   - In Desktop Client App → **API permissions**
   - Make sure the API permission is granted (click **Grant admin consent** if needed)

3. **Try a different scope**:
   - Temporarily try: `azure_api_scope = {your-desktop-client-id}/.default`
   - This will help determine if it's a scope issue or an app configuration issue

4. **Check Azure AD logs**:
   - Go to Azure Portal → **Azure Active Directory** → **Sign-in logs**
   - Look for failed sign-ins with your user account
   - Check the error details

