@echo off
REM GridBanner Publish Script
REM This script creates a standalone executable

echo Publishing GridBanner as standalone executable...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

if errorlevel 1 (
    echo.
    echo Error: Publish failed
    echo Make sure .NET 8.0 SDK is installed
    pause
    exit /b 1
)

echo.
echo Publish successful!
echo Standalone executable location: bin\Release\net8.0-windows\win-x64\publish\GridBanner.exe
echo.
pause




