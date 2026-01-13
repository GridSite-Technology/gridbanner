# Complete fix - handles both file content and commit messages
Write-Host "=== Complete Git History Fix ===" -ForegroundColor Cyan
Write-Host ""

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Create replacement script
$replaceScript = @'
#!/bin/sh
# Replace in files
find . -type f \( -name "*.md" -o -name "*.ps1" -o -name "*.json" -o -name "*.sh" -o -name "*.bat" \) ! -path "./.git/*" | while read file; do
    if [ -f "$file" ]; then
        sed -i "s|{your-client-secret}|{your-client-secret}|g" "$file"
        sed -i "s|{your-api-client-id}|{your-api-client-id}|g" "$file"
        sed -i "s|{your-desktop-client-id}|{your-desktop-client-id}|g" "$file"
        sed -i "s|{your-tenant-id}|{your-tenant-id}|g" "$file"
    fi
done
'@

$scriptPath = ".git-complete-fix.sh"
Set-Content -Path $scriptPath -Value $replaceScript -NoNewline -Encoding ASCII

# Create message filter to clean commit messages
$msgFilter = @'
sed "s|{your-client-secret}|{your-client-secret}|g; s|{your-api-client-id}|{your-api-client-id}|g; s|{your-desktop-client-id}|{your-desktop-client-id}|g; s|{your-tenant-id}|{your-tenant-id}|g"
'@

Write-Host "Running git filter-branch (files + messages)..." -ForegroundColor Yellow
git filter-branch --force --tree-filter "sh .git-complete-fix.sh" --msg-filter $msgFilter --prune-empty --tag-name-filter cat -- --all 2>&1 | Select-Object -Last 5

Write-Host ""
Write-Host "Cleaning up stashes..." -ForegroundColor Cyan
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

# Drop all stashes
$stashCount = (git stash list 2>&1 | Measure-Object).Count
for ($i = 0; $i -lt $stashCount; $i++) {
    git stash drop "stash@{$i}" 2>&1 | Out-Null
}

Remove-Item $scriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Final verification..." -ForegroundColor Cyan
$checks = @(
    @{Name="Client Secret"; Pattern="{your-client-secret}"},
    @{Name="API Client ID"; Pattern="{your-api-client-id}"},
    @{Name="Desktop Client ID"; Pattern="{your-desktop-client-id}"},
    @{Name="Tenant ID"; Pattern="{your-tenant-id}"}
)

$allClean = $true
foreach ($check in $checks) {
    $result = git log --all -S $check.Pattern --oneline 2>&1
    if ($result -and -not ($result -match "fatal") -and ($result | Measure-Object).Count -gt 0) {
        Write-Host "  [FAIL] $($check.Name) still found" -ForegroundColor Red
        $allClean = $false
    } else {
        Write-Host "  [OK] $($check.Name) removed" -ForegroundColor Green
    }
}

Write-Host ""
if ($allClean) {
    Write-Host "SUCCESS! All sensitive values removed from history." -ForegroundColor Green
    Write-Host ""
    Write-Host "Run: git push --force --all" -ForegroundColor Yellow
} else {
    Write-Host "Some values may remain. Try force pushing anyway:" -ForegroundColor Yellow
    Write-Host "  git push --force --all" -ForegroundColor Yellow
}
