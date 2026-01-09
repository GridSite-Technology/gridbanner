# GridBanner Teams App

A Microsoft Teams application that provides full alert management capabilities for GridBanner directly within Teams. Manage alerts, templates, sites, systems, and audio files without leaving your Teams environment.

## Features

- **Alert Management**: Create, view, and clear active alerts
- **Template Management**: Create, edit, and use alert templates
- **Site Management**: Add, edit, and delete sites
- **System Monitoring**: View all connected GridBanner systems
- **Audio File Management**: Upload, rename, and delete audio files
- **Settings**: Configure default contact information

## Prerequisites

- Microsoft Teams (desktop or web)
- Access to a GridBanner alert server
- Admin API key from the alert server

## Installation

### Option 1: Upload to Teams (Recommended for Testing)

1. **Prepare the App Package**:
   - Create a ZIP file containing:
     - `manifest.json`
     - `index.html`
     - `app.js`
     - `icon-color.png` (192x192 pixels)
     - `icon-outline.png` (32x32 pixels)

2. **Upload to Teams**:
   - Open Microsoft Teams
   - Go to Apps → Manage your apps → Upload a custom app
   - Select your ZIP file
   - The app will be available in your personal apps

### Option 2: Deploy to Web Server (Production)

1. **Host the Files**:
   - Upload `index.html` and `app.js` to a web server
   - Ensure HTTPS is enabled (required for Teams apps)
   - Update `manifest.json` with your domain:
     ```json
     "validDomains": ["yourdomain.com"],
     "contentUrl": "https://yourdomain.com/teams-app/index.html"
     ```

2. **Create App Package**:
   - Create a ZIP with `manifest.json` and icon files
   - Upload to Teams as described above

## Configuration

1. **Open the App**:
   - Navigate to the GridBanner Alerts tab in Teams

2. **Configure Connection**:
   - Enter your Alert Server URL (e.g., `http://your-server:3000`)
   - Enter your Admin API Key (found in `alert-server/config.json`)
   - Click "Save Configuration"
   - Click "Test Connection" to verify

3. **Start Managing**:
   - Once connected, you can manage all aspects of your GridBanner alerts

## Usage

### Creating Alerts

1. Navigate to the **Active Alerts** tab
2. Click **Create New Alert**
3. Fill in the alert details:
   - Select a template (optional) to pre-fill fields
   - Choose alert level (Routine, Urgent, Critical, Super Critical)
   - Enter summary and message
   - Set colors
   - Select target site (or leave empty for all sites)
   - Add contact information
4. Click **Create Alert**

### Using Templates

1. Navigate to the **Templates** tab
2. Click **Create New Template** to create a reusable template
3. Click **Use** on any template to create an alert from it
4. Click **Edit** to modify a template
5. Click **Delete** to remove a template

### Managing Sites

1. Navigate to the **Sites** tab
2. Click **Add New Site** to create a site
3. Click **Edit** to modify a site name
4. Click **Delete** to remove a site

### Viewing Systems

1. Navigate to the **Systems** tab
2. View all connected GridBanner systems
3. See system details including:
   - Workstation name
   - Username
   - Classification
   - Location
   - Company
   - Compliance status
   - Last seen timestamp

### Managing Audio Files

1. Navigate to the **Audio Files** tab
2. Click **Upload Audio File** to add a new sound
3. Click **Rename** to rename an audio file
4. Click **Delete** to remove an audio file

### Configuring Settings

1. Navigate to the **Settings** tab
2. Configure default contact information:
   - Contact Name
   - Phone
   - Email
   - Teams (email or Teams deep link)
3. Click **Save Settings**

## API Endpoints Used

The Teams app communicates with the alert server using the following endpoints:

- `GET /api/health` - Health check
- `GET /api/alert` - Get current alert
- `POST /api/alert` - Create/update alert
- `DELETE /api/alert` - Clear alert
- `GET /api/data` - Get all data (templates, sites, systems, settings)
- `POST /api/templates` - Create template
- `PUT /api/templates/:id` - Update template
- `DELETE /api/templates/:id` - Delete template
- `POST /api/sites` - Create site
- `PUT /api/sites/:id` - Update site
- `DELETE /api/sites/:id` - Delete site
- `PUT /api/settings` - Update settings
- `GET /api/audio` - List audio files
- `POST /api/audio` - Upload audio file
- `PUT /api/audio/:name` - Rename audio file
- `DELETE /api/audio/:name` - Delete audio file

All API requests require the `X-API-Key` header with your admin key.

## Security Notes

- The API key is stored in browser localStorage (encrypted by Teams)
- Always use HTTPS in production
- Regularly rotate your API keys
- Limit access to the admin key to authorized personnel only

## Troubleshooting

### Connection Issues

- Verify the server URL is correct and accessible
- Check that the API key matches `config.json` in the alert server
- Ensure the alert server is running
- Check browser console for detailed error messages

### App Not Loading

- Verify the manifest.json is valid
- Check that all required files are present
- Ensure your domain is listed in `validDomains` in the manifest
- Verify HTTPS is enabled (required for Teams apps)

### API Errors

- Check the alert server logs
- Verify the API key is correct
- Ensure the alert server supports all required endpoints
- Check CORS settings if hosting on a different domain

## Development

To modify the app:

1. Edit `index.html` for UI changes
2. Edit `app.js` for functionality changes
3. Update `manifest.json` for app metadata
4. Re-package and upload to Teams

## Support

For issues or questions:
- Check the main GridBanner README
- Review alert-server documentation
- Open an issue on GitHub

## License

Same license as GridBanner project.

