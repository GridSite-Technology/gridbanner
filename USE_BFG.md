# Use BFG Repo-Cleaner to Fix Git History

Since `git filter-branch` is unreliable on Windows, **BFG Repo-Cleaner** is the recommended solution.

## Quick Steps

1. **Download BFG**: https://rtyley.github.io/bfg-repo-cleaner/
   - Download `bfg.jar` and place it in this directory

2. **Create replacements file**:
```powershell
@"
{your-client-secret}==>{your-client-secret}
{your-api-client-id}==>{your-api-client-id}
{your-desktop-client-id}==>{your-desktop-client-id}
{your-tenant-id}==>{your-tenant-id}
"@ | Out-File -FilePath .bfg-replacements.txt -Encoding ASCII
```

3. **Run BFG**:
```powershell
java -jar bfg.jar --replace-text .bfg-replacements.txt
```

4. **Clean up**:
```powershell
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

5. **Verify**:
```powershell
git log --all -S "0Fq8Q" --oneline
# Should return nothing
```

6. **Force push**:
```powershell
git push --force --all
git push --force --tags
```

## Alternative: Current State is Clean

The **current working files are already sanitized**. If GitHub Push Protection continues to block, you may need to:
- Contact GitHub support to allow the push
- Or use BFG as above to clean history
