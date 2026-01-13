# Fix commits containing sensitive values by amending them

Write-Host "Fixing commits with sensitive values..." -ForegroundColor Cyan

# Commits to fix
$commits = @("e4f4978", "171d5cb", "e6b68d7")

foreach ($commit in $commits) {
    Write-Host "Checking commit $commit..." -ForegroundColor Yellow
    
    # Checkout the commit
    git checkout $commit --quiet
    
    # Fix files
    # NOTE: Update the sensitive values below with your actual values before running this script
    if (Test-Path "CLEANUP_INSTRUCTIONS.md") {
        $content = Get-Content "CLEANUP_INSTRUCTIONS.md" -Raw
        $content = $content -replace '{your-actual-client-secret}', '{your-client-secret}'
        $content = $content -replace '{your-actual-api-client-id}', '{your-api-client-id}'
        $content = $content -replace '{your-actual-desktop-client-id}', '{your-desktop-client-id}'
        $content = $content -replace '{your-actual-tenant-id}', '{your-tenant-id}'
        Set-Content "CLEANUP_INSTRUCTIONS.md" -Value $content -NoNewline
    }
    
    if (Test-Path "clean-git-history-final.ps1") {
        $content = Get-Content "clean-git-history-final.ps1" -Raw
        $content = $content -replace '{your-actual-client-secret}', '{your-client-secret}'
        $content = $content -replace '{your-actual-api-client-id}', '{your-api-client-id}'
        $content = $content -replace '{your-actual-desktop-client-id}', '{your-desktop-client-id}'
        $content = $content -replace '{your-actual-tenant-id}', '{your-tenant-id}'
        Set-Content "clean-git-history-final.ps1" -Value $content -NoNewline
    }
    
    if (Test-Path "clean-git-history-simple-final.ps1") {
        $content = Get-Content "clean-git-history-simple-final.ps1" -Raw
        $content = $content -replace '{your-actual-client-secret}', '{your-client-secret}'
        $content = $content -replace '{your-actual-api-client-id}', '{your-api-client-id}'
        $content = $content -replace '{your-actual-desktop-client-id}', '{your-desktop-client-id}'
        $content = $content -replace '{your-actual-tenant-id}', '{your-tenant-id}'
        Set-Content "clean-git-history-simple-final.ps1" -Value $content -NoNewline
    }
    
    # Amend the commit
    git add -A
    git commit --amend --no-edit --quiet
    
    Write-Host "  Fixed!" -ForegroundColor Green
}

# Return to main branch
git checkout main --quiet

Write-Host "Done! Now force push: git push --force" -ForegroundColor Yellow

