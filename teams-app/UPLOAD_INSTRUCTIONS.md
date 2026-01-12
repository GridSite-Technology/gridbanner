# Uploading GridBanner Teams App

## Prerequisites

1. **Alert Server Running**: Make sure the alert-server is running on `http://localhost:3000`
   - Navigate to `alert-server` directory
   - Run: `npm start` or `node server.js`

2. **Teams App Package**: `GridBannerTeamsApp.zip` is ready in the `teams-app` directory

## Upload Steps

1. **Open Microsoft Teams** (Desktop or Web)

2. **Navigate to Apps**:
   - Click on "Apps" in the left sidebar
   - Click on "Manage your apps" (or "Built for your org" if available)
   - Look for "Upload a custom app" or "Upload an app"

3. **Upload the Package**:
   - Click "Upload a custom app"
   - Select `GridBannerTeamsApp.zip` from the `teams-app` directory
   - Teams will validate the manifest

4. **Install the App**:
   - After validation, click "Add" or "Install"
   - The app will appear in your personal apps

5. **Open the App**:
   - Go to "Apps" in Teams
   - Find "GridBanner Alerts"
   - Click to open

6. **Configure**:
   - Enter your Alert Server URL: `http://localhost:3000` (or your server URL)
   - Enter your API Key: `adminkey` (from `alert-server/config.json`)
   - Click "Save Configuration"
   - Click "Test Connection"

## Troubleshooting

### App won't load
- Make sure the alert-server is running
- Check that `index.html` and `app.js` are in `alert-server/public/`
- Verify the server is accessible at the URL you configured

### Connection fails
- Verify the API key matches `alert-server/config.json`
- Check that the server is running and accessible
- Look at browser console (F12) for detailed errors

### Manifest validation errors
- Make sure all required files are in the ZIP
- Verify the manifest.json is valid JSON
- Check that icon files are the correct size (192x192 and 32x32)

## Development Notes

- The Teams app HTML/JS files are served from the alert-server's `public` directory
- For production, update the manifest `contentUrl` to your production server URL
- Update `validDomains` in manifest.json to match your domain


