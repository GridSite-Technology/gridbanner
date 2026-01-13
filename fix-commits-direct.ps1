# Directly fix commits by checking them out and amending
Write-Host "=== Direct Commit Fix ===" -ForegroundColor Cyan
Write-Host ""

# Get all commits that contain sensitive values
$commits = @()
$secretCommits = git log --all -S "{your-client-secret}" --oneline --format="%H" 2>&1
$clientIdCommits = git log --all -S "{your-api-client-id}" --oneline --format="%H" 2>&1

$allCommits = ($secretCommits + $clientIdCommits) | Where-Object { $_ -and $_ -notmatch "fatal" } | Sort-Object -Unique

Write-Host "Found $($allCommits.Count) commits to fix" -ForegroundColor Yellow
Write-Host ""

foreach ($commitHash in $allCommits) {
    Write-Host "Processing $($commitHash.Substring(0, 7))..." -ForegroundColor Cyan
    
    # Checkout the commit
    git checkout $commitHash --quiet 2>&1 | Out-Null
    
    # Find and fix all files
    $files = Get-ChildItem -Recurse -File -Include "*.md", "*.ps1", "*.json", "*.sh", "*.bat" | Where-Object { $_.FullName -notmatch "\.git" }
    $fixed = $false
    
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
                    $fixed = $true
                }
            }
        }
    }
    
    if ($fixed) {
        git add -A --quiet 2>&1 | Out-Null
        git commit --amend --no-edit --quiet 2>&1 | Out-Null
        Write-Host "  [FIXED]" -ForegroundColor Green
    } else {
        Write-Host "  [SKIP - no changes]" -ForegroundColor Gray
    }
}

# Return to main
git checkout main --quiet 2>&1 | Out-Null

Write-Host ""
Write-Host "Verifying..." -ForegroundColor Cyan
$remaining = git log --all -S "{your-client-secret}" --oneline 2>&1
if ($remaining -and -not ($remaining -match "fatal")) {
    Write-Host "  [WARNING] Some commits still contain secrets" -ForegroundColor Yellow
    Write-Host $remaining
} else {
    Write-Host "  [OK] All secrets removed!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done! You may need to rebase or force push." -ForegroundColor Yellow
