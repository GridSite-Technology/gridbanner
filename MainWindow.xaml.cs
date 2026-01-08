using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Linq;

namespace GridBanner
{
    public partial class MainWindow : Window
    {
        private readonly List<BannerWindow> _bannerWindows = new();
        private readonly List<AlertBarWindow> _alertWindows = new();
        private readonly List<SuperCriticalAlertWindow> _superCriticalWindows = new();
        private readonly AlertSoundPlayer _alertSoundPlayer = new();
        private AlertManager? _alertManager;
        private AlertMessage? _activeAlert;
        private string? _dismissedAlertSignature;
        private string? _closedSuperCriticalSignature;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner", "gridbanner.log");

        private bool _isSessionLocked;

        public MainWindow()
        {
            InitializeComponent();
            LogMessage("MainWindow constructor called");

            // Handle lock/unlock + display changes (monitors / work area can change during these events)
            try
            {
                SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            }
            catch
            {
                // ignore
            }

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

                if (_isSessionLocked)
                {
                    LogMessage("Session is locked; skipping banner creation.");
                    return;
                }
                
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
                var computerName = Environment.MachineName;
                var username = userInfo.GetValueOrDefault("username", Environment.UserName);

                var orgOverride = config.GetValueOrDefault("org_name", string.Empty);
                var orgName = !string.IsNullOrWhiteSpace(orgOverride)
                    ? orgOverride.Trim()
                    : userInfo.GetValueOrDefault("org_name", "ORGANIZATION");

                // Compliance badge settings
                var complianceEnabled = ParseInt(config.GetValueOrDefault("compliance_check_enabled", "1"), 1) == 1;

                // Conservative: if we cannot positively prove compliant, show NOT compliant.
                var complianceStatusFallback = ParseInt(config.GetValueOrDefault("compliance_status", "0"), 0);
                complianceStatusFallback = complianceStatusFallback == 1 ? 1 : 0;

                var complianceCommand = config.GetValueOrDefault("compliance_check_command", string.Empty).Trim();
                var complianceStatus = 0;

                if (!complianceEnabled)
                {
                    LogMessage("Compliance badge disabled (compliance_check_enabled=0)");
                }
                else if (!string.IsNullOrWhiteSpace(complianceCommand))
                {
                    LogMessage("Running compliance_check_command...");
                    if (TryRunComplianceCommand(complianceCommand, out var commandCompliant, out var details))
                    {
                        complianceStatus = commandCompliant ? 1 : 0;
                        LogMessage($"Compliance command result: {(complianceStatus == 1 ? "COMPLIANT" : "NOT COMPLIANT")} ({details})");
                    }
                    else
                    {
                        // If the check can't run reliably, treat as NOT compliant
                        complianceStatus = 0;
                        LogMessage($"Compliance command failed; treating as NOT COMPLIANT. Details: {details}");
                    }
                }
                else
                {
                    // No check configured: use fallback, but default is NOT compliant.
                    complianceStatus = complianceStatusFallback;
                    LogMessage($"Compliance: enabled={complianceEnabled}, no command; using compliance_status={complianceStatusFallback}");
                }

                var siteNames = config.GetValueOrDefault("site_name", string.Empty).Trim();

                CreateOrRefreshBanners(
                    computerName,
                    username,
                    orgName,
                    siteNames,
                    classificationLevel,
                    backgroundColor,
                    foregroundColor,
                    bannerHeight,
                    complianceEnabled,
                    complianceStatus);

                // Alert overlays (optional; configured via conf.ini)
                SetupAlertSystem(config, bannerHeight, computerName, username, orgName, classificationLevel);

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

        private void CreateOrRefreshBanners(
            string computerName,
            string username,
            string orgName,
            string siteNames,
            string classificationLevel,
            System.Windows.Media.Color backgroundColor,
            System.Windows.Media.Color foregroundColor,
            double bannerHeight,
            bool complianceEnabled,
            int complianceStatus)
        {
            CloseAllBanners();

            var screens = Screen.AllScreens;
            LogMessage($"Detected {screens?.Length ?? 0} screen(s)");

            if (screens == null || screens.Length == 0)
            {
                LogMessage("ERROR: No screens detected!");
                return;
            }

            int screenIndex = 0;
            foreach (var screen in screens)
            {
                try
                {
                    LogMessage($"Creating banner window for screen {screenIndex}: Bounds={screen.Bounds}, Primary={screen.Primary}");

                    var bannerWindow = new BannerWindow
                    {
                        ComputerName = computerName,
                        Username = username,
                        OrgName = orgName,
                        SiteNames = siteNames,
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

        private void SetupAlertSystem(Dictionary<string, string> config, double alertBarHeight, string computerName, string username, string orgName, string classificationLevel)
        {
            var alertFile = config.GetValueOrDefault("alert_file_location", string.Empty).Trim();
            var alertUrl = config.GetValueOrDefault("alert_url", string.Empty).Trim();

            // Nothing configured => clear any active alerts and stop monitoring
            if (string.IsNullOrWhiteSpace(alertFile) && string.IsNullOrWhiteSpace(alertUrl))
            {
                StopAlertSystem();
                return;
            }

            var pollSecondsRaw = config.GetValueOrDefault("alert_poll_seconds", "5").Trim();
            var pollSeconds = 5;
            if (!int.TryParse(pollSecondsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out pollSeconds))
            {
                pollSeconds = 5;
            }
            if (pollSeconds < 1) pollSeconds = 1;
            if (pollSeconds > 300) pollSeconds = 300;

            EnsureAlertManager();
            CreateOrRefreshAlertWindows(alertBarHeight);

            var siteNames = config.GetValueOrDefault("site_name", string.Empty).Trim();
            
            // Prepare system info for reporting (complianceStatus already calculated above)
            var systemInfo = new AlertManager.SystemInfo(
                WorkstationName: computerName,
                Username: username,
                Classification: classificationLevel,
                Location: siteNames,  // Use site names as location
                Company: orgName,
                BackgroundColor: backgroundColor.ToString(),
                ForegroundColor: foregroundColor.ToString(),
                ComplianceStatus: complianceStatus
            );
            
            _alertManager!.Configure(alertFile, alertUrl, TimeSpan.FromSeconds(pollSeconds), siteNames, systemInfo);
            _alertManager.Start();

            LogMessage($"Alert system enabled. file='{alertFile}', url='{alertUrl}', pollSeconds={pollSeconds}, sites='{siteNames}'");
        }

        private void EnsureAlertManager()
        {
            if (_alertManager != null)
            {
                return;
            }

            _alertManager = new AlertManager();
            _alertManager.AlertChanged += (_, alert) =>
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyAlert(alert)));
            };
        }

        private void StopAlertSystem()
        {
            try { _alertManager?.Stop(); } catch { /* ignore */ }
            _activeAlert = null;
            _dismissedAlertSignature = null;
            _closedSuperCriticalSignature = null;
            _alertSoundPlayer.Stop();
            HideAllAlertWindows();
            HideAllSuperCriticalWindows();
        }

        private void CreateOrRefreshAlertWindows(double height)
        {
            // Recreate per current monitors; these are overlay bars (topmost) and should not reserve space.
            CloseAllAlertWindows();
            CloseAllSuperCriticalWindows();

            var screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return;
            }

            foreach (var screen in screens)
            {
                var w = new AlertBarWindow();
                w.SetScreen(screen, height);
                w.OnDismissRequested = DismissCurrentAlert;
                w.Hide();
                _alertWindows.Add(w);
            }

            // Prepare super-critical overlays too (hidden until needed)
            CreateOrRefreshSuperCriticalWindows();
        }

        private void CloseAllAlertWindows()
        {
            foreach (var w in _alertWindows.ToList())
            {
                try { w.Close(); } catch { /* ignore */ }
            }
            _alertWindows.Clear();
        }

        private void HideAllAlertWindows()
        {
            foreach (var w in _alertWindows)
            {
                try { w.Hide(); } catch { /* ignore */ }
            }
        }

        private void CreateOrRefreshSuperCriticalWindows()
        {
            CloseAllSuperCriticalWindows();

            var screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return;
            }

            foreach (var screen in screens)
            {
                var w = new SuperCriticalAlertWindow();
                w.SetScreen(screen);
                w.OnClosedLocally = CloseSuperCriticalOverlayLocally;
                w.Hide();
                _superCriticalWindows.Add(w);
            }
        }

