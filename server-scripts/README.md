# Server Scripts

Scripts for server-side automation and management of GridBanner authorized_keys.

## update-authorized-keys.py

A self-installing Python script that automatically fetches and updates `authorized_keys` files from the GridBanner alert server.

### Features

- Fetches authorized_keys from alert server by Azure AD group name
- Updates authorized_keys for one or more users
- Self-installing cron job (runs every minute)
- Automatic backup of existing authorized_keys files
- Proper file permissions and ownership handling
- Comprehensive logging

### Requirements

- Python 3.6 or higher
- `cron` (for automatic updates)
- Root/sudo access (for installing cron and updating user files)
- Network access to GridBanner alert server

### Installation

**For root user (default):**
```bash
sudo python3 update-authorized-keys.py --install \
    --key your-admin-key \
    --group Developers
```

Note: If no `--server` is specified, it defaults to `https://umbonic-roseanna-cuppy.ngrok-free.dev`

**For specific users:**
```bash
sudo python3 update-authorized-keys.py --install \
    --server http://your-server:3000 \
    --key your-admin-key \
    --group Developers \
    --users deploy,admin,backup
```

### Usage

#### Run Once (Without Installing)

Update keys immediately without installing cron:
```bash
sudo python3 update-authorized-keys.py \
    --server http://your-server:3000 \
    --key your-admin-key \
    --group Developers
```

#### Install as Cron Job

Install the script to run automatically every minute:
```bash
sudo python3 update-authorized-keys.py --install \
    --server http://your-server:3000 \
    --key your-admin-key \
    --group Developers \
    --users deploy,admin
```

#### Uninstall Cron Job

Remove the automatic cron job:
```bash
sudo python3 update-authorized-keys.py --uninstall
```

### Command Line Arguments

- `--server`: Alert server URL (default: `https://umbonic-roseanna-cuppy.ngrok-free.dev`)
- `--key`: Admin API key (required)
- `--group`: Azure AD group name (default: `Developers`)
- `--users`: Comma-separated list of users to update (default: `root`)
- `--install`: Install as cron job (runs every minute)
- `--uninstall`: Remove cron job

### Examples

**Update root user's keys:**
```bash
sudo python3 update-authorized-keys.py \
    --server http://alert-server.example.com:3000 \
    --key my-secret-key \
    --group DevOps
```

**Update multiple users:**
```bash
sudo python3 update-authorized-keys.py \
    --server http://alert-server.example.com:3000 \
    --key my-secret-key \
    --group Developers \
    --users deploy,admin,backup
```

**Install for production:**
```bash
sudo python3 update-authorized-keys.py --install \
    --server https://alert-server.example.com \
    --key $(cat /etc/gridbanner/admin-key) \
    --group Production-Engineers \
    --users deploy,admin
```

### How It Works

1. **Fetches Keys**: Makes HTTP request to `/api/authorized-keys/group/:groupName` endpoint
2. **Backs Up**: Creates `.backup` file of existing authorized_keys
3. **Updates**: Writes new keys to `~/.ssh/authorized_keys` for each user
4. **Sets Permissions**: Ensures correct file permissions (600) and directory permissions (700)
5. **Sets Ownership**: Sets file ownership to the target user (when running as root)

### Logging

The script logs to:
- **File**: `/var/log/gridbanner-keys-update.log`
- **Console**: stdout/stderr

Log entries include:
- Timestamp
- Fetch status
- User updates
- Errors and warnings

### Security Considerations

- **Admin Key**: Store the admin key securely (e.g., in `/etc/gridbanner/admin-key` with 600 permissions)
- **Network**: Use HTTPS in production environments
- **Permissions**: Script must run as root to update user files
- **Backup**: Always creates backup before overwriting authorized_keys

### Troubleshooting

**"Permission denied" error:**
- Ensure script is run with `sudo` or as root
- Check that target users exist

**"Group not found" error:**
- Verify group name matches exactly (case-sensitive)
- Check that alert server has proper Azure AD permissions

**"Network error":**
- Verify server URL is correct and accessible
- Check firewall rules
- Test with `curl` or `wget` manually

**Cron not running:**
- Check cron service is running: `systemctl status cron`
- View cron logs: `grep CRON /var/log/syslog`
- Verify cron entry: `crontab -l`

### Manual Testing

Test the endpoint manually:
```bash
curl -H "X-API-Key: your-admin-key" \
    "http://your-server:3000/api/authorized-keys/group/Developers"
```

### Integration with Configuration Management

You can integrate this script with Ansible, Puppet, or other configuration management tools:

**Ansible Example:**
```yaml
- name: Install GridBanner key updater
  cron:
    name: "GridBanner authorized_keys updater"
    minute: "*/1"
    job: "/usr/bin/python3 /opt/gridbanner/update-authorized-keys.py --server {{ alert_server_url }} --key {{ admin_key }} --group {{ group_name }} --users {{ users | join(',') }}"
```

### Files Modified

- `~/.ssh/authorized_keys` - Updated with keys from server
- `~/.ssh/authorized_keys.backup` - Backup of previous version
- `/var/log/gridbanner-keys-update.log` - Log file

### Cron Schedule

The script installs with schedule: `*/1 * * * *` (every minute)

To change the schedule, edit the crontab manually:
```bash
sudo crontab -e
```

### See Also

- [Alert Server README](../alert-server/README.md) - Alert server documentation
- [Azure AD Setup Guide](../AZURE_AD_SETUP_GUIDE.md) - Azure AD configuration
