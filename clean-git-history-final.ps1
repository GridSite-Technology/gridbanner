# Final script to remove sensitive Azure AD values from git history
# Uses a shell script that works in Git Bash environment

param(
    [switch]$DryRun = $false
)

Write-Host "=== Git History Cleanup Script ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path ".git")) {
    Write-Host "ERROR: Not in a git repository!" -ForegroundColor Red
    exit 1
}

# ACTUAL sensitive values to replace
$replacements = @{
    "{your-api-client-id}" = "{your-api-client-id}"
    "{your-desktop-client-id}" = "{your-desktop-client-id}"
    "{your-tenant-id}" = "{your-tenant-id}"
    "{your-client-secret}" = "{your-client-secret}"
}

if ($DryRun) {
    Write-Host "DRY RUN MODE - Checking what would be changed..." -ForegroundColor Cyan
    foreach ($oldValue in $replacements.Keys) {
        $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
        if ($count -gt 0) {
            Write-Host "  Found sensitive value in $count commit(s)" -ForegroundColor Yellow
        }
    }
    exit 0
}

Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host "Proceeding automatically..." -ForegroundColor Yellow

$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

Write-Host ""
Write-Host "Creating shell script for Git Bash..." -ForegroundColor Cyan

# Create a shell script that works in Git Bash
$shellScript = @'
#!/bin/sh
file="$1"
if [ -f "$file" ]; then
    # Use sed for simple replacements (works in Git Bash)
    sed -i "s/{your-api-client-id}/{your-api-client-id}/g" "$file"
    sed -i "s/{your-desktop-client-id}/{your-desktop-client-id}/g" "$file"
    sed -i "s/{your-tenant-id}/{your-tenant-id}/g" "$file"
    sed -i "s/{your-client-secret}/{your-client-secret}/g" "$file"
fi
'@

$scriptPath = ".git-replace.sh"
Set-Content -Path $scriptPath -Value $shellScript -Encoding ASCII -NoNewline

Write-Host "Running git filter-branch..." -ForegroundColor Cyan

# Process all files in one filter-branch run
$files = @("AZURE_AD_TROUBLESHOOTING.md", "AZURE_AD_SETUP_GUIDE.md", "AZURE_AUTH_PROPOSAL.md", "alert-server/config.json", "REMOVE_SENSITIVE_DATA.md", "clean-git-history-simple.ps1", "clean-git-history.ps1", "clean-git-history-fixed.ps1", "clean-git-history-batch.ps1")

# Build combined tree-filter
$filterParts = @()
foreach ($file in $files) {
    if (Test-Path $file) {
        $filePath = $file.Replace('\', '/')
        $filterParts += ".git-replace.sh `"$filePath`""
    }
}

if ($filterParts.Count -gt 0) {
    $treeFilter = $filterParts -join " && "
    Write-Host "Processing $($filterParts.Count) files in one pass..." -ForegroundColor Yellow
    
    $result = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] All files processed" -ForegroundColor Green
    } else {
        Write-Host "  [FAILED]" -ForegroundColor Red
        # Check if it actually worked despite exit code
        $verify = git log --all -S "{your-client-secret-pattern}" --oneline 2>&1 | Measure-Object
        if ($verify.Count -eq 0) {
            Write-Host "  [SUCCESS] Verification shows values were removed!" -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ 2>&1 | Out-Null }
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

Remove-Item $scriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
foreach ($oldValue in $replacements.Keys) {
    $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
    if ($count -eq 0) {
        Write-Host "  [OK] Removed from history" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Still found in $count commit(s)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next: git push --force --all" -ForegroundColor Yellow
