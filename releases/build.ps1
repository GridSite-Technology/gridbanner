param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "out"

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

function Exec([string]$cmd, [string[]]$arguments) {
  & $cmd @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed: $cmd $($arguments -join ' ') (exit=$LASTEXITCODE)"
  }
}

Write-Host "Publishing GridBanner..."
# Folder publish (self-contained). This avoids single-file self-extract issues in locked-down environments.
Exec "dotnet" @("publish", (Join-Path $root "GridBanner.csproj"), "-c", $Configuration, "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=false", "-o", (Join-Path $root "bin\$Configuration\net8.0-windows\win-x64\publish"))

Write-Host "Publishing BannerManager..."
# Folder publish (self-contained). This avoids single-file self-extract issues in locked-down environments.
Exec "dotnet" @("publish", (Join-Path $root "BannerManager\BannerManager.csproj"), "-c", $Configuration, "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=false", "-o", (Join-Path $root "BannerManager\bin\$Configuration\net8.0-windows\win-x64\publish"))

Write-Host "Building MSIs (WiX Toolset SDK)..."
Exec "dotnet" @("build", (Join-Path $root "installers\GridBannerInstaller\GridBannerInstaller.wixproj"), "-c", $Configuration, "-p:Platform=x64")
Exec "dotnet" @("build", (Join-Path $root "installers\BannerManagerInstaller\BannerManagerInstaller.wixproj"), "-c", $Configuration, "-p:Platform=x64")

$gridMsi = Get-ChildItem (Join-Path $root "installers\GridBannerInstaller\bin\$Configuration") -Filter "GridBanner.msi" -Recurse | Select-Object -First 1
$mgrMsi  = Get-ChildItem (Join-Path $root "installers\BannerManagerInstaller\bin\$Configuration") -Filter "BannerManager.msi" -Recurse | Select-Object -First 1

if (-not $gridMsi) { throw "GridBanner.msi not found" }
if (-not $mgrMsi) { throw "BannerManager.msi not found" }

Copy-Item $gridMsi.FullName (Join-Path $outDir "GridBanner.msi") -Force
Copy-Item $mgrMsi.FullName  (Join-Path $outDir "BannerManager.msi") -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $outDir "README.md") -Force

# Also copy the main EXEs for direct download (dependencies remain in the publish folders)
$gridExe = Join-Path $root "bin\$Configuration\net8.0-windows\win-x64\publish\GridBanner.exe"
$mgrExe  = Join-Path $root "BannerManager\bin\$Configuration\net8.0-windows\win-x64\publish\BannerManager.exe"
if (Test-Path $gridExe) { Copy-Item $gridExe (Join-Path $outDir "GridBanner.exe") -Force }
if (Test-Path $mgrExe)  { Copy-Item $mgrExe  (Join-Path $outDir "BannerManager.exe") -Force }

Write-Host "Done. Output in: $outDir"


