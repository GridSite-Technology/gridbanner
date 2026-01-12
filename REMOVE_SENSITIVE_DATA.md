# Removing Sensitive Azure AD Data from Git History

This guide will help you remove sensitive Azure AD credentials from your git repository history.

## Sensitive Values to Remove

Replace these with placeholders in git history:
- **API Client ID**: `{your-api-client-id}` (replace real value)
- **Desktop Client ID**: `{your-desktop-client-id}` (replace real value)
- **Tenant ID**: `{your-tenant-id}` (replace real value)
- **Client Secret**: `{your-client-secret}` ⚠️ **CRITICAL - MUST BE REMOVED**

## Files Already Sanitized

The following files have been updated with placeholders:
- `AZURE_AD_TROUBLESHOOTING.md` - All real IDs replaced with placeholders
- `.gitignore` - Added `alert-server/config.json` to prevent future commits

## Method 1: Using git filter-repo (Recommended)

### Install git-filter-repo

```powershell
# Install via pip (requires Python)
pip install git-filter-repo

# Or download from: https://github.com/newren/git-filter-repo
```

### Run the cleanup

```powershell
# Create replacements file
@"
REPLACE_YOUR_API_CLIENT_ID_HERE==>{your-api-client-id}
REPLACE_YOUR_DESKTOP_CLIENT_ID_HERE==>{your-desktop-client-id}
REPLACE_YOUR_TENANT_ID_HERE==>{your-tenant-id}
REPLACE_YOUR_CLIENT_SECRET_HERE==>{your-client-secret}
"@ | Out-File -FilePath replacements.txt -Encoding utf8

# Replace sensitive values in all files
git filter-repo --replace-text replacements.txt
```

**Note**: On Windows PowerShell, you may need to use a different syntax. See Method 2.

## Method 2: Using git filter-branch (Windows Compatible)

### Step 1: Commit current sanitized changes

```powershell
git add -A
git commit -m "Sanitize Azure AD credentials from documentation"
```

### Step 2: Create replacement script

Create a file `replace-sensitive.ps1`:

```powershell
$file = $args[0]
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    # Replace with your actual sensitive values
    $content = $content -replace 'YOUR_API_CLIENT_ID', '{your-api-client-id}'
    $content = $content -replace 'YOUR_DESKTOP_CLIENT_ID', '{your-desktop-client-id}'
    $content = $content -replace 'YOUR_TENANT_ID', '{your-tenant-id}'
    $content = $content -replace 'YOUR_CLIENT_SECRET', '{your-client-secret}'
    Set-Content -Path $file -Value $content -NoNewline
}
```

### Step 3: Run filter-branch

```powershell
$env:FILTER_BRANCH_SQUELCH_WARNING = "1"

# For each file that might contain sensitive data
git filter-branch --force --tree-filter "powershell -File replace-sensitive.ps1 AZURE_AD_TROUBLESHOOTING.md" --prune-empty --tag-name-filter cat -- --all
git filter-branch --force --tree-filter "powershell -File replace-sensitive.ps1 AZURE_AD_SETUP_GUIDE.md" --prune-empty --tag-name-filter cat -- --all
git filter-branch --force --tree-filter "powershell -File replace-sensitive.ps1 AZURE_AUTH_PROPOSAL.md" --prune-empty --tag-name-filter cat -- --all
git filter-branch --force --tree-filter "powershell -File replace-sensitive.ps1 alert-server/config.json" --prune-empty --tag-name-filter cat -- --all
```

### Step 4: Clean up

```powershell
# Remove backup refs
git for-each-ref --format="%(refname)" refs/original/ | ForEach-Object { git update-ref -d $_ }

# Force garbage collection
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

## Method 3: Using BFG Repo-Cleaner (Easiest)

### Download BFG

Download from: https://rtyley.github.io/bfg-repo-cleaner/

### Create replacements file

Create `replacements.txt`:

```
{your-api-client-id}==>{your-api-client-id}
{your-desktop-client-id}==>{your-desktop-client-id}
{your-tenant-id}==>{your-tenant-id}
{your-client-secret}==>{your-client-secret}
```

### Run BFG

```powershell
java -jar bfg.jar --replace-text replacements.txt
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

## After Cleaning History

### 1. Verify the cleanup

```powershell
# Check that sensitive values are gone
git log --all -S "{your-api-client-id}" --oneline
git log --all -S "{your-client-secret-pattern}" --oneline

# Should return no results
```

### 2. Force push to remote

⚠️ **WARNING**: This will rewrite remote history. Coordinate with your team!

```powershell
git push --force --all
git push --force --tags
```

### 3. Notify collaborators

All collaborators will need to:
```powershell
git fetch origin
git reset --hard origin/main  # or origin/master
```

## Prevention

1. ✅ `alert-server/config.json` is now in `.gitignore`
2. ✅ Documentation files use placeholders
3. ⚠️ **Never commit** files containing:
   - Client secrets
   - API keys
   - Passwords
   - Real tenant/client IDs in documentation

## Quick Check Script

Run this to verify no sensitive data is in your current working directory:

```powershell
$sensitive = @(
    "{your-api-client-id}",
    "{your-desktop-client-id}",
    "{your-tenant-id}",
    "{your-client-secret}"
)

Get-ChildItem -Recurse -File | Where-Object { $_.Extension -match '\.(md|json|ini|cs|js)$' } | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    foreach ($value in $sensitive) {
        if ($content -match [regex]::Escape($value)) {
            Write-Host "FOUND in: $($_.FullName)" -ForegroundColor Red
        }
    }
}
```

