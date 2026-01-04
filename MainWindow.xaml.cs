using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Linq;

namespace GridBanner
{
    public partial class MainWindow : Window
    {
        private readonly List<BannerWindow> _bannerWindows = new();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner", "gridbanner.log");

        public MainWindow()
        {
            InitializeComponent();
            LogMessage("MainWindow constructor called");

            // MainWindow is created as Hidden in XAML, so Loaded may never fire.
            // Kick off banner creation via the Dispatcher so it always runs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogMessage("Dispatcher kickoff executing (startup)");
                MainWindow_Loaded(this, new RoutedEventArgs());
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static void LogMessage(string message)
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogPath, logEntry + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("MainWindow_Loaded started");
                
                // Load configuration (this will create default if needed)
                LogMessage("Loading configuration...");
                var config = ConfigManager.LoadConfig();
                LogMessage($"Config loaded: bg={config.GetValueOrDefault("background_color")}, fg={config.GetValueOrDefault("foreground_color")}, level={config.GetValueOrDefault("classification_level")}");
                
                var userInfo = UserInfoHelper.GetUserInfo();
                LogMessage($"User info: username={userInfo.GetValueOrDefault("username")}, org={userInfo.GetValueOrDefault("org_name")}");

                // Parse colors with fallbacks
                var backgroundColor = ParseColor(config.GetValueOrDefault("background_color", "#FFA500"));
                var foregroundColor = ParseColor(config.GetValueOrDefault("foreground_color", "#FFFFFF"));
                var classificationLevel = config.GetValueOrDefault("classification_level", "UNSPECIFIED CLASSIFICATION");

                var bannerHeight = 30d;
                var bannerHeightRaw = config.GetValueOrDefault("banner_height", "30");
                if (!double.TryParse(bannerHeightRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out bannerHeight))
                {
                    bannerHeight = 30d;
                }
                if (bannerHeight < 20) bannerHeight = 20;
                if (bannerHeight > 300) bannerHeight = 300;
                LogMessage($"Banner height: {bannerHeight} (raw='{bannerHeightRaw}')");

                // Get username and org with fallbacks
                var username = userInfo.GetValueOrDefault("username", Environment.UserName);

                var orgOverride = config.GetValueOrDefault("org_name", string.Empty);
                var orgName = !string.IsNullOrWhiteSpace(orgOverride)
                    ? orgOverride.Trim()
                    : userInfo.GetValueOrDefault("org_name", "ORGANIZATION");

                // Create banner window for each screen
                var screens = Screen.AllScreens;
                LogMessage($"Detected {screens?.Length ?? 0} screen(s)");
                
                if (screens != null && screens.Length > 0)
                {
                    int screenIndex = 0;
                    foreach (var screen in screens)
                    {
                        try
                        {
                            LogMessage($"Creating banner window for screen {screenIndex}: Bounds={screen.Bounds}, Primary={screen.Primary}");
                            
                            var bannerWindow = new BannerWindow
                            {
                                Username = username,
                                OrgName = orgName,
                                ClassificationLevel = classificationLevel,
                                BackgroundColor = new SolidColorBrush(backgroundColor),
                                ForegroundColor = new SolidColorBrush(foregroundColor),
                                BannerHeight = bannerHeight
                            };

                            bannerWindow.SetScreen(screen);
                            LogMessage($"Banner window {screenIndex} configured: Left={bannerWindow.Left}, Top={bannerWindow.Top}, Width={bannerWindow.Width}, Height={bannerWindow.Height}, Topmost={bannerWindow.Topmost}");
                            
                            bannerWindow.Show();
                            bannerWindow.Activate();
                            bannerWindow.Focus();
                            LogMessage($"Banner window {screenIndex} shown and activated");
                            
                            _bannerWindows.Add(bannerWindow);
                            screenIndex++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error creating banner window for screen {screenIndex}: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    
                    LogMessage($"Successfully created {_bannerWindows.Count} banner window(s)");
                }
                else
                {
                    LogMessage("ERROR: No screens detected!");
                }

                // Hide the main window
                Hide();
                LogMessage("MainWindow hidden, banner windows should be visible");
            }
            catch (Exception ex)
            {
                LogMessage($"CRITICAL ERROR in MainWindow_Loaded: {ex.Message}\n{ex.StackTrace}");
                System.Windows.MessageBox.Show($"Failed to start GridBanner: {ex.Message}", 
                    "GridBanner Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private System.Windows.Media.Color ParseColor(string colorString)
        {
            try
            {
                if (colorString.StartsWith("#"))
                {
                    var hex = colorString.Substring(1);
                    if (hex.Length == 6)
                    {
                        var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                        return System.Windows.Media.Color.FromRgb(r, g, b);
                    }
                }
            }
            catch
            {
                // If parsing fails, return default color
            }

            return Colors.Navy; // Default fallback
        }

        protected override void OnClosed(EventArgs e)
        {
            // Close all banner windows when main window closes
            foreach (var window in _bannerWindows)
            {
                window.Close();
            }
            base.OnClosed(e);
        }
    }
}

