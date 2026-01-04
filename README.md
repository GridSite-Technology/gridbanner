# GridBanner

Modern replacement for NetBanner.exe - A multi-monitor banner application that displays user information, classification level, and Azure AD organization name across all screens.

## Features

- **Multi-Monitor Support**: Automatically detects and displays banners on all connected monitors
- **User Information Display**:
  - Username (top left)
  - Classification level (center)
  - Azure AD organization name (top right)
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
background_color = #000080
foreground_color = #FFFFFF
classification_level = UNCLASSIFIED
```

### Configuration Options

- **background_color**: Hex color code for the banner background (default: `#000080` - Navy blue)
- **foreground_color**: Hex color code for the text (default: `#FFFFFF` - White)
- **classification_level**: Text to display in the center (default: `UNCLASSIFIED`)

## Usage

Simply run `GridBanner.exe`. The application will:
- Automatically detect all connected monitors
- Display a fullscreen banner on each monitor
- Stay on top of all windows
- Read configuration from INI files

To close the application, use Task Manager (Ctrl+Shift+Esc) and end the GridBanner process.

## Notes

- The banner appears as a fullscreen overlay on all monitors
- The application stays on top of all windows
- Azure AD organization name is detected from Windows environment variables and registry
- The application window does not appear in the taskbar

