# Fix git history - replace actual sensitive values with placeholders
Write-Host "=== Fixing Git History ===" -ForegroundColor Cyan
Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# The actual sensitive values that need to be replaced
$replacements = @{
    "{your-client-secret}" = "{your-client-secret}"
    "{your-api-client-id}" = "{your-api-client-id}"
    "{your-desktop-client-id}" = "{your-desktop-client-id}"
    "{your-tenant-id}" = "{your-tenant-id}"
}

Write-Host "Building replacement commands..." -ForegroundColor Yellow

# Build sed commands for each replacement
$sedCommands = @()
foreach ($old in $replacements.Keys) {
    $new = $replacements[$old]
    # Escape special characters for sed
    $oldEscaped = $old -replace '([\\/.*+?|()\[\]{}^$])', '\$1'
    $newEscaped = $new -replace '([\\/&])', '\$1'
    $sedCommands += "sed -i 's/$oldEscaped/$newEscaped/g'"
}

# Process all text files
$treeFilter = @"
find . -type f \( -name '*.md' -o -name '*.ps1' -o -name '*.json' -o -name '*.sh' -o -name '*.bat' \) ! -path './.git/*' -exec sh -c 'for f; do $($sedCommands -join '; ') `"`$f`"; done' _ {} +
"@

Write-Host "Running git filter-branch (this will take a few minutes)..." -ForegroundColor Yellow
Write-Host ""

# Run filter-branch
$result = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "SUCCESS!" -ForegroundColor Green
} else {
    Write-Host "Filter-branch completed (checking results)..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Cleaning up references..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Write-Host ""
Write-Host "Verifying cleanup..." -ForegroundColor Cyan
$found = $false
foreach ($old in $replacements.Keys) {
    $check = git log --all -S $old --oneline 2>&1
    if ($check -and -not ($check -match "fatal")) {
        Write-Host "  [WARNING] Still found: $($old.Substring(0, [Math]::Min(20, $old.Length)))..." -ForegroundColor Yellow
        $found = $true
    } else {
        Write-Host "  [OK] Removed: $($old.Substring(0, [Math]::Min(20, $old.Length)))..." -ForegroundColor Green
    }
}

if (-not $found) {
    Write-Host ""
    Write-Host "SUCCESS! All sensitive values removed from history." -ForegroundColor Green
    Write-Host ""
    Write-Host "Next: git push --force --all" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Some values may still be present. Check the output above." -ForegroundColor Yellow
}
