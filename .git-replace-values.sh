#!/bin/sh
# Replace sensitive values in files

for file in CLEANUP_INSTRUCTIONS.md REMOVE_SENSITIVE_DATA.md clean-git-history-final.ps1 clean-git-history-simple.ps1 clean-git-history.ps1 clean-git-history-fixed.ps1 clean-git-history-batch.ps1 clean-git-history-simple-final.ps1 fix-commits.ps1 AZURE_AD_TROUBLESHOOTING.md AZURE_AD_SETUP_GUIDE.md AZURE_AUTH_PROPOSAL.md alert-server/config.json; do
    if [ -f "$file" ]; then
        sed -i "s/{your-client-secret}/{your-client-secret}/g" "$file"
        sed -i "s/{your-api-client-id}/{your-api-client-id}/g" "$file"
        sed -i "s/{your-desktop-client-id}/{your-desktop-client-id}/g" "$file"
        sed -i "s/{your-tenant-id}/{your-tenant-id}/g" "$file"
        sed -i "s/0Fq8Q/{your-client-secret-pattern}/g" "$file"
        sed -i "s/70cd6b3f/{your-api-client-id-pattern}/g" "$file"
    fi
done
