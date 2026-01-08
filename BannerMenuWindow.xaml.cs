using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GridBanner
{
    public partial class BannerMenuWindow : Window
    {
        public bool PermitTerminate { get; set; } = false;

        public BannerMenuWindow()
        {
            InitializeComponent();
            TerminateButton.Visibility = PermitTerminate ? Visibility.Visible : Visibility.Collapsed;
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
                              "© GridSite Technology\n\n" +
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
                              $"  Alert File Location: {GetConfigValue("alert_file_location")}\n" +
                              $"  Alert URL: {GetConfigValue("alert_url")}\n" +
                              $"  Alert Poll Seconds: {GetConfigValue("alert_poll_seconds")}";

            MessageBox.Show(
                aboutMessage,
                "About GridBanner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

