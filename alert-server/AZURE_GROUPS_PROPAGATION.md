# Azure AD Group Membership Propagation

## How Group Memberships Are Fetched

The alert-server **does not cache** group memberships. Every time you request a user's groups or query group members, the server makes a fresh API call to Microsoft Graph API.

## Propagation Delays

When you add or remove a user from an Azure AD group, the change may not appear immediately in API queries. This is due to Azure AD's internal propagation mechanisms.

### Typical Propagation Times

- **Direct group membership**: Usually 1-3 minutes
- **Nested group membership**: Can take 3-5 minutes
- **Complex nested groups**: May take up to 15 minutes
- **Large organizations**: Can take longer due to replication

### Why There's a Delay

Azure AD uses distributed systems with multiple data centers. When you make a change:
1. The change is written to the primary directory
2. The change replicates to other data centers
3. Microsoft Graph API queries may hit different data centers
4. Until replication completes, queries may return stale data

## What This Means

- **No caching on our side**: The server always queries Azure AD directly
- **Azure AD propagation**: Changes take time to appear in API responses
- **Transitive memberships**: Nested group changes take longer to propagate

## Troubleshooting

### User Not Showing in Group

If you just added a user to a group and they're not appearing:

1. **Wait 1-5 minutes** - Most changes propagate within this time
2. **Check server logs** - Look for the group query results:
   ```
   Found X groups for user jake@precisionx.tech: Group1, Group2
   ```
3. **Verify in Azure Portal** - Confirm the user is actually in the group
4. **Try refreshing** - The admin interface queries fresh each time

### Force Refresh

To force a fresh query:
1. Refresh the admin interface page
2. The server will make a new API call to Microsoft Graph
3. If Azure AD has propagated the change, it will appear

### Check Server Logs

The server logs show exactly what groups are returned:
```
Fetching groups for user: jake@precisionx.tech
Found user jake@precisionx.tech with ID: abc-123-def
Found 2 groups for user jake@precisionx.tech: Developers, Admins
```

If the group you just added isn't in the list, it's likely still propagating in Azure AD.

## Best Practices

1. **Wait before testing**: After adding a user to a group, wait 2-3 minutes before checking
2. **Check Azure Portal first**: Verify the membership is saved in Azure AD
3. **Use server logs**: Check what the API is actually returning
4. **Refresh admin interface**: The UI queries fresh each time you load the page

## Technical Details

The server uses:
- `transitiveMemberOf` - Gets all groups including nested memberships
- `transitiveMembers` - Gets all users including nested group memberships

These endpoints query Azure AD directly with no caching, so any delay is on Azure AD's side.

## References

- [Microsoft Graph API - Group Members](https://learn.microsoft.com/en-us/graph/api/group-list-members)
- [Azure AD Replication](https://learn.microsoft.com/en-us/azure/active-directory/hybrid/how-to-connect-sync-features)
