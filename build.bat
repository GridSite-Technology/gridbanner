@echo off
REM GridBanner Build Script
REM This script builds GridBanner.exe

echo Building GridBanner...
dotnet build -c Release

if errorlevel 1 (
    echo.
    echo Error: Build failed
    echo Make sure .NET 8.0 SDK is installed
    pause
    exit /b 1
)

echo.
echo Build successful!
echo Executable location: bin\Release\net8.0-windows\win-x64\GridBanner.exe
echo.
pause





