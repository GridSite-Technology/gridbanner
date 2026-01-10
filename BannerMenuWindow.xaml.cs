using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GridBanner
{
    public partial class BannerMenuWindow : Window
    {
        private bool _permitTerminate = false;
        private bool _keyringEnabled = false;
        
        public bool PermitTerminate
        {
            get => _permitTerminate;
            set
            {
                _permitTerminate = value;
                // Update visibility immediately if button exists
                if (TerminateButton != null)
                {
                    TerminateButton.Visibility = _permitTerminate ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        
        public bool KeyringEnabled
        {
            get => _keyringEnabled;
            set
            {
                _keyringEnabled = value;
                if (ManageKeysButton != null)
                {
                    ManageKeysButton.Visibility = _keyringEnabled ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        
        public event EventHandler? ManageKeysClicked;

        public BannerMenuWindow()
        {
            InitializeComponent();
        }
        
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // Ensure visibility is set correctly after rendering
            TerminateButton.Visibility = PermitTerminate ? Visibility.Visible : Visibility.Collapsed;
            ManageKeysButton.Visibility = KeyringEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TerminateButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "Are you sure you want to terminate GridBanner?",
                "Confirm Termination",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");

            if (File.Exists(logPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "notepad.exe",
                        Arguments = $"\"{logPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to open log file:\n{ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(
                    $"Log file not found:\n{logPath}",
                    "Log File Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            
            // Close menu after opening logs
            Close();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigManager.LoadConfig();
            
            string GetConfigValue(string key, string defaultValue = "Not set")
            {
                return config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) 
                    ? value 
                    : defaultValue;
            }
            
            var aboutMessage = "GridBanner\n\n" +
                              "Modern replacement for NetBanner.exe\n\n" +
                              "A multi-monitor banner application that displays user information, " +
                              "classification level, organization name, and device compliance status.\n\n" +
                              "Copyright © 2026 GridSite Technology, Inc.\n\n" +
                              "Configuration:\n" +
                              $"  Background Color: {GetConfigValue("background_color")}\n" +
                              $"  Foreground Color: {GetConfigValue("foreground_color")}\n" +
                              $"  Classification Level: {GetConfigValue("classification_level")}\n" +
                              $"  Banner Height: {GetConfigValue("banner_height")}\n" +
                              $"  Organization Name: {GetConfigValue("org_name", "Not set (auto-detected)")}\n" +
                              $"  Site Name: {GetConfigValue("site_name")}\n" +
                              $"  Compliance Check Enabled: {GetConfigValue("compliance_check_enabled")}\n" +
                              $"  Compliance Status: {GetConfigValue("compliance_status")}\n" +
                              $"  Compliance Command: {GetConfigValue("compliance_check_command")}\n" +
                              $"  Permit Terminate: {GetConfigValue("permit_terminate")}\n" +
                              $"  Keyring Enabled: {GetConfigValue("keyring_enabled")}\n" +
                              $"  Alert File Location: {GetConfigValue("alert_file_location")}\n" +
                              $"  Alert URL: {GetConfigValue("alert_url")}\n" +
                              $"  Alert Poll Seconds: {GetConfigValue("alert_poll_seconds")}";

            MessageBox.Show(
                aboutMessage,
                "About GridBanner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            // Close menu after showing about
            Close();
        }
        
        private void LicenseButton_Click(object sender, RoutedEventArgs e)
        {
            var licenseText = @"MIT License

Copyright (c) 2026 GridSite Technology, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

            MessageBox.Show(
                licenseText,
                "MIT License",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            // Close menu after showing license
            Close();
        }
        
        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/GridSite-Technology/gridbanner/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open GitHub page:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            
            // Close menu after opening GitHub
            Close();
        }
        
        private void ManageKeysButton_Click(object sender, RoutedEventArgs e)
        {
            ManageKeysClicked?.Invoke(this, EventArgs.Empty);
            Close();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

