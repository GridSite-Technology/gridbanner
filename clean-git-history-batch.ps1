# Batch-based git history cleanup - more reliable on Windows
# Run this from the repository root directory

param(
    [switch]$DryRun = $false
)

Write-Host "=== Git History Cleanup Script (Batch Method) ===" -ForegroundColor Cyan
Write-Host ""

# Check if we're in a git repo
if (-not (Test-Path ".git")) {
    Write-Host "ERROR: Not in a git repository!" -ForegroundColor Red
    exit 1
}

# Sensitive values to replace
# IMPORTANT: Update these with your actual sensitive values before running!
$replacements = @{
    "REPLACE_WITH_YOUR_API_CLIENT_ID" = "{your-api-client-id}"
    "REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID" = "{your-desktop-client-id}"
    "REPLACE_WITH_YOUR_TENANT_ID" = "{your-tenant-id}"
    "REPLACE_WITH_YOUR_CLIENT_SECRET" = "{your-client-secret}"
}

if ($DryRun) {
    Write-Host "DRY RUN MODE - Checking what would be changed..." -ForegroundColor Cyan
    foreach ($oldValue in $replacements.Keys) {
        $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
        if ($count -gt 0) {
            Write-Host "  Found '$oldValue' in $count commit(s)" -ForegroundColor Yellow
        }
    }
    exit 0
}

Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
$confirm = Read-Host "Continue? (type 'yes')"
if ($confirm -ne "yes") {
    exit 1
}

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

Write-Host ""
Write-Host "Creating replacement batch file..." -ForegroundColor Cyan

# Create a batch file that does the replacement
$batchContent = @'
@echo off
setlocal enabledelayedexpansion

set "file=%~1"
if not exist "%file%" exit /b 0

powershell -NoProfile -Command "$content = Get-Content '%file%' -Raw -ErrorAction SilentlyContinue; if ($content) { $content = $content -replace 'REPLACE_WITH_YOUR_API_CLIENT_ID', '{your-api-client-id}'; $content = $content -replace 'REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID', '{your-desktop-client-id}'; $content = $content -replace 'REPLACE_WITH_YOUR_TENANT_ID', '{your-tenant-id}'; $content = $content -replace 'REPLACE_WITH_YOUR_CLIENT_SECRET', '{your-client-secret}'; Set-Content -Path '%file%' -Value $content -NoNewline }"
'@

$batchPath = Join-Path $env:TEMP "git-replace.bat"
Set-Content -Path $batchPath -Value $batchContent -Encoding ASCII

# Files to process
$files = @("AZURE_AD_TROUBLESHOOTING.md", "AZURE_AD_SETUP_GUIDE.md", "AZURE_AUTH_PROPOSAL.md", "alert-server/config.json")

Write-Host "Running git filter-branch..." -ForegroundColor Cyan

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  Processing $file..." -ForegroundColor Yellow
        $filePath = $file.Replace('\', '/')
        $treeFilter = "cmd /c `"$batchPath`" $filePath"
        
        git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    [OK]" -ForegroundColor Green
        } else {
            Write-Host "    [FAILED]" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "Cleaning up references..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Remove-Item $batchPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
foreach ($oldValue in $replacements.Keys) {
    $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
    if ($count -eq 0) {
        Write-Host "  [OK] Removed: $($oldValue.Substring(0, 20))..." -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Still found in $count commit(s): $($oldValue.Substring(0, 20))..." -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next: git push --force --all" -ForegroundColor Yellow

