# Simple git history cleanup - processes one file at a time with minimal escaping

param(
    [switch]$DryRun = $false
)

Write-Host "=== Git History Cleanup Script (Simple Method) ===" -ForegroundColor Cyan
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

# Create a simple sed-like replacement script
$scriptContent = @'
$f = $args[0]
if (Test-Path $f) {
    $c = [System.IO.File]::ReadAllText($f)
    $c = $c.Replace('{your-api-client-id}', '{your-api-client-id}')
    $c = $c.Replace('{your-desktop-client-id}', '{your-desktop-client-id}')
    $c = $c.Replace('{your-tenant-id}', '{your-tenant-id}')
    $c = $c.Replace('{your-client-secret}', '{your-client-secret}')
    [System.IO.File]::WriteAllText($f, $c)
}
'@

$scriptPath = ".git-replace.ps1"
Set-Content -Path $scriptPath -Value $scriptContent -Encoding UTF8

Write-Host "Processing files one at a time..." -ForegroundColor Cyan

$files = @("AZURE_AD_TROUBLESHOOTING.md", "AZURE_AD_SETUP_GUIDE.md", "AZURE_AUTH_PROPOSAL.md", "alert-server/config.json", "REMOVE_SENSITIVE_DATA.md", "clean-git-history-simple.ps1", "clean-git-history.ps1", "clean-git-history-fixed.ps1", "clean-git-history-batch.ps1")

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  Processing $file..." -ForegroundColor Yellow
        $fileEscaped = $file.Replace('\', '/')
        
        # Use simple relative path
        $filter = "powershell -NoProfile -File .git-replace.ps1 $fileEscaped"
        
        $output = git filter-branch --force --tree-filter $filter --prune-empty --tag-name-filter cat -- --all 2>&1 | Out-String
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    [OK]" -ForegroundColor Green
        } else {
            Write-Host "    [FAILED]" -ForegroundColor Red
            if ($output -match "Rewrite") {
                Write-Host "    (May have succeeded - check verification)" -ForegroundColor Gray
            }
        }
    }
}

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Remove-Item $scriptPath -ErrorAction SilentlyContinue

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

