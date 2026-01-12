# Script to remove sensitive Azure AD values from git history
# Run this from the repository root

Write-Host "This script will rewrite git history to remove sensitive Azure AD values." -ForegroundColor Yellow
Write-Host "Make sure you have a backup and have pushed/pulled all changes!" -ForegroundColor Yellow
Write-Host ""
$confirm = Read-Host "Continue? (yes/no)"
if ($confirm -ne "yes") {
    Write-Host "Aborted." -ForegroundColor Red
    exit 1
}

# Set environment variable to suppress filter-branch warning
$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# Values to replace
$replacements = @{
    "{your-api-client-id}" = "{your-api-client-id}"
    "{your-desktop-client-id}" = "{your-desktop-client-id}"
    "{your-tenant-id}" = "{your-tenant-id}"
    "{your-client-secret}" = "{your-client-secret}"
}

Write-Host "Creating filter script..." -ForegroundColor Cyan

# Create a temporary script file for the tree filter
$filterScript = @"
`$content = Get-Content `$args[0] -Raw -ErrorAction SilentlyContinue
if (`$content) {
    `$content = `$content -replace '{your-api-client-id}', '{your-api-client-id}'
    `$content = `$content -replace '{your-desktop-client-id}', '{your-desktop-client-id}'
    `$content = `$content -replace '{your-tenant-id}', '{your-tenant-id}'
    `$content = `$content -replace '{your-client-secret}', '{your-client-secret}'
    Set-Content -Path `$args[0] -Value `$content -NoNewline
}
"@

$filterScriptPath = Join-Path $env:TEMP "git-filter-script.ps1"
Set-Content -Path $filterScriptPath -Value $filterScript

Write-Host "Running git filter-branch (this may take a while)..." -ForegroundColor Cyan

# Run filter-branch for each file that might contain sensitive data
$files = @("AZURE_AD_TROUBLESHOOTING.md", "AZURE_AD_SETUP_GUIDE.md", "AZURE_AUTH_PROPOSAL.md", "alert-server/config.json")

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "Processing $file..." -ForegroundColor Yellow
        git filter-branch --force --tree-filter "if (Test-Path '$file') { powershell -File '$filterScriptPath' '$file' }" --prune-empty --tag-name-filter cat -- --all 2>&1 | Out-Null
    }
}

# Clean up
Remove-Item $filterScriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Git history has been cleaned!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Verify the changes: git log --all -S '{your-api-client-id}'" -ForegroundColor White
Write-Host "2. Force push to update remote: git push --force --all" -ForegroundColor White
Write-Host "3. Force push tags: git push --force --tags" -ForegroundColor White
Write-Host ""
Write-Host "WARNING: Force pushing will rewrite remote history. Make sure all collaborators are aware!" -ForegroundColor Red

