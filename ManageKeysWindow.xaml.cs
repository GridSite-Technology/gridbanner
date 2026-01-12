using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridBanner
{
    /// <summary>
    /// View model for displaying a key in the list.
    /// </summary>
    public class KeyDisplayItem
    {
        public string? KeyName { get; set; }
        public string? KeyType { get; set; }
        public string? Fingerprint { get; set; }
        public string? UploadedAt { get; set; }
        public string? StatusText { get; set; }
        public Brush? StatusColor { get; set; }
        public string? ActionText { get; set; }
        public Visibility ActionVisibility { get; set; } = Visibility.Visible;
        public PublicKeyInfo? OriginalKey { get; set; }
        public KeyStatus Status { get; set; }
        
        // Compliance properties
        public bool IsCompliant { get; set; }
        public string? ComplianceText { get; set; }
        public Brush? ComplianceColor { get; set; }
        
        // Sync status
        public string SyncStatus { get; set; } = "UNKNOWN";
        public Brush? SyncStatusColor { get; set; }
        
        // Selection
        public bool IsSelected { get; set; }
        
        /// <summary>
        /// Check if the key type is compliant with security policies.
        /// Allowed types: Ed25519, ECDSA (ssh-ed25519, ecdsa-sha2-*)
        /// </summary>
        public static bool IsKeyTypeCompliant(string? keyType)
        {
            if (string.IsNullOrEmpty(keyType)) return false;
            
            var type = keyType.ToLowerInvariant();
            
            // Ed25519 is the preferred modern algorithm
            if (type.Contains("ed25519")) return true;
            
            // ECDSA with secure curves is acceptable
            if (type.Contains("ecdsa")) return true;
            
            // RSA with sufficient key size could be acceptable, but we'll be strict
            // and only allow modern algorithms
            
            return false;
        }
        
        /// <summary>
        /// Evaluate compliance: key must be a compliant type AND password-protected.
        /// </summary>
        public static (bool isCompliant, string reason) EvaluateCompliance(PublicKeyInfo key)
        {
            var issues = new List<string>();
            
            // Check key type
            if (!IsKeyTypeCompliant(key.KeyType))
            {
                issues.Add($"Key type '{key.KeyType}' is not compliant (use Ed25519 or ECDSA)");
            }
            
            // Check password protection
            if (!key.IsPasswordProtected)
            {
                issues.Add("Key is not password-protected");
            }
            
            if (issues.Count == 0)
            {
                return (true, "Compliant ✓");
            }
            else
            {
                return (false, string.Join("; ", issues));
            }
        }
    }

    public enum KeyStatus
    {
        Pending,    // Not uploaded, not ignored
        Uploaded,   // Already on server
        Ignored     // User chose not to upload
    }

    public partial class ManageKeysWindow : Window
    {
        private readonly KeyringManager _keyringManager;
        private KeySummary? _currentSummary;

        public ManageKeysWindow(KeyringManager keyringManager)
        {
            InitializeComponent();
            _keyringManager = keyringManager;
            
            Loaded += async (s, e) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                _currentSummary = await _keyringManager.GetKeySummaryAsync();
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading keys: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateUI()
        {
            if (_currentSummary == null) return;

            // Local keys with status
            var localItems = new List<KeyDisplayItem>();
            
            foreach (var key in _currentSummary.UploadedLocalKeys)
            {
                var (isCompliant, complianceReason) = KeyDisplayItem.EvaluateCompliance(key);
                localItems.Add(new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType,
                    Fingerprint = key.Fingerprint,
                    StatusText = "✓ Uploaded to keyring",
                    StatusColor = Brushes.Green,
                    ActionVisibility = Visibility.Collapsed,
                    OriginalKey = key,
                    Status = KeyStatus.Uploaded,
                    IsCompliant = isCompliant,
                    ComplianceText = complianceReason,
                    ComplianceColor = isCompliant ? Brushes.Green : Brushes.OrangeRed,
                    SyncStatus = "SYNCED",
                    SyncStatusColor = Brushes.Green
                });
            }
            
            foreach (var key in _currentSummary.PendingLocalKeys)
            {
                var (isCompliant, complianceReason) = KeyDisplayItem.EvaluateCompliance(key);
                var passwordNote = key.IsPasswordProtected ? " 🔒" : "";
                
                // Check if this key exists on the server (by fingerprint)
                var serverFingerprints = _currentSummary.ServerKeys
                    .Where(k => !string.IsNullOrEmpty(k.Fingerprint))
                    .Select(k => k.Fingerprint!)
                    .ToHashSet();
                
                var isOnServer = !string.IsNullOrEmpty(key.Fingerprint) && 
                                serverFingerprints.Contains(key.Fingerprint);
                
                // Only show "Ready to upload" if compliant
                string statusText;
                Brush statusColor;
                Visibility actionVis;
                string syncStatus;
                Brush syncStatusColor;
                
                if (isCompliant)
                {
                    statusText = key.IsPasswordProtected 
                        ? "⬆ Ready to upload (password required)" 
                        : "⬆ Ready to upload";
                    statusColor = Brushes.DodgerBlue;
                    actionVis = Visibility.Visible;
                    
                    // Set sync status based on server check
                    if (isOnServer)
                    {
                        syncStatus = "SYNCED";
                        syncStatusColor = Brushes.Green;
                    }
                    else
                    {
                        syncStatus = "NOT SYNCED";
                        syncStatusColor = Brushes.OrangeRed;
                    }
                }
                else
                {
                    statusText = "⚠ Not compliant - cannot upload";
                    statusColor = Brushes.OrangeRed;
                    actionVis = Visibility.Collapsed;
                    syncStatus = "N/A";
                    syncStatusColor = Brushes.Gray;
                }
                
                localItems.Add(new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType + passwordNote,
                    Fingerprint = key.Fingerprint,
                    StatusText = statusText,
                    StatusColor = statusColor,
                    ActionText = "Upload",
                    ActionVisibility = actionVis,
                    OriginalKey = key,
                    Status = KeyStatus.Pending,
                    IsCompliant = isCompliant,
                    ComplianceText = complianceReason,
                    ComplianceColor = isCompliant ? Brushes.Green : Brushes.OrangeRed,
                    SyncStatus = syncStatus,
                    SyncStatusColor = syncStatusColor
                });
            }
            
            // NOTE: Ignored keys are NOT shown in the Local Keys tab - they only appear in the Ignored tab
            
            LocalKeysList.ItemsSource = localItems;

            // Server keys - include both synced local keys AND server-only keys
            var uploadedLocalFingerprints = _currentSummary.UploadedLocalKeys
                .Where(k => !string.IsNullOrEmpty(k.Fingerprint))
                .Select(k => k.Fingerprint!)
                .ToHashSet();
            
            var serverItems = _currentSummary.ServerKeys.Select(key => 
            {
                var isServerOnly = !string.IsNullOrEmpty(key.Fingerprint) && 
                                  !uploadedLocalFingerprints.Contains(key.Fingerprint);
                
                return new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType,
                    Fingerprint = key.Fingerprint,
                    UploadedAt = !string.IsNullOrEmpty(key.UploadedAt) 
                        ? $"Uploaded: {DateTime.Parse(key.UploadedAt).ToLocalTime():g}" 
                        : null,
                    StatusText = isServerOnly ? "⚠ Server-only (not found locally)" : "✓ Synced with local key",
                    StatusColor = isServerOnly ? Brushes.Orange : Brushes.Green,
                    OriginalKey = key
                };
            }).ToList();
            
            ServerKeysList.ItemsSource = serverItems;

            // Ignored keys - with compliance info
            var ignoredItems = _currentSummary.IgnoredLocalKeys.Select(key => 
            {
                var (isCompliant, complianceReason) = KeyDisplayItem.EvaluateCompliance(key);
                return new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType,
                    Fingerprint = key.Fingerprint,
                    OriginalKey = key,
                    IsCompliant = isCompliant,
                    ComplianceText = complianceReason,
                    ComplianceColor = isCompliant ? Brushes.Green : Brushes.OrangeRed,
                    SyncStatus = "IGNORED",
                    SyncStatusColor = Brushes.Gray
                };
            }).ToList();
            
            IgnoredKeysList.ItemsSource = ignoredItems;
        }

        private async void KeyAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is KeyDisplayItem item && item.OriginalKey != null)
            {
                // Reset retry count when user manually clicks upload
                await UploadKeyWithPasswordPromptAsync(item, null, 0, button);
            }
        }
        
        private async Task UploadKeyWithPasswordPromptAsync(KeyDisplayItem item, string? password = null, int retryCount = 0, System.Windows.Controls.Button? senderButton = null)
        {
            if (item.OriginalKey == null) return;
            
            // Prevent infinite password loops
            const int maxPasswordRetries = 3;
            if (retryCount >= maxPasswordRetries)
            {
                MessageBox.Show(
                    $"Too many failed password attempts for key '{item.KeyName}'. Please try again later.",
                    "Too Many Attempts",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Show progress indicator (disable button, show status)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (senderButton != null)
                    {
                        senderButton.IsEnabled = false;
                        senderButton.Content = "Uploading...";
                    }
                });
                
                // Upload the key with proof of possession
                Log($"Starting upload for key: {item.KeyName}");
                var result = await _keyringManager.UploadKeyAsync(
                    item.OriginalKey, 
                    item.OriginalKey.SourcePath,
                    password);
                Log($"Upload completed. Success: {result.Success}, Error: {result.Error}");
                
                if (result.Success)
                {
                    var verifiedText = result.Verified ? " (verified)" : "";
                    MessageBox.Show(
                        $"Key '{item.KeyName}' uploaded successfully{verifiedText}!", 
                        "Success", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                    await RefreshAsync();
                }
                else if (result.NeedsPassword)
                {
                    // Check if this is a retry after a wrong password
                    bool isWrongPassword = !string.IsNullOrEmpty(password) && 
                                         result.Error != null &&
                                         (result.Error.Contains("Wrong password") || 
                                          result.Error.Contains("decryption failed") ||
                                          result.Error.Contains("check values don't match"));
                    
                    // Show error if password was wrong
                    if (isWrongPassword)
                    {
                        MessageBox.Show(
                            $"The password you entered is incorrect.\n\nPlease try again.",
                            "Incorrect Password",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    
                    // Prompt for password
                    var passwordDialog = new PasswordPromptWindow(item.KeyName ?? "SSH Key");
                    if (passwordDialog.ShowDialog() == true && !string.IsNullOrEmpty(passwordDialog.Password))
                    {
                        // Retry with new password
                        await UploadKeyWithPasswordPromptAsync(item, passwordDialog.Password, retryCount + 1, senderButton);
                    }
                }
                else
                {
                    // Ask if user wants to ignore
                    var dialogResult = MessageBox.Show(
                        $"Failed to upload key '{item.KeyName}'.\n\nError: {result.Error}\n\nWould you like to ignore this key?",
                        "Upload Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (dialogResult == MessageBoxResult.Yes)
                    {
                        _keyringManager.IgnoreKey(item.OriginalKey);
                        await RefreshAsync();
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Log($"Upload was cancelled or timed out: {ex.Message}");
                MessageBox.Show(
                    "Upload timed out. Please check your network connection and try again.",
                    "Upload Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log($"Error during upload: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                MessageBox.Show(
                    $"Error uploading key: {ex.Message}\n\nPlease check the logs for more details.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable button
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (senderButton != null)
                    {
                        senderButton.IsEnabled = true;
                        senderButton.Content = "Upload";
                    }
                });
            }
        }
        
        private void Log(string message)
        {
            // Forward to KeyringManager's logging
            System.Diagnostics.Debug.WriteLine($"[ManageKeysWindow] {message}");
        }

        private async void UnignoreKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is KeyDisplayItem item && item.OriginalKey != null)
            {
                if (!string.IsNullOrEmpty(item.OriginalKey.Fingerprint))
                {
                    _keyringManager.UnignoreKey(item.OriginalKey.Fingerprint);
                    await RefreshAsync();
                }
            }
        }
        
        private async void DeleteServerKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is KeyDisplayItem item && item.OriginalKey != null)
            {
                // Get the key ID from the server key
                var keyId = item.OriginalKey.Id;
                if (string.IsNullOrEmpty(keyId))
                {
                    MessageBox.Show(
                        "Unable to delete key: Key ID not found.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                
                var result = MessageBox.Show(
                    $"Are you sure you want to delete key '{item.KeyName}' from the server?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var success = await _keyringManager.DeleteKeyAsync(keyId);
                        if (success)
                        {
                            MessageBox.Show(
                                $"Key '{item.KeyName}' deleted successfully.",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            await RefreshAsync();
                        }
                        else
                        {
                            MessageBox.Show(
                                $"Failed to delete key '{item.KeyName}'. Please check the logs for details.",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Error deleting key: {ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void ImportKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select SSH Public Key",
                    Filter = "SSH Public Keys (*.pub)|*.pub|All Files (*.*)|*.*",
                    InitialDirectory = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh")
                };
                
                if (dialog.ShowDialog() == true)
                {
                    var path = dialog.FileName;
                    
                    // Validate it looks like an SSH public key
                    var content = System.IO.File.ReadAllText(path).Trim();
                    if (!content.StartsWith("ssh-") && !content.StartsWith("ecdsa-"))
                    {
                        MessageBox.Show(
                            "The selected file doesn't appear to be a valid SSH public key.\n\nSSH public keys typically start with 'ssh-rsa', 'ssh-ed25519', 'ecdsa-sha2-', etc.",
                            "Invalid Key File",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Add the custom key path
                    _keyringManager.AddCustomKeyPath(path);
                    
                    MessageBox.Show(
                        $"Key imported successfully:\n{System.IO.Path.GetFileName(path)}",
                        "Key Imported",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // Refresh the list
                    await RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void SyncSelectedKeys_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LocalKeysList.SelectedItems.Cast<KeyDisplayItem>().ToList();
            
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more keys to sync.", "No Keys Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Filter to pending/ignored keys that are also compliant
            var pendingKeys = selectedItems
                .Where(k => (k.Status == KeyStatus.Pending || k.Status == KeyStatus.Ignored) && k.IsCompliant)
                .ToList();
            
            var nonCompliantCount = selectedItems.Count(k => !k.IsCompliant);
            
            if (pendingKeys.Count == 0)
            {
                if (nonCompliantCount > 0)
                {
                    MessageBox.Show(
                        "The selected keys are not compliant and cannot be synced.\n\nKeys must be Ed25519 or ECDSA and password-protected to be uploaded.",
                        "Not Compliant",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show("All selected keys are already synced.", "Already Synced", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }
            
            foreach (var item in pendingKeys)
            {
                await UploadKeyWithPasswordPromptAsync(item);
            }
        }
        
        private void RemoveSelectedKeys_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LocalKeysList.SelectedItems.Cast<KeyDisplayItem>().ToList();
            
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more keys to ignore.", "No Keys Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show(
                $"Are you sure you want to ignore {selectedItems.Count} key(s)?\n\nIgnored keys won't be synced but will remain on your system.",
                "Confirm Ignore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    if (item.OriginalKey != null)
                    {
                        _keyringManager.IgnoreKey(item.OriginalKey);
                    }
                }
                
                // Refresh to show updated status
                Dispatcher.BeginInvoke(new Action(async () => await RefreshAsync()));
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

