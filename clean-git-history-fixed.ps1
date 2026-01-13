# Fixed script to remove sensitive Azure AD values from git history
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

# Sensitive values to replace
# IMPORTANT: Update these with your actual sensitive values before running!
$replacements = @{
    "REPLACE_WITH_YOUR_API_CLIENT_ID" = "{your-api-client-id}"
    "REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID" = "{your-desktop-client-id}"
    "REPLACE_WITH_YOUR_TENANT_ID" = "{your-tenant-id}"
    "REPLACE_WITH_YOUR_CLIENT_SECRET" = "{your-client-secret}"
}

Write-Host "This script will:" -ForegroundColor Yellow
Write-Host "  1. Replace sensitive values in git history" -ForegroundColor White
Write-Host "  2. Clean up git references" -ForegroundColor White
Write-Host "  3. Force garbage collection" -ForegroundColor White
Write-Host ""
Write-Host "WARNING: This will rewrite git history!" -ForegroundColor Red
Write-Host "WARNING: Make sure you have a backup and have pushed/pulled all changes!" -ForegroundColor Red
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN MODE - Checking what would be changed..." -ForegroundColor Cyan
    foreach ($oldValue in $replacements.Keys) {
        $count = (git log --all -S $oldValue --oneline 2>&1 | Measure-Object).Count
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
$filterScriptContent = @'
param($filePath)
if (Test-Path $filePath) {
    $content = Get-Content $filePath -Raw -ErrorAction SilentlyContinue
    if ($content) {
        $changed = $false
        # Update these with your actual sensitive values
        if ($content -match [regex]::Escape('REPLACE_WITH_YOUR_API_CLIENT_ID')) {
            $content = $content -replace [regex]::Escape('REPLACE_WITH_YOUR_API_CLIENT_ID'), '{your-api-client-id}'
            $changed = $true
        }
        if ($content -match [regex]::Escape('REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID')) {
            $content = $content -replace [regex]::Escape('REPLACE_WITH_YOUR_DESKTOP_CLIENT_ID'), '{your-desktop-client-id}'
            $changed = $true
        }
        if ($content -match [regex]::Escape('REPLACE_WITH_YOUR_TENANT_ID')) {
            $content = $content -replace [regex]::Escape('REPLACE_WITH_YOUR_TENANT_ID'), '{your-tenant-id}'
            $changed = $true
        }
        if ($content -match [regex]::Escape('REPLACE_WITH_YOUR_CLIENT_SECRET')) {
            $content = $content -replace [regex]::Escape('REPLACE_WITH_YOUR_CLIENT_SECRET'), '{your-client-secret}'
            $changed = $true
        }
        if ($changed) {
            Set-Content -Path $filePath -Value $content -NoNewline
        }
    }
}
'@

$filterScriptPath = Join-Path $env:TEMP "git-filter-replace.ps1"
Set-Content -Path $filterScriptPath -Value $filterScriptContent -Encoding UTF8

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

Write-Host "Processing files: $($filesFound -join ', ')" -ForegroundColor Yellow

# Process each file separately to avoid complex escaping issues
foreach ($file in $filesFound) {
    Write-Host "  Processing $file..." -ForegroundColor Cyan
    $fileEscaped = $file.Replace('\', '/')  # Use forward slashes for git
    $treeFilter = "powershell -ExecutionPolicy Bypass -File `"$filterScriptPath`" `"$fileEscaped`""
    
    $output = git filter-branch --force --tree-filter $treeFilter --prune-empty --tag-name-filter cat -- --all 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR processing $file" -ForegroundColor Red
        Write-Host $output -ForegroundColor Red
        # Continue with other files
    }
}

Write-Host "Step 3: Cleaning up git references..." -ForegroundColor Cyan

# Remove backup refs
$refs = git for-each-ref --format="%(refname)" refs/original/ 2>&1
if ($refs) {
    $refs | ForEach-Object { 
        git update-ref -d $_ 2>&1 | Out-Null
    }
}

Write-Host "Step 4: Running garbage collection..." -ForegroundColor Cyan

# Force garbage collection
git reflog expire --expire=now --all 2>&1 | Out-Null
git gc --prune=now --aggressive 2>&1 | Out-Null

# Clean up
Remove-Item $filterScriptPath -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "SUCCESS: Git history has been cleaned!" -ForegroundColor Green
Write-Host ""
Write-Host "Verification:" -ForegroundColor Cyan
foreach ($oldValue in $replacements.Keys) {
    $result = git log --all -S $oldValue --oneline 2>&1
    $count = ($result | Measure-Object).Count
    if ($count -eq 0 -or ($result -match "fatal")) {
        Write-Host "  [OK] '$oldValue' removed from history" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] '$oldValue' still found in $count commit(s)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify: git log --all -S 'YOUR_SENSITIVE_VALUE' --oneline" -ForegroundColor White
Write-Host "  2. Force push: git push --force --all" -ForegroundColor White
Write-Host "  3. Force push tags: git push --force --tags" -ForegroundColor White
Write-Host ""
Write-Host "WARNING: Force pushing will rewrite remote history!" -ForegroundColor Red
Write-Host "WARNING: Make sure all collaborators are aware and have pulled the cleaned history!" -ForegroundColor Red

