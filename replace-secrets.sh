#!/bin/sh
find . -type f \( -name "*.md" -o -name "*.ps1" -o -name "*.json" \) ! -path "./.git/*" -print0 | while IFS= read -r -d '' file; do
    if [ -f "$file" ]; then
        sed -i "s/{your-client-secret}/{your-client-secret}/g" "$file"
        sed -i "s/{your-api-client-id}/{your-api-client-id}/g" "$file"
        sed -i "s/{your-desktop-client-id}/{your-desktop-client-id}/g" "$file"
        sed -i "s/{your-tenant-id}/{your-tenant-id}/g" "$file"
    fi
done