        private void CloseSuperCriticalOverlayLocally()
        {
            if (_activeAlert?.Level != AlertLevel.SuperCritical)
            {
                return;
            }

            _closedSuperCriticalSignature = _activeAlert.Signature;
            HideAllSuperCriticalWindows();

            // Re-show the non-dismissible sub-bar immediately (no need to wait for a new alert event).
            try
            {
                var alert = _activeAlert;
                var bgBrush = new SolidColorBrush(ParseColor(alert.BackgroundColor));
                var fgBrush = new SolidColorBrush(ParseColor(alert.ForegroundColor));

                foreach (var w in _alertWindows)
                {
                    try
                    {
                        w.ApplyAlert(alert, bgBrush, fgBrush, showDismiss: false);
                        if (!w.IsVisible)
                        {
                            w.Show();
                        }
                        w.Topmost = true;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void CloseAllSuperCriticalWindows()
        {
            foreach (var w in _superCriticalWindows.ToList())
            {
                try { w.Close(); } catch { /* ignore */ }
            }
            _superCriticalWindows.Clear();
        }

        private void HideAllSuperCriticalWindows()
        {
            foreach (var w in _superCriticalWindows)
            {
                try { w.Hide(); } catch { /* ignore */ }
            }
        }

        private void ApplyAlert(AlertMessage? alert)
        {
            _activeAlert = alert;

            if (alert == null)
            {
                _dismissedAlertSignature = null;
                _closedSuperCriticalSignature = null;
                _alertSoundPlayer.Update(null, dismissed: false);
                HideAllAlertWindows();
                HideAllSuperCriticalWindows();
                return;
            }

            var isDismissable = alert.Level == AlertLevel.Routine || alert.Level == AlertLevel.Urgent;
            var dismissed = isDismissable && string.Equals(_dismissedAlertSignature, alert.Signature, StringComparison.Ordinal);

            var bgBrush = new SolidColorBrush(ParseColor(alert.BackgroundColor));
            var fgBrush = new SolidColorBrush(ParseColor(alert.ForegroundColor));

            // Super critical: show full-screen overlay (can be closed locally), plus a non-dismissible top bar.
            if (alert.Level == AlertLevel.SuperCritical)
            {
                if (_superCriticalWindows.Count == 0)
                {
                    CreateOrRefreshSuperCriticalWindows();
                }

                var overlayClosedLocally = string.Equals(_closedSuperCriticalSignature, alert.Signature, StringComparison.Ordinal);
                if (!overlayClosedLocally)
                {
                    foreach (var w in _superCriticalWindows)
                    {
                        try
                        {
                            w.Apply(alert, bgBrush, fgBrush);
                            if (!w.IsVisible)
                            {
                                w.Show();
                            }
                            w.Topmost = true;
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    // Keep the UI focused: while the full-screen overlay is visible, hide the smaller alert bar.
                    HideAllAlertWindows();
                }
                else
                {
                    // Overlay was closed locally; show the top bar (still non-dismissible) until admin clears alert.
                    foreach (var w in _alertWindows)
                    {
                        try
                        {
                            w.ApplyAlert(alert, bgBrush, fgBrush, showDismiss: false);
                            if (!w.IsVisible)
                            {
                                w.Show();
                            }
                            w.Topmost = true;
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                // Beep until cleared (handled in AlertSoundPlayer)
                _alertSoundPlayer.Update(alert, dismissed: false);
                return;
            }

            if (dismissed)
            {
                _alertSoundPlayer.Update(alert, dismissed: true);
                HideAllAlertWindows();
                HideAllSuperCriticalWindows();
                return;
            }

            foreach (var w in _alertWindows)
            {
                try
                {
                    w.ApplyAlert(alert, bgBrush, fgBrush, showDismiss: isDismissable && alert.Level != AlertLevel.Critical);
                    if (!w.IsVisible)
                    {
                        w.Show();
                    }
                    w.Topmost = true;
                }
                catch
                {
                    // ignore
                }
            }

            _alertSoundPlayer.Update(alert, dismissed: false);
            HideAllSuperCriticalWindows();
        }

        private void DismissCurrentAlert()
        {
            if (_activeAlert == null)
            {
                return;
            }

            if (!(_activeAlert.Level == AlertLevel.Routine || _activeAlert.Level == AlertLevel.Urgent))
            {
                return;
            }

            _dismissedAlertSignature = _activeAlert.Signature;
            _alertSoundPlayer.Update(_activeAlert, dismissed: true);
            HideAllAlertWindows();
        }

        private void CloseAllBanners()
        {
            foreach (var w in _bannerWindows.ToList())
            {
                try
                {
                    w.Close();
                }
                catch
                {
                    // ignore
                }
            }
            _bannerWindows.Clear();
        }

        private void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            // Runs on a system thread; marshal to UI thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Reason == SessionSwitchReason.SessionLock)
                {
                    _isSessionLocked = true;
                    LogMessage("Session locked: closing banners / unregistering appbars.");
                    CloseAllBanners();
                    StopAlertSystem();
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    _isSessionLocked = false;
                    LogMessage("Session unlocked: recreating banners.");
                    MainWindow_Loaded(this, new RoutedEventArgs());
                }
            }));
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isSessionLocked)
                {
                    return;
                }

                LogMessage("Display settings changed: refreshing banners.");
                MainWindow_Loaded(this, new RoutedEventArgs());
            }));
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
            CloseAllBanners();
            CloseAllAlertWindows();
            CloseAllSuperCriticalWindows();
            try { _alertManager?.Dispose(); } catch { /* ignore */ }
            _alertManager = null;

            try
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            }
            catch
            {
                // ignore
            }
            base.OnClosed(e);
        }
    }
}

