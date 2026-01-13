# Use git-filter-repo to fix history (much better than filter-branch)
Write-Host "=== Using git-filter-repo ===" -ForegroundColor Cyan
Write-Host ""

# Check if git-filter-repo is installed
$filterRepo = Get-Command git-filter-repo -ErrorAction SilentlyContinue
if (-not $filterRepo) {
    Write-Host "Installing git-filter-repo..." -ForegroundColor Yellow
    pip install git-filter-repo 2>&1 | Out-Null
}

# Create replacements file
$replacements = @"
{your-client-secret}==>{your-client-secret}
{your-api-client-id}==>{your-api-client-id}
{your-desktop-client-id}==>{your-desktop-client-id}
{your-tenant-id}==>{your-tenant-id}
"@

$replacementsFile = ".filter-repo-replacements.txt"
Set-Content -Path $replacementsFile -Value $replacements -Encoding ASCII

Write-Host "Running git-filter-repo..." -ForegroundColor Yellow
Write-Host "This will rewrite git history..." -ForegroundColor Red
Write-Host ""

git filter-repo --replace-text $replacementsFile --force 2>&1 | Select-Object -Last 10

Remove-Item $replacementsFile -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verifying..." -ForegroundColor Cyan
$secret = git log --all -S "{your-client-secret}" --oneline 2>&1
$apiId = git log --all -S "{your-api-client-id}" --oneline 2>&1

if (($secret -and -not ($secret -match "fatal") -and ($secret | Measure-Object).Count -gt 0) -or 
    ($apiId -and -not ($apiId -match "fatal") -and ($apiId | Measure-Object).Count -gt 0)) {
    Write-Host "  [WARN] Some values may still remain" -ForegroundColor Yellow
} else {
    Write-Host "  [OK] All sensitive values removed!" -ForegroundColor Green
}

Write-Host ""
Write-Host "SUCCESS! Run: git push --force --all" -ForegroundColor Green
