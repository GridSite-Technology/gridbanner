# GridBanner Alert Server

Centralized alert management server for GridBanner. Provides a REST API and web admin interface for managing alerts across all GridBanner workstations.

## Features

- **REST API**: Create, update, and clear alerts via HTTP endpoints
- **Web Admin Interface**: User-friendly HTML interface for managing alerts
- **Simple Key Authentication**: Admin key required for write operations
- **Site-Based Filtering**: Supports site-specific alerts (via alert JSON `site` field)

## Quick Start

1. **Install dependencies:**
   ```bash
   cd alert-server
   npm install
   ```

2. **Set admin key (optional, defaults to "changeme"):**
   ```bash
   # Windows PowerShell
   $env:ADMIN_KEY="your-secret-key-here"
   npm start

   # Or set in environment before starting
   ```

3. **Start the server:**
   ```bash
   npm start
   ```

   Server runs on `http://localhost:3000` by default.

4. **Access admin interface:**
   Open `http://localhost:3000?key=your-admin-key` in a browser.

## Configuration

Environment variables:

- `PORT`: Server port (default: `3000`)
- `ADMIN_KEY`: Admin authentication key (default: `changeme`)
- `ALERT_FILE`: Path to alert JSON file (default: `./alerts/current.json`)

Example:
```bash
PORT=8080 ADMIN_KEY=my-secret-key npm start
```

## API Endpoints

### GET /api/alert
Get current alert (public, no auth required).

**Response:**
- `200 OK`: Alert JSON object
- `204 No Content`: No active alert

### POST /api/alert
Create or update alert (requires admin key).

**Headers:**
- `X-Admin-Key: your-admin-key`

**Body:** Alert JSON object
```json
{
  "level": "urgent",
  "summary": "Alert Summary",
  "message": "Full alert message",
  "background_color": "#FF0000",
  "foreground_color": "#FFFFFF",
  "site": "HQ",
  "alert_contact_name": "Contact Name",
  "alert_contact_phone": "+1 (555) 123-4567",
  "alert_contact_email": "contact@example.com",
  "alert_contact_teams": "https://teams.microsoft.com/..."
}
```

**Response:**
- `200 OK`: `{"success": true, "message": "Alert updated"}`

### DELETE /api/alert
Clear current alert (requires admin key).

**Headers:**
- `X-Admin-Key: your-admin-key`

**Response:**
- `200 OK`: `{"success": true, "message": "Alert cleared"}`

## BannerManager Integration

Configure BannerManager to use the server:

1. Open BannerManager
2. Set **Server URL**: `http://localhost:3000` (or your server URL)
3. Set **Admin Key**: Your admin key
4. Click **Trigger Alert** or **Clear Alert** - operations will use the server API

## GridBanner Client Configuration

Configure GridBanner workstations to poll the server:

In `conf.ini`:
```ini
alert_url = http://your-server:3000/api/alert
alert_poll_seconds = 5
```

Workstations will automatically fetch alerts from the server.

## Security Notes

- The server is designed for internal/trusted networks
- Admin key should be kept secret
- Consider using HTTPS in production
- Restrict network access via firewall if needed
- The admin web interface requires the key as a query parameter or prompt

## Deployment

For production deployment:

1. Set a strong `ADMIN_KEY` environment variable
2. Use a reverse proxy (nginx, IIS) for HTTPS
3. Configure firewall rules to restrict access
4. Consider running as a Windows service or systemd service

