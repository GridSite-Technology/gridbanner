# GridBanner Alert Server

GridBanner includes a centralized alert management server (`alert-server`) that provides:

- **Web-based admin interface** for managing alerts
- **Template system** for reusable alert configurations
- **Site management** for organizing workstations by location
- **System monitoring** to track all connected GridBanner workstations
- **Audio file management** for custom alert sounds
- **REST API** for programmatic alert management

## Installation

1. **Install Node.js** (v14 or later):
   - Download from [nodejs.org](https://nodejs.org/)

2. **Install dependencies:**
   ```bash
   cd alert-server
   npm install
   ```

3. **Start the server:**
   ```bash
   npm start
   ```

   Server runs on `http://localhost:3000` by default.

## Configuration

The server uses `config.json` for configuration:

```json
{
  "admin_key": "your-secret-key-here"
}
```

**Environment Variables:**
- `PORT`: Server port (default: `3000`)
- `ADMIN_KEY`: Admin authentication key (default: `adminkey`)
- `ALERT_FILE`: Path to alert JSON file (default: `./alerts/current.json`)

## Web Interface

Access the admin interface at `http://localhost:3000` (or your server URL).

**Features:**
- **Alerts Tab**: Create and send one-time alerts
  - Select from templates or create custom alerts
  - Choose target sites (all sites or specific sites)
  - Preview colors and test alerts
- **Templates Tab**: Create, edit, and manage alert templates
  - Save common alert configurations for reuse
  - Edit templates with full alert customization
- **Sites Tab**: Manage site definitions
  - Create, edit, and delete sites
  - Sites are used for filtering alerts to specific workstations
- **Systems Tab**: Monitor connected GridBanner workstations
  - View all workstations that have reported in
  - See last seen time, user info, classification, location, company
  - View compliance status and classification colors
  - Filter, sort, and paginate system list
- **Settings Tab**: Configure server settings
  - Change admin key
  - Set default contact information
  - Manage audio files (upload, rename, delete)

## API Endpoints

**Public Endpoints (no authentication):**
- `GET /api/alert` - Get current alert (used by GridBanner clients)
- `GET /api/audio/:id/download` - Download audio file (used by GridBanner clients)

**Admin Endpoints (require `X-Admin-Key` header):**
- `POST /api/alert` - Create or update alert
- `DELETE /api/alert` - Clear current alert
- `GET /api/systems` - List all connected systems
- `GET /api/sites` - List all sites
- `POST /api/sites` - Create new site
- `PUT /api/sites/:id` - Update site
- `DELETE /api/sites/:id` - Delete site
- `GET /api/templates` - List all templates
- `POST /api/templates` - Create new template
- `PUT /api/templates/:id` - Update template
- `DELETE /api/templates/:id` - Delete template
- `GET /api/settings` - Get server settings
- `POST /api/settings` - Update server settings
- `GET /api/audio` - List all audio files
- `POST /api/audio` - Upload audio file
- `PUT /api/audio/:id` - Rename audio file
- `DELETE /api/audio/:id` - Delete audio file
- `GET /api/admin/key` - Get current admin key
- `POST /api/admin/key` - Update admin key

## Features

### Templates

- Create reusable alert configurations
- Include all alert fields (level, colors, message, site, contacts, audio)
- Use templates to quickly send common alerts

### Site Management

- Define sites (e.g., "HQ", "Remote", "Lab")
- Workstations are associated with sites via their `site_name` config
- Send alerts to specific sites or all sites

### System Monitoring

- Automatically tracks all GridBanner workstations that poll the server
- Displays:
  - Workstation name and username
  - Classification level and colors
  - Location (primary location with badge for multiple)
  - Company name
  - Compliance status (color-coded)
  - Last seen timestamp
- Filtering, sorting, and pagination support

### Audio File Management

- Upload custom audio files (MP3, WAV, etc.)
- Rename and delete audio files
- Select audio files when creating alerts/templates
- GridBanner clients automatically download audio files when needed

### Default Contact Information

- Set default contact info in Settings
- Automatically populated when creating new alerts/templates
- Can be overridden per alert

## GridBanner Client Configuration

Configure GridBanner workstations to use the alert server:

```ini
[Alerts]
alert_url = http://your-server:3000/api/alert
alert_poll_seconds = 5
```

**How it works:**
1. GridBanner polls the server every `alert_poll_seconds` seconds
2. On each poll, GridBanner sends system information (workstation name, user, classification, location, company, colors, compliance status)
3. Server returns current alert (if any)
4. GridBanner displays the alert if it matches the workstation's site configuration

## BannerManager Integration

BannerManager can connect to the alert server:

1. Open BannerManager
2. Configure **Server URL**: `http://your-server:3000`
3. Configure **Admin Key**: Your server admin key
4. Use **Trigger Alert** or **Clear Alert** - operations use the server API

## Security

**Important Security Considerations:**
- The server is designed for **internal/trusted networks**
- Admin key should be kept **secret** and changed from default
- Consider using **HTTPS** in production (via reverse proxy)
- Restrict network access via **firewall rules** if needed
- The web interface requires admin key authentication
- API endpoints require `X-Admin-Key` header for write operations

**Production Deployment Recommendations:**
1. Set a strong `ADMIN_KEY` environment variable
2. Use a reverse proxy (nginx, IIS) for HTTPS
3. Configure firewall rules to restrict access
4. Consider running as a Windows service or systemd service
5. Regularly update Node.js and dependencies
