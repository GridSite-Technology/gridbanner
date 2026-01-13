# Clean git history of sensitive Azure AD values
# This script uses git filter-branch to replace sensitive values in all commits

Write-Host "=== Git History Cleanup ===" -ForegroundColor Cyan
Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Files that may contain sensitive data
$files = @(
    "CLEANUP_INSTRUCTIONS.md",
    "REMOVE_SENSITIVE_DATA.md",
    "clean-git-history-final.ps1",
    "clean-git-history-simple.ps1",
    "clean-git-history.ps1",
    "clean-git-history-fixed.ps1",
    "clean-git-history-batch.ps1",
    "clean-git-history-simple-final.ps1",
    "fix-commits.ps1",
    "AZURE_AD_TROUBLESHOOTING.md",
    "AZURE_AD_SETUP_GUIDE.md",
    "AZURE_AUTH_PROPOSAL.md",
    "alert-server/config.json"
)

# Build the tree-filter command
$filterCommands = @()
foreach ($file in $files) {
    $filePath = $file.Replace('\', '/')
    $filterCommands += @"
if [ -f "$filePath" ]; then
    sed -i "s/{your-client-secret}/{your-client-secret}/g" "$filePath"
    sed -i "s/{your-api-client-id}/{your-api-client-id}/g" "$filePath"
    sed -i "s/{your-desktop-client-id}/{your-desktop-client-id}/g" "$filePath"
    sed -i "s/{your-tenant-id}/{your-tenant-id}/g" "$filePath"
fi
"@
}

$treeFilter = $filterCommands -join " && "

Write-Host "Running git filter-branch (this may take several minutes)..." -ForegroundColor Yellow
Write-Host ""

# Run filter-branch
$result = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "SUCCESS: Git history cleaned!" -ForegroundColor Green
} else {
    Write-Host "ERROR: git filter-branch failed" -ForegroundColor Red
    Write-Host $result
    exit 1
}

Write-Host ""
Write-Host "Cleaning up git references..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
$secretCheck = git log --all -S "{your-client-secret}" --oneline 2>&1
if ($secretCheck -match "fatal" -or $secretCheck.Count -eq 0) {
    Write-Host "  [OK] Client secret removed from history" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] Client secret still found in history" -ForegroundColor Red
    Write-Host $secretCheck
}

$clientIdCheck = git log --all -S "{your-api-client-id}" --oneline 2>&1
if ($clientIdCheck -match "fatal" -or $clientIdCheck.Count -eq 0) {
    Write-Host "  [OK] API Client ID removed from history" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] API Client ID still found in history" -ForegroundColor Red
    Write-Host $clientIdCheck
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify: git log --all -S '0Fq8Q' --oneline" -ForegroundColor White
Write-Host "  2. Force push: git push --force --all" -ForegroundColor White
Write-Host "  3. Force push tags: git push --force --tags" -ForegroundColor White
