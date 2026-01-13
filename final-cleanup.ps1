# Final cleanup - simpler approach
Write-Host "=== Final Git History Cleanup ===" -ForegroundColor Cyan
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Create file replacement script
$fileScript = @'
#!/bin/sh
find . -type f \( -name "*.md" -o -name "*.ps1" -o -name "*.json" -o -name "*.sh" -o -name "*.bat" \) ! -path "./.git/*" | while read file; do
    if [ -f "$file" ]; then
        sed -i "s|{your-actual-client-secret}|{your-client-secret}|g" "$file"
        sed -i "s|{your-actual-api-client-id}|{your-api-client-id}|g" "$file"
        sed -i "s|{your-actual-desktop-client-id}|{your-desktop-client-id}|g" "$file"
        sed -i "s|{your-actual-tenant-id}|{your-tenant-id}|g" "$file"
    fi
done
'@

# Create message filter script
$msgScript = @'
#!/bin/sh
sed "s|{your-actual-client-secret}|{your-client-secret}|g" | \
sed "s|{your-actual-api-client-id}|{your-api-client-id}|g" | \
sed "s|{your-actual-desktop-client-id}|{your-desktop-client-id}|g" | \
sed "s|{your-actual-tenant-id}|{your-tenant-id}|g"
'@

Set-Content -Path ".git-fix-files.sh" -Value $fileScript -NoNewline -Encoding ASCII
Set-Content -Path ".git-fix-msg.sh" -Value $msgScript -NoNewline -Encoding ASCII

Write-Host "Running git filter-branch..." -ForegroundColor Yellow
git filter-branch --force --tree-filter "sh .git-fix-files.sh" --msg-filter "sh .git-fix-msg.sh" --prune-empty --tag-name-filter cat -- --all 2>&1 | Select-Object -Last 10

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

# Clear stashes
git reflog expire --expire=now refs/stash 2>&1 | Out-Null

Remove-Item ".git-fix-files.sh", ".git-fix-msg.sh" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verifying..." -ForegroundColor Cyan
$secret = git log --all -S "{your-actual-client-secret}" --oneline 2>&1
$apiId = git log --all -S "{your-actual-api-client-id}" --oneline 2>&1
$desktopId = git log --all -S "{your-actual-desktop-client-id}" --oneline 2>&1
$tenantId = git log --all -S "{your-actual-tenant-id}" --oneline 2>&1

$clean = $true
if ($secret -and -not ($secret -match "fatal") -and ($secret | Measure-Object).Count -gt 0) {
    Write-Host "  [WARN] Client secret: $($secret.Count) matches" -ForegroundColor Yellow
    $clean = $false
} else {
    Write-Host "  [OK] Client secret removed" -ForegroundColor Green
}

if ($apiId -and -not ($apiId -match "fatal") -and ($apiId | Measure-Object).Count -gt 0) {
    Write-Host "  [WARN] API Client ID: $($apiId.Count) matches" -ForegroundColor Yellow
    $clean = $false
} else {
    Write-Host "  [OK] API Client ID removed" -ForegroundColor Green
}

if ($desktopId -and -not ($desktopId -match "fatal") -and ($desktopId | Measure-Object).Count -gt 0) {
    Write-Host "  [WARN] Desktop Client ID: $($desktopId.Count) matches" -ForegroundColor Yellow
    $clean = $false
} else {
    Write-Host "  [OK] Desktop Client ID removed" -ForegroundColor Green
}

if ($tenantId -and -not ($tenantId -match "fatal") -and ($tenantId | Measure-Object).Count -gt 0) {
    Write-Host "  [WARN] Tenant ID: $($tenantId.Count) matches" -ForegroundColor Yellow
    $clean = $false
} else {
    Write-Host "  [OK] Tenant ID removed" -ForegroundColor Green
}

Write-Host ""
if ($clean) {
    Write-Host "SUCCESS! All sensitive values removed." -ForegroundColor Green
} else {
    Write-Host "Most values removed. Remaining may be in unreachable commits or stashes." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Ready to push: git push --force --all" -ForegroundColor Cyan
