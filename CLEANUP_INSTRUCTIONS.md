# Git History Cleanup Instructions

The sensitive Azure AD credentials need to be removed from git history. We've tried several approaches with `git filter-branch`, but it's unreliable on Windows.

## Recommended: Use BFG Repo-Cleaner

BFG is the fastest and most reliable tool for this task.

### Step 1: Download BFG
1. Download `bfg.jar` from: https://rtyley.github.io/bfg-repo-cleaner/
2. Place `bfg.jar` in the repository root directory

### Step 2: Run the cleanup script
```powershell
.\clean-git-history-bfg.ps1
```

This will:
- Replace all sensitive values with placeholders
- Clean up git references
- Verify the cleanup

### Step 3: Force push
```powershell
git push --force --all
git push --force --tags
```

## Alternative: Manual BFG Commands

If the script doesn't work, run these commands manually:

```powershell
# Create replacements file
@"
{your-api-client-id}==>{your-api-client-id}
{your-desktop-client-id}==>{your-desktop-client-id}
{your-tenant-id}==>{your-tenant-id}
{your-client-secret}==>{your-client-secret}
"@ | Out-File -FilePath .bfg-replacements.txt -Encoding ASCII

# Run BFG
java -jar bfg.jar --replace-text .bfg-replacements.txt

# Clean up
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# Verify
git log --all -S "{your-client-secret-pattern}" --oneline
git log --all -S "{your-api-client-id-pattern}" --oneline

# Force push
git push --force --all
git push --force --tags
```

## Sensitive Values to Remove

- API Client ID: `{your-api-client-id}`
- Desktop Client ID: `{your-desktop-client-id}`
- Tenant ID: `{your-tenant-id}`
- Client Secret: `{your-client-secret}` ⚠️ **CRITICAL**

## Files That May Contain Sensitive Data

- `AZURE_AD_TROUBLESHOOTING.md`
- `AZURE_AD_SETUP_GUIDE.md`
- `AZURE_AUTH_PROPOSAL.md`
- `alert-server/config.json`
- `REMOVE_SENSITIVE_DATA.md`
- `clean-git-history-*.ps1` scripts

