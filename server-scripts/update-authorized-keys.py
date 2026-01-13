#!/usr/bin/env python3
"""
GridBanner Authorized Keys Updater

This script fetches authorized_keys from the GridBanner alert server and updates
the authorized_keys file for specified users. It can install itself as a cron job
to run automatically every minute.

Usage:
    # Update root user's authorized_keys
    python3 update-authorized-keys.py --install --server http://server:3000 --key adminkey --group Developers

    # Update specific users
    python3 update-authorized-keys.py --install --server http://server:3000 --key adminkey --group Developers --users deploy,admin

    # Run once (without installing)
    python3 update-authorized-keys.py --server http://server:3000 --key adminkey --group Developers

    # Uninstall cron job
    python3 update-authorized-keys.py --uninstall
"""

import argparse
import os
import sys
import subprocess
import urllib.request
import urllib.error
import logging
from pathlib import Path

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('/var/log/gridbanner-keys-update.log'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

# Default configuration
DEFAULT_SERVER = 'https://umbonic-roseanna-cuppy.ngrok-free.dev'
DEFAULT_GROUP = 'Developers'
CRON_COMMENT = '# GridBanner authorized_keys updater'


def get_script_path():
    """Get the absolute path to this script."""
    return os.path.abspath(__file__)


def fetch_authorized_keys(server_url, admin_key, group_name):
    """
    Fetch authorized_keys from the alert server.
    
    Args:
        server_url: Base URL of the alert server
        admin_key: Admin API key
        group_name: Name of the Azure AD group
        
    Returns:
        str: Content of authorized_keys file, or None on error
    """
    try:
        # Construct URL
        url = f"{server_url.rstrip('/')}/api/authorized-keys/group/{group_name}?key={admin_key}"
        
        logger.info(f"Fetching authorized_keys from {url}")
        
        # Make request
        req = urllib.request.Request(url)
        req.add_header('X-API-Key', admin_key)
        
        with urllib.request.urlopen(req, timeout=30) as response:
            if response.status == 200:
                content = response.read().decode('utf-8')
                logger.info(f"Successfully fetched {len(content)} bytes")
                return content
            else:
                logger.error(f"Server returned status {response.status}")
                return None
                
    except urllib.error.HTTPError as e:
        if e.code == 404:
            logger.error(f"Group '{group_name}' not found on server")
        elif e.code == 401:
            logger.error("Authentication failed - check admin key")
        else:
            logger.error(f"HTTP error {e.code}: {e.reason}")
        return None
    except urllib.error.URLError as e:
        logger.error(f"Network error: {e.reason}")
        return None
    except Exception as e:
        logger.error(f"Unexpected error: {e}")
        return None


def update_authorized_keys(user, keys_content):
    """
    Update authorized_keys file for a user.
    
    Args:
        user: Username (or 'root')
        keys_content: Content to write to authorized_keys
        
    Returns:
        bool: True if successful, False otherwise
    """
    try:
        # Determine home directory
        if user == 'root':
            home_dir = '/root'
        else:
            # Try to get home directory from /etc/passwd
            try:
                import pwd
                home_dir = pwd.getpwnam(user).pw_dir
            except KeyError:
                logger.error(f"User '{user}' not found")
                return False
            except ImportError:
                # Fallback: try /home/username
                home_dir = f"/home/{user}"
                if not os.path.exists(home_dir):
                    logger.error(f"Home directory for user '{user}' not found")
                    return False
        
        # Create .ssh directory if it doesn't exist
        ssh_dir = os.path.join(home_dir, '.ssh')
        os.makedirs(ssh_dir, mode=0o700, exist_ok=True)
        
        # Write authorized_keys file
        keys_file = os.path.join(ssh_dir, 'authorized_keys')
        backup_file = keys_file + '.backup'
        
        # Create backup if file exists
        if os.path.exists(keys_file):
            import shutil
            shutil.copy2(keys_file, backup_file)
            logger.info(f"Created backup: {backup_file}")
        
        # Write new keys
        with open(keys_file, 'w') as f:
            f.write(keys_content)
        
        # Set correct permissions
        os.chmod(keys_file, 0o600)
        os.chmod(ssh_dir, 0o700)
        
        # Set ownership if running as root
        if os.geteuid() == 0:
            try:
                import pwd
                uid = pwd.getpwnam(user).pw_uid
                gid = pwd.getpwnam(user).pw_gid
                os.chown(keys_file, uid, gid)
                os.chown(ssh_dir, uid, gid)
            except (ImportError, KeyError):
                pass  # Skip ownership change if we can't determine it
        
        logger.info(f"Updated authorized_keys for user '{user}' ({keys_file})")
        return True
        
    except PermissionError:
        logger.error(f"Permission denied updating keys for user '{user}' (run as root)")
        return False
    except Exception as e:
        logger.error(f"Error updating keys for user '{user}': {e}")
        return False


def install_cron(server_url, admin_key, group_name, users):
    """
    Install this script as a cron job.
    
    Args:
        server_url: Server URL
        admin_key: Admin key
        group_name: Group name
        users: List of users
    """
    if os.geteuid() != 0:
        logger.error("Must run as root to install cron job")
        sys.exit(1)
    
    script_path = get_script_path()
    python_path = sys.executable
    
    # Build command
    users_arg = f" --users {','.join(users)}" if users else ""
    cmd = f"{python_path} {script_path} --server {server_url} --key {admin_key} --group {group_name}{users_arg}"
    
    # Create cron entry
    cron_entry = f"*/1 * * * * {cmd} >> /var/log/gridbanner-keys-update.log 2>&1"
    
    try:
        # Get current crontab
        result = subprocess.run(['crontab', '-l'], capture_output=True, text=True)
        current_crontab = result.stdout if result.returncode == 0 else ""
        
        # Remove existing GridBanner entries
        lines = current_crontab.split('\n')
        filtered_lines = [line for line in lines if CRON_COMMENT not in line and 'update-authorized-keys.py' not in line]
        
        # Add new entry
        new_crontab = '\n'.join(filtered_lines).strip()
        if new_crontab:
            new_crontab += '\n'
        new_crontab += f"{CRON_COMMENT}\n"
        new_crontab += cron_entry + '\n'
        
        # Write new crontab
        process = subprocess.Popen(['crontab', '-'], stdin=subprocess.PIPE, text=True)
        process.communicate(input=new_crontab)
        
        if process.returncode == 0:
            logger.info("Cron job installed successfully")
            logger.info(f"Cron entry: {cron_entry}")
        else:
            logger.error("Failed to install cron job")
            sys.exit(1)
            
    except FileNotFoundError:
        logger.error("crontab command not found. Is cron installed?")
        sys.exit(1)
    except Exception as e:
        logger.error(f"Error installing cron job: {e}")
        sys.exit(1)


def uninstall_cron():
    """Remove GridBanner cron entries."""
    if os.geteuid() != 0:
        logger.error("Must run as root to uninstall cron job")
        sys.exit(1)
    
    try:
        # Get current crontab
        result = subprocess.run(['crontab', '-l'], capture_output=True, text=True)
        if result.returncode != 0:
            logger.info("No crontab found")
            return
        
        current_crontab = result.stdout
        
        # Remove GridBanner entries
        lines = current_crontab.split('\n')
        filtered_lines = [line for line in lines if CRON_COMMENT not in line and 'update-authorized-keys.py' not in line]
        
        new_crontab = '\n'.join(filtered_lines).strip()
        
        if new_crontab:
            # Write updated crontab
            process = subprocess.Popen(['crontab', '-'], stdin=subprocess.PIPE, text=True)
            process.communicate(input=new_crontab + '\n')
            logger.info("Cron job uninstalled successfully")
        else:
            # Remove empty crontab
            subprocess.run(['crontab', '-r'])
            logger.info("Cron job uninstalled (crontab is now empty)")
            
    except Exception as e:
        logger.error(f"Error uninstalling cron job: {e}")
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(
        description='Update authorized_keys from GridBanner alert server',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__
    )
    
    parser.add_argument('--server', default=DEFAULT_SERVER,
                       help=f'Alert server URL (default: {DEFAULT_SERVER})')
    parser.add_argument('--key', required=True,
                       help='Admin API key')
    parser.add_argument('--group', default=DEFAULT_GROUP,
                       help=f'Azure AD group name (default: {DEFAULT_GROUP})')
    parser.add_argument('--users', 
                       help='Comma-separated list of users to update (default: root)')
    parser.add_argument('--install', action='store_true',
                       help='Install as cron job (runs every minute)')
    parser.add_argument('--uninstall', action='store_true',
                       help='Uninstall cron job')
    
    args = parser.parse_args()
    
    # Handle uninstall
    if args.uninstall:
        uninstall_cron()
        return
    
    # Determine users to update
    if args.users:
        users = [u.strip() for u in args.users.split(',') if u.strip()]
    else:
        users = ['root']
    
    # Fetch keys from server
    keys_content = fetch_authorized_keys(args.server, args.key, args.group)
    if keys_content is None:
        logger.error("Failed to fetch authorized_keys from server")
        sys.exit(1)
    
    # Update keys for each user
    success_count = 0
    for user in users:
        if update_authorized_keys(user, keys_content):
            success_count += 1
    
    if success_count == 0:
        logger.error("Failed to update keys for any user")
        sys.exit(1)
    
    logger.info(f"Successfully updated keys for {success_count}/{len(users)} user(s)")
    
    # Install cron if requested
    if args.install:
        install_cron(args.server, args.key, args.group, users)


if __name__ == '__main__':
    main()
