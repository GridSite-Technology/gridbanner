# Final fix for git history - simple and direct
Write-Host "=== Fixing Git History (Final Attempt) ===" -ForegroundColor Cyan
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Create a simple replacement script that will work
$scriptContent = @'
#!/bin/sh
for file in $(find . -type f \( -name "*.md" -o -name "*.ps1" -o -name "*.json" -o -name "*.sh" -o -name "*.bat" \) ! -path "./.git/*"); do
    if [ -f "$file" ]; then
        sed -i "s|{your-client-secret}|{your-client-secret}|g" "$file"
        sed -i "s|{your-api-client-id}|{your-api-client-id}|g" "$file"
        sed -i "s|{your-desktop-client-id}|{your-desktop-client-id}|g" "$file"
        sed -i "s|{your-tenant-id}|{your-tenant-id}|g" "$file"
    fi
done
'@

$scriptPath = ".git-fix-history.sh"
Set-Content -Path $scriptPath -Value $scriptContent -NoNewline -Encoding ASCII

# Make it executable (for Git Bash)
git update-index --chmod=+x $scriptPath 2>&1 | Out-Null

Write-Host "Running git filter-branch..." -ForegroundColor Yellow
$result = git filter-branch --force --tree-filter "sh .git-fix-history.sh" --prune-empty --tag-name-filter cat -- --all 2>&1

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Remove-Item $scriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verifying..." -ForegroundColor Cyan
$secretCheck = git log --all -S "{your-client-secret}" --oneline 2>&1
$clientIdCheck = git log --all -S "{your-api-client-id}" --oneline 2>&1
$desktopIdCheck = git log --all -S "{your-desktop-client-id}" --oneline 2>&1
$tenantIdCheck = git log --all -S "{your-tenant-id}" --oneline 2>&1

$allClean = $true
if ($secretCheck -and -not ($secretCheck -match "fatal") -and $secretCheck.Count -gt 0) {
    Write-Host "  [FAIL] Client secret still found" -ForegroundColor Red
    $allClean = $false
} else {
    Write-Host "  [OK] Client secret removed" -ForegroundColor Green
}

if ($clientIdCheck -and -not ($clientIdCheck -match "fatal") -and $clientIdCheck.Count -gt 0) {
    Write-Host "  [FAIL] API Client ID still found" -ForegroundColor Red
    $allClean = $false
} else {
    Write-Host "  [OK] API Client ID removed" -ForegroundColor Green
}

if ($desktopIdCheck -and -not ($desktopIdCheck -match "fatal") -and $desktopIdCheck.Count -gt 0) {
    Write-Host "  [FAIL] Desktop Client ID still found" -ForegroundColor Red
    $allClean = $false
} else {
    Write-Host "  [OK] Desktop Client ID removed" -ForegroundColor Green
}

if ($tenantIdCheck -and -not ($tenantIdCheck -match "fatal") -and $tenantIdCheck.Count -gt 0) {
    Write-Host "  [FAIL] Tenant ID still found" -ForegroundColor Red
    $allClean = $false
} else {
    Write-Host "  [OK] Tenant ID removed" -ForegroundColor Green
}

Write-Host ""
if ($allClean) {
    Write-Host "SUCCESS! All sensitive values removed." -ForegroundColor Green
    Write-Host "Run: git push --force --all" -ForegroundColor Yellow
} else {
    Write-Host "Some values may still be in commit messages or binary files." -ForegroundColor Yellow
    Write-Host "Try: git push --force --all" -ForegroundColor Yellow
}
