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
            var aboutMessage = "GridBanner\n\n" +
                              "Modern replacement for NetBanner.exe\n\n" +
                              "A multi-monitor banner application that displays user information, " +
                              "classification level, organization name, and device compliance status.\n\n" +
                              "© GridSite Technology";

            MessageBox.Show(
                aboutMessage,
                "About GridBanner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}

