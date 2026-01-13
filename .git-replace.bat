@echo off
if not exist "%~1" exit /b 0
powershell -NoProfile -Command "$f='%~1'; $c=[System.IO.File]::ReadAllText($f); $c=$c.Replace('{your-api-client-id}','{your-api-client-id}').Replace('{your-desktop-client-id}','{your-desktop-client-id}').Replace('{your-tenant-id}','{your-tenant-id}').Replace('{your-client-secret}','{your-client-secret}'); [System.IO.File]::WriteAllText($f,$c)"

