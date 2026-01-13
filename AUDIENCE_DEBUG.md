# Debugging JWT Audience Mismatch

## The Problem

The server expects a specific audience in JWT tokens, but the token has a different audience.

## Understanding Application ID vs Object ID

In Azure AD:
- **Application ID (Client ID)**: `{your-api-client-id}` - This is what should be used for the audience
- **Object ID**: `b3829a51-5793-4816-8461-78c2acc42669` - This is the unique identifier for the app object

## The Application ID URI

The audience in JWT tokens is determined by the **Application ID URI** set in Azure AD:

1. Go to Azure Portal → App registrations → Your API app (`{your-api-client-id}`)
2. Click **Expose an API**
3. Check the **Application ID URI** field
4. It should be: `api://{your-api-client-id}`
5. If it's set to `api://b3829a51-5793-4816-8461-78c2acc42669` (using Object ID), that's the problem!

## Fix Options

### Option 1: Change Application ID URI to Use Application ID (Recommended)

1. In Azure Portal, go to your API app registration
2. Click **Expose an API**
3. Click **Set** next to Application ID URI (if not set) or click the edit icon
4. Change it to: `api://{your-api-client-id}`
5. Click **Save**
6. Wait 1-2 minutes for changes to propagate
7. Restart the alert-server
8. Restart GridBanner client (it will need to get a new token)

### Option 2: Update Server Config to Match Current Application ID URI

If the Application ID URI is intentionally set to use the Object ID, update the server config:

```json
{
  "azure_client_id": "b3829a51-5793-4816-8461-78c2acc42669"
}
```

And update the client's `conf.ini`:
```ini
azure_api_scope = api://b3829a51-5793-4816-8461-78c2acc42669/.default
```

**Note**: Option 1 is recommended because Application IDs are more stable and standard.

## Debugging Output

With the new debugging code, when a token validation fails, you'll see:
- Expected audience: `api://{client-id-from-config}`
- Token audience: `{actual-audience-from-token}`
- Token scopes: `{scopes-in-token}`
- Token appid: `{app-id-that-requested-token}`

This will help identify the mismatch.
