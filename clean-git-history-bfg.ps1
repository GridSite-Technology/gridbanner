# Clean git history using BFG Repo-Cleaner (recommended method)
# Download BFG from: https://rtyley.github.io/bfg-repo-cleaner/

Write-Host "=== Git History Cleanup using BFG Repo-Cleaner ===" -ForegroundColor Cyan
Write-Host ""

# Check if BFG jar exists
$bfgPath = "bfg.jar"
if (-not (Test-Path $bfgPath)) {
    Write-Host "ERROR: bfg.jar not found!" -ForegroundColor Red
    Write-Host "Download from: https://rtyley.github.io/bfg-repo-cleaner/" -ForegroundColor Yellow
    Write-Host "Place bfg.jar in the repository root directory." -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating replacements file..." -ForegroundColor Cyan

# Create replacements file for BFG
# IMPORTANT: Update these with your actual sensitive values before running!
$replacements = @"
REPLACE_WITH_YOUR_API_CLIENT_ID==>{your-api-client-id}
REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID==>{your-desktop-client-id}
REPLACE_WITH_YOUR_TENANT_ID==>{your-tenant-id}
REPLACE_WITH_YOUR_CLIENT_SECRET==>{your-client-secret}
"@

$replacementsFile = ".bfg-replacements.txt"
Set-Content -Path $replacementsFile -Value $replacements -Encoding ASCII

Write-Host "Running BFG..." -ForegroundColor Cyan
java -jar $bfgPath --replace-text $replacementsFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "Cleaning up..." -ForegroundColor Cyan
    git reflog expire --expire=now --all
    git gc --prune=now --aggressive
    
    Write-Host ""
    Write-Host "Verification:" -ForegroundColor Cyan
    $sensitive = @("REPLACE_WITH_YOUR_API_CLIENT_ID", "REPLACE_WITH_YOUR_CLIENT_SECRET")
    foreach ($val in $sensitive) {
        $count = (git log --all -S $val --oneline 2>&1 | Measure-Object).Count
        if ($count -eq 0) {
            Write-Host "  [OK] Removed: $val" -ForegroundColor Green
        } else {
            Write-Host "  [FAIL] Still found: $val" -ForegroundColor Red
        }
    }
    
    Write-Host ""
    Write-Host "Next: git push --force --all" -ForegroundColor Yellow
} else {
    Write-Host "BFG failed!" -ForegroundColor Red
}

Remove-Item $replacementsFile -ErrorAction SilentlyContinue

