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
                localItems.Add(new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType,
                    Fingerprint = key.Fingerprint,
                    StatusText = "✓ Uploaded to keyring",
                    StatusColor = Brushes.Green,
                    ActionVisibility = Visibility.Collapsed,
                    OriginalKey = key,
                    Status = KeyStatus.Uploaded
                });
            }
            
            foreach (var key in _currentSummary.PendingLocalKeys)
            {
                var passwordNote = key.IsPasswordProtected ? " 🔒" : "";
                localItems.Add(new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType + passwordNote,
                    Fingerprint = key.Fingerprint,
                    StatusText = key.IsPasswordProtected 
                        ? "⬆ Ready to upload (password required)" 
                        : "⬆ Ready to upload",
                    StatusColor = Brushes.DodgerBlue,
                    ActionText = "Upload",
                    ActionVisibility = Visibility.Visible,
                    OriginalKey = key,
                    Status = KeyStatus.Pending
                });
            }
            
            foreach (var key in _currentSummary.IgnoredLocalKeys)
            {
                localItems.Add(new KeyDisplayItem
                {
                    KeyName = key.KeyName,
                    KeyType = key.KeyType,
                    Fingerprint = key.Fingerprint,
                    StatusText = "⊘ Ignored",
                    StatusColor = Brushes.Gray,
                    ActionText = "Upload",
                    ActionVisibility = Visibility.Visible,
                    OriginalKey = key,
                    Status = KeyStatus.Ignored
                });
            }
            
            LocalKeysList.ItemsSource = localItems;

            // Server keys
            var serverItems = _currentSummary.ServerKeys.Select(key => new KeyDisplayItem
            {
                KeyName = key.KeyName,
                KeyType = key.KeyType,
                Fingerprint = key.Fingerprint,
                UploadedAt = !string.IsNullOrEmpty(key.UploadedAt) 
                    ? $"Uploaded: {DateTime.Parse(key.UploadedAt).ToLocalTime():g}" 
                    : null,
                OriginalKey = key
            }).ToList();
            
            ServerKeysList.ItemsSource = serverItems;

            // Ignored keys
            var ignoredItems = _currentSummary.IgnoredLocalKeys.Select(key => new KeyDisplayItem
            {
                KeyName = key.KeyName,
                KeyType = key.KeyType,
                Fingerprint = key.Fingerprint,
                OriginalKey = key
            }).ToList();
            
            IgnoredKeysList.ItemsSource = ignoredItems;
        }

        private async void KeyAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is KeyDisplayItem item && item.OriginalKey != null)
            {
                await UploadKeyWithPasswordPromptAsync(item);
            }
        }
        
        private async Task UploadKeyWithPasswordPromptAsync(KeyDisplayItem item, string? password = null)
        {
            if (item.OriginalKey == null) return;
            
            try
            {
                // Upload the key with proof of possession
                var result = await _keyringManager.UploadKeyAsync(
                    item.OriginalKey, 
                    item.OriginalKey.SourcePath,
                    password);
                
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
                    // Prompt for password
                    var passwordDialog = new PasswordPromptWindow(item.KeyName ?? "SSH Key");
                    if (passwordDialog.ShowDialog() == true && !string.IsNullOrEmpty(passwordDialog.Password))
                    {
                        // Retry with password
                        await UploadKeyWithPasswordPromptAsync(item, passwordDialog.Password);
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
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

