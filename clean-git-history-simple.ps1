# Simple script to remove sensitive Azure AD values from git history
# Run this from the repository root directory

param(
    [switch]$DryRun = $false
)

Write-Host "=== Git History Cleanup Script ===" -ForegroundColor Cyan
Write-Host ""

# Check if we're in a git repo
if (-not (Test-Path ".git")) {
    Write-Host "ERROR: Not in a git repository!" -ForegroundColor Red
    Write-Host "Please run this script from the repository root." -ForegroundColor Yellow
    exit 1
}

# Sensitive values to replace (UPDATE THESE WITH YOUR ACTUAL VALUES)
$replacements = @{
    "{your-api-client-id}" = "{your-api-client-id}"
    "{your-desktop-client-id}" = "{your-desktop-client-id}"
    "{your-tenant-id}" = "{your-tenant-id}"
    "{your-client-secret}" = "{your-client-secret}"
}

Write-Host "This script will:" -ForegroundColor Yellow
Write-Host "  1. Replace sensitive values in git history" -ForegroundColor White
Write-Host "  2. Clean up git references" -ForegroundColor White
Write-Host "  3. Force garbage collection" -ForegroundColor White
Write-Host ""
Write-Host "⚠️  WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host "⚠️  Make sure you have a backup and have pushed/pulled all changes!" -ForegroundColor Red
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN MODE - Checking what would be changed..." -ForegroundColor Cyan
    foreach ($oldValue in $replacements.Keys) {
        $count = (git log --all -S $oldValue --oneline | Measure-Object).Count
        if ($count -gt 0) {
            Write-Host "  Found '$oldValue' in $count commit(s)" -ForegroundColor Yellow
        }
    }
    Write-Host ""
    Write-Host "Run without -DryRun to perform the actual cleanup." -ForegroundColor Cyan
    exit 0
}

$confirm = Read-Host "Continue with cleanup? (type 'yes' to continue)"
if ($confirm -ne "yes") {
    Write-Host "Aborted." -ForegroundColor Red
    exit 1
}

# Set environment variable to suppress filter-branch warning
$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

Write-Host ""
Write-Host "Step 1: Creating replacement script..." -ForegroundColor Cyan

# Create a PowerShell script that will do the replacements
$filterScript = @'
param($filePath)
if (Test-Path $filePath) {
    $content = Get-Content $filePath -Raw -ErrorAction SilentlyContinue
    if ($content) {
        $changed = $false
'@

foreach ($oldValue in $replacements.Keys) {
    $newValue = $replacements[$oldValue]
    $filterScript += @"
        if (`$content -match [regex]::Escape('$oldValue')) {
            `$content = `$content -replace [regex]::Escape('$oldValue'), '$newValue'
            `$changed = `$true
        }
"@
}

$filterScript += @'
        if ($changed) {
            Set-Content -Path $filePath -Value $content -NoNewline
        }
    }
}
'@

$filterScriptPath = Join-Path $env:TEMP "git-filter-replace.ps1"
Set-Content -Path $filterScriptPath -Value $filterScript

Write-Host "Step 2: Running git filter-branch (this may take several minutes)..." -ForegroundColor Cyan

# Files that might contain sensitive data
$filesToCheck = @(
    "AZURE_AD_TROUBLESHOOTING.md",
    "AZURE_AD_SETUP_GUIDE.md", 
    "AZURE_AUTH_PROPOSAL.md",
    "alert-server/config.json"
)

$filesFound = @()
foreach ($file in $filesToCheck) {
    if (Test-Path $file) {
        $filesFound += $file
    }
}

if ($filesFound.Count -eq 0) {
    Write-Host "No files found to process." -ForegroundColor Yellow
    Remove-Item $filterScriptPath -ErrorAction SilentlyContinue
    exit 0
}

# Run filter-branch for all files at once
$treeFilter = "foreach (`$f in @('$($filesFound -join "','")')) { if (Test-Path `$f) { powershell -File '$filterScriptPath' `$f } }"

Write-Host "Processing files: $($filesFound -join ', ')" -ForegroundColor Yellow
git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1 | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: git filter-branch failed!" -ForegroundColor Red
    Remove-Item $filterScriptPath -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "Step 3: Cleaning up git references..." -ForegroundColor Cyan

# Remove backup refs
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { 
    git update-ref -d $_ 2>&1 | Out-Null
}

Write-Host "Step 4: Running garbage collection..." -ForegroundColor Cyan

# Force garbage collection
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

# Clean up
Remove-Item $filterScriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "✅ Git history has been cleaned!" -ForegroundColor Green
Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
foreach ($oldValue in $replacements.Keys) {
    $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
    if ($count -eq 0) {
        Write-Host "  ✓ '$oldValue' removed from history" -ForegroundColor Green
    } else {
        Write-Host "  ✗ '$oldValue' still found in $count commit(s)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify: git log --all -S 'YOUR_SENSITIVE_VALUE' --oneline" -ForegroundColor White
Write-Host "  2. Force push: git push --force --all" -ForegroundColor White
Write-Host "  3. Force push tags: git push --force --tags" -ForegroundColor White
Write-Host ""
Write-Host "⚠️  WARNING: Force pushing will rewrite remote history!" -ForegroundColor Red
Write-Host "⚠️  Make sure all collaborators are aware and have pulled the cleaned history!" -ForegroundColor Red

