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
```

### Configuration Options

- **background_color**: Hex color code for the banner background (default: `#000080` - Navy blue)
- **foreground_color**: Hex color code for the text (default: `#FFFFFF` - White)
- **classification_level**: Text to display in the center (default: `UNSPECIFIED CLASSIFICATION`)
- **banner_height**: Banner height in pixels (default: `30`)
- **org_name**: Optional override for organization name (default: auto-detected)
- **compliance_check_enabled**: `1` to show the badge, `0` to hide it (default: `1`)
- **compliance_status**: `1` for compliant (green) / `0` for NOT compliant (red) (default: `0`)
- **compliance_check_command**: Optional command to run at startup; exit code `0` is treated as compliant. If the command is missing/fails/times out, GridBanner treats the device as **NOT compliant** (default: empty)

## Usage

Simply run `GridBanner.exe`. The application will:
- Automatically detect all connected monitors
- Display a top banner on each monitor
- Reserve screen space (AppBar-style) so maximized windows appear below the banner
- Read configuration from INI files

To close the application, use Task Manager (Ctrl+Shift+Esc) and end the GridBanner process.

## Notes

- The banner appears at the top of each monitor and reserves space (AppBar)
- Azure AD organization name is detected from Windows environment variables and registry
- The application window does not appear in the taskbar

