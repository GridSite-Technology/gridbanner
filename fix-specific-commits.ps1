# Fix specific commits that contain sensitive values
# This script checks out each commit, fixes the files, and amends the commit

Write-Host "=== Fixing Specific Commits ===" -ForegroundColor Cyan
Write-Host ""

$commits = @("1282101", "c20a72d")

foreach ($commit in $commits) {
    Write-Host "Processing commit $commit..." -ForegroundColor Yellow
    
    # Get the commit hash
    $fullHash = git rev-parse $commit
    Write-Host "  Full hash: $fullHash" -ForegroundColor Gray
    
    # Checkout the commit (detached HEAD)
    git checkout $commit --quiet 2>&1 | Out-Null
    
    # Fix files
    $files = Get-ChildItem -Recurse -File -Include "*.md", "*.ps1", "*.json" | Where-Object { $_.FullName -notmatch "\.git" }
    
    foreach ($file in $files) {
        if (Test-Path $file.FullName) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content) {
                $original = $content
                $content = $content -replace '{your-client-secret}', '{your-client-secret}'
                $content = $content -replace '{your-api-client-id}', '{your-api-client-id}'
                $content = $content -replace '{your-desktop-client-id}', '{your-desktop-client-id}'
                $content = $content -replace '{your-tenant-id}', '{your-tenant-id}'
                
                if ($content -ne $original) {
                    Set-Content -Path $file.FullName -Value $content -NoNewline
                    Write-Host "  Fixed: $($file.Name)" -ForegroundColor Green
                }
            }
        }
    }
    
    # Stage changes
    git add -A --quiet 2>&1 | Out-Null
    
    # Check if there are changes
    $status = git status --porcelain
    if ($status) {
        # Amend the commit
        git commit --amend --no-edit --quiet 2>&1 | Out-Null
        Write-Host "  [OK] Commit amended" -ForegroundColor Green
    } else {
        Write-Host "  [SKIP] No changes needed" -ForegroundColor Gray
    }
}

# Return to main branch
git checkout main --quiet 2>&1 | Out-Null

Write-Host ""
Write-Host "Done! You may need to rebase or force push." -ForegroundColor Yellow
