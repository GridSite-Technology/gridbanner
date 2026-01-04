using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
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

                // Compliance badge settings
                var complianceEnabled = ParseInt(config.GetValueOrDefault("compliance_check_enabled", "1"), 1) == 1;
                var complianceStatusFallback = ParseInt(config.GetValueOrDefault("compliance_status", "1"), 1);
                complianceStatusFallback = complianceStatusFallback == 1 ? 1 : 0;

                var complianceCommand = config.GetValueOrDefault("compliance_check_command", string.Empty).Trim();
                var complianceStatus = complianceStatusFallback;

                if (complianceEnabled && !string.IsNullOrWhiteSpace(complianceCommand))
                {
                    LogMessage("Running compliance_check_command...");
                    if (TryRunComplianceCommand(complianceCommand, out var commandCompliant, out var details))
                    {
                        complianceStatus = commandCompliant ? 1 : 0;
                        LogMessage($"Compliance command result: {(complianceStatus == 1 ? "COMPLIANT" : "NON-COMPLIANT")} ({details})");
                    }
                    else
                    {
                        LogMessage($"Compliance command failed; using compliance_status fallback={complianceStatusFallback}. Details: {details}");
                    }
                }
                else
                {
                    LogMessage($"Compliance: enabled={complianceEnabled}, using compliance_status={complianceStatusFallback} (no command)");
                }

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
                                BannerHeight = bannerHeight,
                                ComplianceEnabled = complianceEnabled,
                                ComplianceStatus = complianceStatus
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

        private static int ParseInt(string? raw, int fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static bool TryRunComplianceCommand(string command, out bool compliant, out string details)
        {
            compliant = true;
            details = "not run";

            try
            {
                // Use cmd.exe so config can specify PowerShell, scripts, etc.
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    details = "Process.Start returned null";
                    return false;
                }

                // Keep startup snappy; if it takes too long we fallback to config.
                var exited = proc.WaitForExit(2500);
                if (!exited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    details = "timeout (>2500ms)";
                    return false;
                }

                var stdout = proc.StandardOutput.ReadToEnd().Trim();
                var stderr = proc.StandardError.ReadToEnd().Trim();

                // Convention: exit code 0 = compliant
                compliant = proc.ExitCode == 0;
                details = $"exitCode={proc.ExitCode}"
                          + (string.IsNullOrWhiteSpace(stdout) ? "" : $", stdout='{stdout}'")
                          + (string.IsNullOrWhiteSpace(stderr) ? "" : $", stderr='{stderr}'");
                return true;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
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

