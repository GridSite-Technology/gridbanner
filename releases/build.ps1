param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "out"

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Publishing GridBanner..."
dotnet publish (Join-Path $root "GridBanner.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $root "bin\$Configuration\net8.0-windows\win-x64\publish")

Write-Host "Publishing BannerManager..."
dotnet publish (Join-Path $root "BannerManager\BannerManager.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $root "BannerManager\bin\$Configuration\net8.0-windows\win-x64\publish")

Write-Host "Building MSIs (WiX Toolset SDK)..."
dotnet build (Join-Path $root "installers\GridBannerInstaller\GridBannerInstaller.wixproj") -c $Configuration
dotnet build (Join-Path $root "installers\BannerManagerInstaller\BannerManagerInstaller.wixproj") -c $Configuration

$gridMsi = Get-ChildItem (Join-Path $root "installers\GridBannerInstaller\bin\$Configuration") -Filter "GridBanner.msi" -Recurse | Select-Object -First 1
$mgrMsi  = Get-ChildItem (Join-Path $root "installers\BannerManagerInstaller\bin\$Configuration") -Filter "BannerManager.msi" -Recurse | Select-Object -First 1

if (-not $gridMsi) { throw "GridBanner.msi not found" }
if (-not $mgrMsi) { throw "BannerManager.msi not found" }

Copy-Item $gridMsi.FullName (Join-Path $outDir "GridBanner.msi") -Force
Copy-Item $mgrMsi.FullName  (Join-Path $outDir "BannerManager.msi") -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $outDir "README.md") -Force

Write-Host "Done. Output in: $outDir"


