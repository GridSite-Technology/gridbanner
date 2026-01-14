# GridBanner

Modern replacement for NetBanner.exe - A multi-monitor banner application that displays user information, classification level, Azure AD organization name, and an optional device compliance badge across all screens.

## Features

- **Multi-Monitor Support**: Automatically detects and displays banners on all connected monitors
- **User Information Display**:
  - Username (top left)
  - Classification level (center)
  - Azure AD organization name (top right)
- **Device Compliance Badge (Optional)**:
  - Right-side badge showing compliant/non-compliant status with green/red background
  - Can be driven by config or an optional command
- **Configuration Management**: 
  - System-level config at `C:\gridbanner\conf.ini` (overrides user config)
  - User-level config at `%USERPROFILE%\userdata\gridbanner\conf.ini`
  - Auto-creates default user config if none exists
- **Native Windows Application**: Compiled C#/.NET application - no runtime dependencies required

## Requirements

- .NET 8.0 SDK or Runtime (for building)
- Windows 10/11

## Building from Source

1. Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

2. Open a command prompt in the project directory and run:
```bash
dotnet build -c Release
```

3. The executable will be created at:
   ```
   bin\Release\net8.0-windows\win-x64\GridBanner.exe
   ```

## Creating a Standalone Executable

To create a single-file executable that includes the .NET runtime:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be in:
```
bin\Release\net8.0-windows\win-x64\publish\GridBanner.exe
```

This creates a standalone EXE that doesn't require .NET to be installed on the target machine.

## Configuration

The application reads configuration from INI files. Configuration files are checked in this order:
1. System config: `C:\gridbanner\conf.ini` (highest priority)
2. User config: `%USERPROFILE%\userdata\gridbanner\conf.ini`
3. Default config (created automatically if no config exists)

### Configuration File Format

Create or edit `conf.ini`:

```ini
[Display]
background_color = #FFA500
foreground_color = #FFFFFF
classification_level = UNSPECIFIED CLASSIFICATION
banner_height = 30

; Optional: override detected organization name
org_name =

; Device compliance badge (right side)
; compliance_check_enabled: 1=show badge, 0=hide badge
compliance_check_enabled = 1
; compliance_status: 1=compliant (green), 0=NOT compliant (red)
; Note: GridBanner is conservative — it only shows COMPLIANT if a real check proves it.
compliance_status = 0
; Optional: command to determine compliance. Exit code 0 => compliant, non-zero => non-compliant.
; Example: compliance_check_command = powershell.exe -NoProfile -Command "exit 0"
compliance_check_command =

; Alert system configuration
; alert_file_location: Path to local JSON file for alerts (alternative to alert_url)
alert_file_location = 
; alert_url: URL to alert server API endpoint
alert_url = 
; alert_poll_seconds: How often to poll for alerts (1-300 seconds, default: 5)
alert_poll_seconds = 5

; Multi-site support: comma-separated list of site names (e.g., "HQ,Remote,Lab")
; If not set, workstation receives all alerts (backward compatible)
site_name = 

; Tray-only mode: 1=hide banner (show only when alerts are active), 0=show banner always
tray_only = 0

; Security and menu options
; permit_terminate: 1=allow terminate option in menu, 0=hide terminate option (default: 0)
permit_terminate = 0
; disable_triple_click_menu: 1=disable triple-click menu, 0=enable triple-click menu (default: 0)
disable_triple_click_menu = 0

; Keyring feature: centralized public key management
; keyring_enabled: 1=enable keyring feature, 0=disable keyring feature (default: 0)
keyring_enabled = 0
```

### Configuration Options

#### Display Settings

- **background_color**: Hex color code for the banner background (default: `#FFA500` - Orange)
- **foreground_color**: Hex color code for the text (default: `#FFFFFF` - White)
- **classification_level**: Text to display in the center (default: `UNSPECIFIED CLASSIFICATION`)
- **banner_height**: Banner height in pixels (default: `30`, min: `20`, max: `300`)
- **org_name**: Optional override for organization name (default: auto-detected from Azure AD)

#### Device Compliance Badge

