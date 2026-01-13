# Final script to remove sensitive Azure AD values from git history
# This script has the actual values hardcoded - run this to clean history

param(
    [switch]$DryRun = $false
)

Write-Host "=== Git History Cleanup Script ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path ".git")) {
    Write-Host "ERROR: Not in a git repository!" -ForegroundColor Red
    exit 1
}

# ACTUAL sensitive values to replace
$replacements = @{
    "{your-api-client-id}" = "{your-api-client-id}"
    "{your-desktop-client-id}" = "{your-desktop-client-id}"
    "{your-tenant-id}" = "{your-tenant-id}"
    "{your-client-secret}" = "{your-client-secret}"
}

if ($DryRun) {
    Write-Host "DRY RUN MODE - Checking what would be changed..." -ForegroundColor Cyan
    foreach ($oldValue in $replacements.Keys) {
        $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
        if ($count -gt 0) {
            Write-Host "  Found sensitive value in $count commit(s)" -ForegroundColor Yellow
        }
    }
    exit 0
}

Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host "Proceeding automatically..." -ForegroundColor Yellow

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

Write-Host ""
Write-Host "Creating batch wrapper script..." -ForegroundColor Cyan

# Create a batch file that calls PowerShell - more reliable for git filter-branch
$batchContent = @'
@echo off
setlocal enabledelayedexpansion
set "file=%~1"
if not exist "%file%" exit /b 0
powershell -NoProfile -Command "$c = Get-Content '%file%' -Raw; if ($c) { $c = $c -replace '{your-api-client-id}', '{your-api-client-id}' -replace '{your-desktop-client-id}', '{your-desktop-client-id}' -replace '{your-tenant-id}', '{your-tenant-id}' -replace '{your-client-secret}', '{your-client-secret}'; Set-Content '%file%' -Value $c -NoNewline }"
'@

$batchPath = Join-Path $PWD ".git-cleanup-temp.bat"
Set-Content -Path $batchPath -Value $batchContent -Encoding ASCII

Write-Host "Running git filter-branch..." -ForegroundColor Cyan

# Process all files in one filter-branch run
$files = @("AZURE_AD_TROUBLESHOOTING.md", "AZURE_AD_SETUP_GUIDE.md", "AZURE_AUTH_PROPOSAL.md", "alert-server/config.json", "REMOVE_SENSITIVE_DATA.md", "clean-git-history-simple.ps1", "clean-git-history.ps1", "clean-git-history-fixed.ps1", "clean-git-history-batch.ps1")

# Build a combined tree-filter that processes all files
$filterParts = @()
foreach ($file in $files) {
    if (Test-Path $file) {
        $filePath = $file.Replace('\', '/')
        $filterParts += ".git-cleanup-temp.bat `"$filePath`""
    }
}

if ($filterParts.Count -gt 0) {
    $treeFilter = $filterParts -join " && "
    Write-Host "Processing $($filterParts.Count) files in one pass..." -ForegroundColor Yellow
    
    $result = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] All files processed" -ForegroundColor Green
    } else {
        Write-Host "  [FAILED]" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Remove-Item $batchPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
foreach ($oldValue in $replacements.Keys) {
    $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
    if ($count -eq 0) {
        Write-Host "  [OK] Removed from history" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Still found in $count commit(s)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next: git push --force --all" -ForegroundColor Yellow
