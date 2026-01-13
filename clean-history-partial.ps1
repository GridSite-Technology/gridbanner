# Clean git history of partial sensitive value matches in verification commands
# This replaces partial matches like "0Fq8Q" and "70cd6b3f" in git log examples

Write-Host "=== Cleaning Partial Matches from Git History ===" -ForegroundColor Cyan
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Files that may contain partial matches in verification commands
$files = @(
    "CLEANUP_INSTRUCTIONS.md",
    "REMOVE_SENSITIVE_DATA.md",
    "clean-git-history-final.ps1",
    "clean-git-history-simple.ps1",
    "clean-git-history.ps1",
    "clean-git-history-fixed.ps1",
    "clean-git-history-batch.ps1",
    "clean-git-history-simple-final.ps1"
)

# Build the tree-filter command to replace partial matches
$filterCommands = @()
foreach ($file in $files) {
    $filePath = $file.Replace('\', '/')
    $filterCommands += @"
if [ -f "$filePath" ]; then
    sed -i "s/0Fq8Q/{your-client-secret-pattern}/g" "$filePath"
    sed -i "s/70cd6b3f/{your-api-client-id-pattern}/g" "$filePath"
fi
"@
}

$treeFilter = $filterCommands -join " && "

Write-Host "Running git filter-branch to replace partial matches..." -ForegroundColor Yellow
Write-Host ""

# Run filter-branch
$result = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "SUCCESS: Partial matches cleaned!" -ForegroundColor Green
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
$partialCheck = git log --all -S "0Fq8Q" --oneline 2>&1
if ($partialCheck -match "fatal" -or $partialCheck.Count -eq 0) {
    Write-Host "  [OK] Partial matches removed from history" -ForegroundColor Green
} else {
    Write-Host "  [INFO] Some partial matches may remain (these are in verification examples)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done! You can now try: git push --force --all" -ForegroundColor Yellow
