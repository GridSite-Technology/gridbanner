## Releases (Windows x64 only)

This project currently ships **Windows x64** builds only.

### Contents
- **`GridBanner.msi`**: installs the GridBanner agent
- **`BannerManager.msi`**: installs the BannerManager alert editor/trigger tool

### Silent install (Intune-friendly)

```powershell
msiexec /i GridBanner.msi /qn /norestart ACCEPTEULA=1
msiexec /i BannerManager.msi /qn /norestart ACCEPTEULA=1
```

### Notes
- `BannerManager` writes alerts to `C:\gridsite\alert.json` (and makes timestamped backups next to it).
- `GridBanner` can monitor `C:\gridsite\alert.json` if configured via `alert_file_location`.