- **compliance_check_enabled**: `1` to show the badge, `0` to hide it (default: `1`)
- **compliance_status**: `1` for compliant (green) / `0` for NOT compliant (red) (default: `0`)
  - Used only if no command is set or command fails
  - Note: GridBanner is conservative — it only shows COMPLIANT if a real check proves it
- **compliance_check_command**: Optional command to run at startup; exit code `0` is treated as compliant. If the command is missing/fails/times out, GridBanner treats the device as **NOT compliant** (default: empty)
  - Example: `compliance_check_command = powershell.exe -NoProfile -Command "exit 0"`

#### Alert System

- **alert_file_location**: Path to local JSON file for alerts (alternative to `alert_url`)
  - Example: `alert_file_location = C:\gridbanner\alerts\current.json`
- **alert_url**: URL to alert server API endpoint
  - Example: `alert_url = https://example.com/api/alert`
- **alert_poll_seconds**: How often to poll for alerts in seconds (default: `5`, min: `1`, max: `300`)

#### Multi-Site Support

- **site_name**: Comma-separated list of site names (e.g., `"HQ,Remote,Lab"`)
  - If not set, workstation receives all alerts (backward compatible)
  - Used to filter alerts to specific workstations

#### Tray-Only Mode

- **tray_only**: `1` to hide banner (show only when alerts are active), `0` to show banner always (default: `0`)
  - When enabled, displays a system tray icon with the configured background color
  - Banner appears automatically when alerts are triggered
  - Right-click tray icon for menu access (same options as triple-click menu)

#### Security and Menu Options

- **permit_terminate**: `1` to allow terminate option in menu, `0` to hide terminate option (default: `0`)
- **disable_triple_click_menu**: `1` to disable triple-click menu, `0` to enable triple-click menu (default: `0`)
  - When enabled, triple-clicking the banner shows a context menu with options

#### Keyring Feature

- **keyring_enabled**: `1` to enable keyring feature, `0` to disable keyring feature (default: `0`)
  - When enabled, GridBanner detects local SSH keys and offers to upload them to the alert server
  - Requires `alert_url` to be configured

#### Clipboard Monitoring and Paste Protection

- **clipboard_monitoring_enabled**: `1` to enable clipboard monitoring and paste blocking, `0` to disable (default: `0`)
  - When enabled, GridBanner monitors clipboard operations and blocks pastes when sensitivity levels don't match
  - Detects sensitivity labels from Office documents (Word, Excel, PowerPoint)
  - Detects sensitivity from browser URLs (Chrome, Edge, Firefox)
  - Blocks paste operations when pasting to destinations with lower sensitivity levels
  - Shows warning dialogs when paste is blocked
  - Configuration is stored in `%USERPROFILE%\userdata\gridbanner\sensitivity.json`

## Usage

Simply run `GridBanner.exe`. The application will:
- Automatically detect all connected monitors
- Display a top banner on each monitor
- Reserve screen space (AppBar-style) so maximized windows appear below the banner
- Read configuration from INI files

To close the application, use Task Manager (Ctrl+Shift+Esc) and end the GridBanner process.

## Microsoft Teams Integration

GridBanner includes a Microsoft Teams app that allows you to manage alerts, templates, sites, systems, and audio files directly from Teams. This provides a convenient way to manage your GridBanner alerts without leaving your Teams environment.

### Features

- **Alert Management**: Create, view, and clear active alerts
- **Template Management**: Create, edit, and use alert templates
- **Site Management**: Add, edit, and delete sites
- **System Monitoring**: View all connected GridBanner systems
- **Audio File Management**: Upload, rename, and delete audio files
- **Settings**: Configure default contact information

### Installation

See the [Teams App README](teams-app/README.md) for detailed installation and configuration instructions.

The Teams app requires:
- Access to a GridBanner alert server
- Admin API key from the alert server
- Microsoft Teams (desktop or web)

## Notes

- The banner appears at the top of each monitor and reserves space (AppBar)
- Azure AD organization name is detected from Windows environment variables and registry
- The application window does not appear in the taskbar

