using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace GridBanner
{
    public partial class MainWindow : Window
    {
        private readonly List<BannerWindow> _bannerWindows = new();
        private readonly List<AlertBarWindow> _alertWindows = new();
        private readonly List<SuperCriticalAlertWindow> _superCriticalWindows = new();
        private readonly List<SystemLockdownAlertWindow> _systemLockdownWindows = new();
        private SystemLockdownKeyboardHook? _lockdownKeyboardHook;
        private string? _closedSystemLockdownSignature;
        private System.Windows.Threading.DispatcherTimer? _lockdownFocusMonitor;
        private bool _systemLockdownWindowsInitialized = false; // Track if windows have been shown at least once
        private SystemLockdownAlertWindow? _primarySystemLockdownWindow = null; // Track primary monitor window
        private DateTime? _focusMonitoringStartTime = null; // Track when focus monitoring started (grace period)
        private readonly AlertSoundPlayer _alertSoundPlayer = new();
        private readonly KeyringManager _keyringManager = new();
        private AzureAuthManager? _azureAuthManager;
        private AlertManager? _alertManager;
        private ClipboardMonitor? _clipboardMonitor;
        private PasteInterceptor? _pasteInterceptor;
        private SensitivityConfig? _sensitivityConfig;
        private System.Windows.Threading.DispatcherTimer? _connectivityTimer;
        private System.Windows.Threading.DispatcherTimer? _keyringCheckTimer;
        private AlertMessage? _activeAlert;
        private string? _dismissedAlertSignature;
        private string? _closedSuperCriticalSignature;
        private bool _keyringEnabled = false;
        private List<PublicKeyInfo> _pendingKeys = new();
        private NotifyIcon? _trayIcon;
        private bool _trayOnlyMode = false;
        private HwndSource? _hotkeyHwndSource;
        private const int HOTKEY_ID = 9000;
        private const int HOTKEY_ID_COMMAND = 9001;
        private const int WM_HOTKEY = 0x0312;
        private bool _alertSilenced = false;
        private bool _alertHidden = false;
        private CommandWindow? _commandWindow;

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

            // Global hotkey will be registered in OnSourceInitialized when we have a window handle

            // MainWindow is created as Hidden in XAML, so Loaded may never fire.
            // Show window briefly to ensure handle is created, then hide it
            Show();
            Hide();
            
            // Kick off banner creation via the Dispatcher so it always runs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogMessage("Dispatcher kickoff executing (startup)");
                MainWindow_Loaded(this, new RoutedEventArgs());
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            LogMessage("OnSourceInitialized called");
            
            // Set up window handle for hotkey messages (if not already done)
            if (_hotkeyHwndSource == null)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    LogMessage($"Window handle obtained in OnSourceInitialized: {hwnd}");
                    _hotkeyHwndSource = HwndSource.FromHwnd(hwnd);
                    _hotkeyHwndSource?.AddHook(HotkeyWndProc);
                    
                    // Register global hotkey now that we have a window handle
                    DoRegisterGlobalHotkey();
                }
                else
                {
                    LogMessage("Window handle is zero in OnSourceInitialized");
                }
            }
        }
        
        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (id == HOTKEY_ID)
                {
                    // Hotkey pressed: CTRL+ALT+SHIFT+F12
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogMessage("Global hotkey CTRL+ALT+SHIFT+F12 pressed");
                        System.Windows.MessageBox.Show(
                            "Sequence captured",
                            "GridBanner Hotkey",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }));
                    handled = true;
                    return IntPtr.Zero;
                }
                else if (id == HOTKEY_ID_COMMAND)
                {
                    // Hotkey pressed: CTRL+ALT+SHIFT+F1
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogMessage("Global hotkey CTRL+ALT+SHIFT+F1 pressed - opening command window");
                        ShowCommandWindow();
                    }));
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }
        
        private void DoRegisterGlobalHotkey()
        {
            try
            {
                const int MOD_CONTROL = 0x0002;
                const int MOD_ALT = 0x0001;
                const int MOD_SHIFT = 0x0004;
                const int VK_F12 = 0x7B;
                const int VK_F1 = 0x70;
                
                var modifiers = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
                
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // Register CTRL+ALT+SHIFT+F12
                    if (HotkeyNativeMethods.RegisterHotKey(hwnd, HOTKEY_ID, modifiers, VK_F12))
                    {
                        LogMessage("Global hotkey CTRL+ALT+SHIFT+F12 registered successfully");
                    }
                    else
                    {
                        var error = Marshal.GetLastWin32Error();
                        LogMessage($"Failed to register global hotkey F12. Error code: {error}");
                    }
                    
                    // Register CTRL+ALT+SHIFT+F1
                    if (HotkeyNativeMethods.RegisterHotKey(hwnd, HOTKEY_ID_COMMAND, modifiers, VK_F1))
                    {
                        LogMessage("Global hotkey CTRL+ALT+SHIFT+F1 registered successfully");
                    }
                    else
                    {
                        var error = Marshal.GetLastWin32Error();
                        LogMessage($"Failed to register global hotkey F1. Error code: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error registering global hotkey: {ex.Message}");
            }
        }
        
        private void ShowCommandWindow()
        {
            try
            {
                // Close existing command window if open
                if (_commandWindow != null && _commandWindow.IsVisible)
                {
                    _commandWindow.Close();
                }
                
                _commandWindow = new CommandWindow();
                _commandWindow.CommandExecuted += CommandWindow_CommandExecuted;
                _commandWindow.Closed += (s, e) => { _commandWindow = null; };
                _commandWindow.Show();
                _commandWindow.Activate();
                _commandWindow.Focus();
            }
            catch (Exception ex)
            {
                LogMessage($"Error showing command window: {ex.Message}");
            }
        }
        
        private void CommandWindow_CommandExecuted(object? sender, string command)
        {
            try
            {
                LogMessage($"Command executed: {command}");
                var cmd = command.ToLowerInvariant().Trim();
                
                if (cmd == "silence")
                {
                    SilenceAlert();
                }
                else if (cmd == "hidealert")
                {
                    HideAlert();
                }
                else if (cmd == "managekeys")
                {
                    ShowManageKeysWindow();
                }
                else if (cmd == "exitlockdown")
                {
                    // Exit system lockdown if active - check both alert level and visible windows
                    bool hasActiveLockdown = (_activeAlert?.Level == AlertLevel.SystemLockdown) ||
                                            (_systemLockdownWindows != null && _systemLockdownWindows.Count > 0 && 
                                             _systemLockdownWindows.Any(w => w.IsVisible));
                    
                    if (hasActiveLockdown)
                    {
                        ExitSystemLockdown();
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "No system lockdown is currently active.",
                            "GridBanner Command",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Unknown command: {command}\n\nSupported commands:\n- silence\n- hidealert\n- managekeys",
                        "GridBanner Command",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error executing command: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Error executing command: {ex.Message}",
                    "GridBanner Command Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        
        private void SilenceAlert()
        {
            if (_activeAlert != null)
            {
                _alertSilenced = true;
                _alertSoundPlayer.Silence();
                LogMessage("Alert silenced");
                System.Windows.MessageBox.Show(
                    "Alert audio silenced.",
                    "GridBanner Command",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "No active alert to silence.",
                    "GridBanner Command",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        
        private void HideAlert()
        {
            if (_activeAlert != null)
            {
                _alertSilenced = true;
                _alertHidden = true;
                _alertSoundPlayer.Silence();
                HideAllAlertWindows();
                HideAllSuperCriticalWindows();
                LogMessage("Alert hidden and silenced");
                System.Windows.MessageBox.Show(
                    "Alert hidden and silenced. It will reappear if a new alert is received or the current alert is updated.",
                    "GridBanner Command",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "No active alert to hide.",
                    "GridBanner Command",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        
        private static class HotkeyNativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
            
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
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
                var alertUrl = config.GetValueOrDefault("alert_url", string.Empty).Trim();
                
                // Try to fetch GridBanner URL from server if Azure auth is enabled
                if (string.IsNullOrWhiteSpace(alertUrl))
                {
                    _ = FetchAndApplyServerSettingsAsync(alertUrl);
                }
                
                var alertServerConfigured = !string.IsNullOrWhiteSpace(alertUrl);

                // Check for permit_terminate and disable_triple_click_menu config options
                // These can be overridden by global settings from server
                var permitTerminate = ParseInt(config.GetValueOrDefault("permit_terminate", "0"), 0) == 1;
                var tripleClickMenuEnabled = ParseInt(config.GetValueOrDefault("disable_triple_click_menu", "0"), 0) == 0;
                
                // Keyring feature (optional) - can be overridden by global settings
                _keyringEnabled = ParseInt(config.GetValueOrDefault("keyring_enabled", "0"), 0) == 1;
                LogMessage($"Keyring feature: {(_keyringEnabled ? "enabled" : "disabled")}");
                
                // Tray-only mode - can be overridden by global settings
                var newTrayOnlyMode = ParseInt(config.GetValueOrDefault("tray_only", "0"), 0) == 1;
                
                // Fetch and apply global settings from server (async, won't block initialization)
                _ = FetchAndApplyGlobalSettingsAsync(permitTerminate, tripleClickMenuEnabled, _keyringEnabled, newTrayOnlyMode);
                var trayModeChanged = _trayOnlyMode != newTrayOnlyMode;
                _trayOnlyMode = newTrayOnlyMode;
                LogMessage($"Tray-only mode: {(_trayOnlyMode ? "enabled" : "disabled")}");
                
                // Setup tray icon if in tray-only mode
                if (_trayOnlyMode)
                {
                    SetupTrayIcon(backgroundColor);
                }
                else
                {
                    // Clean up tray icon if switching from tray-only to normal mode
                    CleanupTrayIcon();
                }
                
                // If switching from banner mode to tray mode, close banners first
                if (trayModeChanged && _trayOnlyMode && _activeAlert == null)
                {
                    CloseAllBanners();
                }
                
                // Only create banners if not in tray-only mode (or if we have an active alert)
                if (!_trayOnlyMode || _activeAlert != null)
                {
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
                        complianceStatus,
                        alertServerConfigured,
                        permitTerminate,
                        tripleClickMenuEnabled,
                        _keyringEnabled);
                }
                else
                {
                    // In tray-only mode with no alert, close any existing banners
                    CloseAllBanners();
                }

                // Alert overlays (optional; configured via conf.ini)
                SetupAlertSystem(config, bannerHeight, computerName, username, orgName, classificationLevel, backgroundColor, foregroundColor, complianceStatus);
                
                // Keyring setup (optional)
                if (_keyringEnabled && alertServerConfigured)
                {
                    SetupKeyringSystem(alertUrl, username);
                }

                // Clipboard monitoring and paste interception (optional)
                SetupClipboardMonitoring(config);

                // Hide the main window
                Hide();
                LogMessage("MainWindow hidden, banner windows should be visible");
                
                // Register global hotkey after window is loaded (try with a small delay to ensure handle is ready)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (_hotkeyHwndSource == null)
                        {
                            var hwnd = new WindowInteropHelper(this).Handle;
                            if (hwnd != IntPtr.Zero)
                            {
                                LogMessage($"Registering hotkey from Loaded event (delayed), handle: {hwnd}");
                                _hotkeyHwndSource = HwndSource.FromHwnd(hwnd);
                                _hotkeyHwndSource?.AddHook(HotkeyWndProc);
                                DoRegisterGlobalHotkey();
                            }
                            else
                            {
                                LogMessage("Window handle still not available after delay");
                            }
                        }
                    };
                    timer.Start();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
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
            int complianceStatus,
            bool alertServerConfigured,
            bool permitTerminate,
            bool tripleClickMenuEnabled,
            bool keyringEnabled)
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
                        ComplianceStatus = complianceStatus,
                        PermitTerminate = permitTerminate,
                        TripleClickMenuEnabled = tripleClickMenuEnabled
                    };

                    bannerWindow.SetScreen(screen);
                    
                    // Set connectivity tracking if alert server is configured
                    if (alertServerConfigured)
                    {
                        bannerWindow.AlertServerConfigured = true;
                        bannerWindow.LastServerConnection = null; // Will be updated by timer
                    }
                    
                    // Set up keyring indicator click handler
                    if (keyringEnabled)
                    {
                        bannerWindow.KeyringEnabled = true;
                        bannerWindow.KeyringIndicatorClicked += BannerWindow_KeyringIndicatorClicked;
                        bannerWindow.ManageKeysRequested += BannerWindow_ManageKeysRequested;
                    }
                    
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

        private void SetupAlertSystem(Dictionary<string, string> config, double alertBarHeight, string computerName, string username, string orgName, string classificationLevel, System.Windows.Media.Color backgroundColor, System.Windows.Media.Color foregroundColor, int complianceStatus)
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
            
            // Configure alert manager initially (system groups will be added later)
            _alertManager!.Configure(alertFile, alertUrl, TimeSpan.FromSeconds(pollSeconds), siteNames, systemInfo, null);
            _alertManager.Start();
            
            // Fetch system's Azure groups for group-based alert filtering (async, updates AlertManager later)
            if (_azureAuthManager != null && _azureAuthManager.IsEnabled)
            {
                _ = FetchAndUpdateSystemGroupsAsync();
            }
            
            // Set base URL for audio downloads
            if (!string.IsNullOrWhiteSpace(alertUrl))
            {
                _alertSoundPlayer.SetBaseUrl(_alertManager.BaseUrl);
                
                // Set up connectivity tracking for banners
                foreach (var banner in _bannerWindows)
                {
                    banner.AlertServerConfigured = true;
                    // Initialize with null so warning shows if we've never connected
                    banner.LastServerConnection = null;
                }
                
                // Update connectivity immediately
                UpdateBannerConnectivity();
                
                // Start timer to update connectivity status
                _connectivityTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _connectivityTimer.Tick += (_, __) => UpdateBannerConnectivity();
                _connectivityTimer.Start();
            }

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
            try { _connectivityTimer?.Stop(); } catch { /* ignore */ }
            _activeAlert = null;
            _dismissedAlertSignature = null;
            _closedSuperCriticalSignature = null;
            _closedSystemLockdownSignature = null;
            _alertSoundPlayer.Stop();
            HideAllAlertWindows();
            HideAllSuperCriticalWindows();
            HideAllSystemLockdownWindows();
            
            // Stop SystemLockdown monitoring and cleanup
            StopLockdownFocusMonitoring();
            _lockdownKeyboardHook?.Dispose();
            _lockdownKeyboardHook = null;
            
            // Clear connectivity status
            foreach (var banner in _bannerWindows)
            {
                try
                {
                    banner.AlertServerConfigured = false;
                    banner.LastServerConnection = null;
                }
                catch { /* ignore */ }
            }
        }

        private void UpdateBannerConnectivity()
        {
            if (_alertManager == null) return;
            
            var lastConnection = _alertManager.LastSuccessfulConnection;
            foreach (var banner in _bannerWindows)
            {
                try
                {
                    // Always update to trigger property change notifications
                    // This ensures visibility is re-evaluated based on current time
                    banner.LastServerConnection = lastConnection;
                }
                catch
                {
                    // ignore
                }
            }
        }

        // ============================================
        // Keyring Management
        // ============================================
        
        private void SetupClipboardMonitoring(Dictionary<string, string> config)
        {
            try
            {
                // Check if clipboard monitoring is enabled
                var clipboardEnabled = config.GetValueOrDefault("clipboard_monitoring_enabled", "0") == "1";
                if (!clipboardEnabled)
                {
                    LogMessage("Clipboard monitoring disabled in config");
                    return;
                }

                LogMessage("Setting up clipboard monitoring and paste interception");

                // Load sensitivity configuration
                _sensitivityConfig = SensitivityConfig.Load();

                // Initialize clipboard monitor (starts automatically in constructor)
                _clipboardMonitor = new ClipboardMonitor(this);
                _clipboardMonitor.ClipboardChanged += ClipboardMonitor_ClipboardChanged;

                // Initialize paste interceptor (starts automatically in constructor)
                _pasteInterceptor = new PasteInterceptor(this, _sensitivityConfig);
                _pasteInterceptor.PasteBlocked += PasteInterceptor_PasteBlocked;

                LogMessage($"Clipboard monitoring and paste interception initialized. PasteBlockingEnabled={_sensitivityConfig.PasteBlockingEnabled}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting up clipboard monitoring: {ex.Message}");
            }
        }

        private void ClipboardMonitor_ClipboardChanged(object? sender, ClipboardChangedEventArgs e)
        {
            try
            {
                LogMessage($"Clipboard changed from: {e.SourceApplication} ({e.SourceWindowTitle})");

                // Detect sensitivity from source
                var sensitivity = DetectClipboardSensitivity(e);
                
                // Update paste interceptor with current clipboard sensitivity
                _pasteInterceptor?.SetClipboardSensitivity(sensitivity);

                if (sensitivity != null)
                {
                    LogMessage($"Detected sensitivity level: {sensitivity.Level} from {sensitivity.Source}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error handling clipboard change: {ex.Message}");
            }
        }

        private SensitivityInfo? DetectClipboardSensitivity(ClipboardChangedEventArgs e)
        {
            try
            {
                var appName = e.SourceApplication.ToUpperInvariant();
                LogMessage($"Detecting sensitivity for source: {appName}, Window title: {e.SourceWindowTitle}");

                // Check if it's an Office application (Word, Excel, PowerPoint)
                if (appName == "WINWORD" || appName == "EXCEL" || appName == "POWERPNT")
                {
                    LogMessage($"Checking Office document sensitivity for {appName}...");
                    // Try to get sensitivity label from Office document
                    var officeSensitivity = SensitivityLabelManager.GetSensitivityFromActiveOfficeApp(
                        new WindowInteropHelper(this).Handle);
                    
                    if (officeSensitivity != null && officeSensitivity.Level != SensitivityLevel.None)
                    {
                        // Any sensitivity label found = sensitive
                        LogMessage($"✓ Office document sensitivity detected: Level={officeSensitivity.Level}, Label={officeSensitivity.LabelName}");
                        return officeSensitivity;
                    }
                    else
                    {
                        LogMessage($"✗ No sensitivity label found in Office document");
                    }
                }

                // Check if it's a browser (Chrome, Edge, Firefox)
                if (appName == "CHROME" || appName == "MSEDGE" || appName == "FIREFOX")
                {
                    LogMessage($"Checking browser URL sensitivity for {appName}...");
                    
                    // Method 1: Check window title for sensitive keywords (most reliable for modern browsers)
                    var windowTitle = e.SourceWindowTitle ?? "";
                    LogMessage($"Browser window title: {windowTitle}");
                    
                    // Check if title contains PrecisionX Ventures (sensitive company content)
                    if (windowTitle.Contains("PrecisionX Ventures", StringComparison.OrdinalIgnoreCase) ||
                        windowTitle.Contains("PrecisionX", StringComparison.OrdinalIgnoreCase))
                    {
                        LogMessage($"✓ PrecisionX Ventures detected in window title - marking as sensitive");
                        return new SensitivityInfo
                        {
                            Level = SensitivityLevel.Internal,
                            Source = "Browser",
                            LabelName = "PrecisionX Ventures",
                            Description = "PrecisionX content (detected from window title)"
                        };
                    }
                    
                    // Check if title contains SharePoint indicators
                    if (windowTitle.Contains("precisionxtech.sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
                        windowTitle.Contains("sharepoint", StringComparison.OrdinalIgnoreCase))
                    {
                        LogMessage($"✓ SharePoint detected in window title");
                        return new SensitivityInfo
                        {
                            Level = SensitivityLevel.Internal,
                            Source = "SharePoint",
                            LabelName = "precisionxtech.sharepoint.com",
                            Description = "SharePoint site (detected from window title)"
                        };
                    }
                    
                    // Method 2: Try to extract URL from title
                    var url = BrowserUrlDetector.ExtractUrlFromTitle(windowTitle);
                    LogMessage($"Extracted URL from title: {url ?? "(none)"}");
                    
                    // Method 3: Try to get from browser window directly (less reliable)
                    if (string.IsNullOrEmpty(url))
                    {
                        try
                        {
                            var process = System.Diagnostics.Process.GetProcessById(e.SourceProcessId);
                            url = BrowserUrlDetector.GetUrlFromBrowserWindow(
                                new WindowInteropHelper(this).Handle,
                                process.ProcessName);
                            LogMessage($"Extracted URL from window: {url ?? "(none)"}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error getting URL from browser window: {ex.Message}");
                        }
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        // Check if it's precisionxtech.sharepoint.com
                        if (SensitivityLabelManager.IsSharePointUrl(url))
                        {
                            LogMessage($"✓ SharePoint URL detected: {url}");
                            return new SensitivityInfo
                            {
                                Level = SensitivityLevel.Internal,
                                Source = "SharePoint",
                                LabelName = url,
                                Description = "SharePoint site"
                            };
                        }
                        
                        // Check general URL sensitivity
                        var level = SensitivityLabelManager.GetSensitivityFromUrl(url);
                        LogMessage($"URL sensitivity level: {level}");
                        if (level != SensitivityLevel.None && level != SensitivityLevel.Public)
                        {
                            LogMessage($"✓ URL marked as sensitive: {url}");
                            return new SensitivityInfo
                            {
                                Level = level,
                                Source = "Browser",
                                LabelName = url
                            };
                        }
                    }
                    else
                    {
                        LogMessage($"✗ Could not extract URL from browser - checking title for keywords");
                    }
                }

                // Default: no sensitivity detected
                LogMessage($"✗ No sensitivity detected for {appName}");
                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"Error detecting clipboard sensitivity: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private void PasteInterceptor_PasteBlocked(object? sender, PasteBlockedEventArgs e)
        {
            try
            {
                LogMessage($"Paste blocked: Source={e.SourceSensitivity.Level}, Destination={e.DestinationSensitivity.Level}");

                if (_sensitivityConfig?.ShowWarnings == true)
                {
                    // Show warning window
                    var warningWindow = new PasteWarningWindow();
                    warningWindow.SetWarningInfo(e.SourceSensitivity, e.DestinationSensitivity);
                    warningWindow.Show();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error showing paste warning: {ex.Message}");
            }
        }

        private void SetupKeyringSystem(string alertUrl, string username)
        {
            LogMessage($"Setting up keyring system for user: {username}");
            
            // Setup Azure AD authentication if configured
            var config = ConfigManager.LoadConfig();
            var azureAuthEnabled = ParseInt(config.GetValueOrDefault("azure_auth_enabled", "0"), 0) == 1;
            var azureClientId = config.GetValueOrDefault("azure_client_id", string.Empty).Trim();
            var azureTenantId = config.GetValueOrDefault("azure_tenant_id", string.Empty).Trim();
            var azureApiScope = config.GetValueOrDefault("azure_api_scope", string.Empty).Trim();
            
            if (azureAuthEnabled && !string.IsNullOrEmpty(azureClientId) && !string.IsNullOrEmpty(azureTenantId))
            {
                _azureAuthManager = new AzureAuthManager(azureClientId, azureTenantId, azureApiScope, true);
                _azureAuthManager.LogMessage += (_, message) => LogMessage($"Azure Auth: {message}");
                LogMessage("Azure AD authentication enabled");
            }
            else
            {
                LogMessage("Azure AD authentication disabled or not configured");
            }
            
            // Configure keyring manager
            _keyringManager.LogMessage += (_, message) => LogMessage($"Keyring: {message}");
            _keyringManager.Configure(alertUrl, username, true, _azureAuthManager);
            
            // Initial key check
            _ = CheckForNewKeysAsync();
            
            // Set up periodic check (every 5 minutes)
            _keyringCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _keyringCheckTimer.Tick += async (s, e) => await CheckForNewKeysAsync();
            _keyringCheckTimer.Start();
        }
        
        private async Task CheckForNewKeysAsync()
        {
            try
            {
                _pendingKeys = await _keyringManager.DetectNewLocalKeysAsync();
                
                // Fire and forget - UI update
                #pragma warning disable CS4014
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var banner in _bannerWindows)
                    {
                        try
                        {
                            banner.PendingKeyCount = _pendingKeys.Count;
                        }
                        catch { /* ignore */ }
                    }
                }));
                #pragma warning restore CS4014
                
                if (_pendingKeys.Count > 0)
                {
                    LogMessage($"Detected {_pendingKeys.Count} new SSH key(s) ready to upload");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error checking for new keys: {ex.Message}");
            }
        }
        
        private void BannerWindow_KeyringIndicatorClicked(object? sender, EventArgs e)
        {
            ShowManageKeysWindow();
        }
        
        private void BannerWindow_ManageKeysRequested(object? sender, EventArgs e)
        {
            ShowManageKeysWindow();
        }
        
        private void ShowManageKeysWindow()
        {
            try
            {
                var window = new ManageKeysWindow(_keyringManager);
                window.Closed += async (s, e) => await CheckForNewKeysAsync(); // Refresh after closing
                window.Show();
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening Manage Keys window: {ex.Message}");
            }
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

        private void CreateOrRefreshSystemLockdownWindows()
        {
            CloseAllSystemLockdownWindows();

            var screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return;
            }

            foreach (var screen in screens)
            {
                var w = new SystemLockdownAlertWindow();
                w.SetScreen(screen);
                w.OnExitLockdown = ExitSystemLockdown;
                
                // Track primary monitor window (for focus monitoring)
                if (screen.Primary && _primarySystemLockdownWindow == null)
                {
                    _primarySystemLockdownWindow = w;
                }
                
                // Monitor window state - only lock if window is actually minimized or hidden
                // Don't lock on simple deactivation (e.g., when command window opens)
                w.StateChanged += (sender, e) =>
                {
                    // Only monitor if windows have been initialized (shown at least once)
                    if (_systemLockdownWindowsInitialized && w.WindowState == WindowState.Minimized)
                    {
                        LogMessage("SystemLockdown window minimized - locking system");
                        SystemLockHelper.LockSystem();
                    }
                };
                
                // Monitor if window is actually hidden (user might have closed it via task manager or other means)
                // Only trigger if windows have been initialized (shown) - ignore initial transitions
                w.IsVisibleChanged += (sender, e) =>
                {
                    // Only check if windows have been initialized and are now hidden
                    if (_systemLockdownWindowsInitialized && !w.IsVisible && w.WindowState != WindowState.Minimized)
                    {
                        // Window was hidden but not minimized - check if it's still supposed to be visible
                        if (_activeAlert?.Level == AlertLevel.SystemLockdown && 
                            !string.Equals(_closedSystemLockdownSignature, _activeAlert.Signature, StringComparison.Ordinal))
                        {
                            LogMessage("SystemLockdown window hidden unexpectedly - locking system");
                            SystemLockHelper.LockSystem();
                        }
                    }
                };
                
                w.Hide();
                _systemLockdownWindows.Add(w);
            }

            // Install keyboard hook to block dangerous keys
            // Note: Ctrl+Alt+Shift+F1 is NOT blocked - it passes through to the command window
            if (_lockdownKeyboardHook == null)
            {
                _lockdownKeyboardHook = new SystemLockdownKeyboardHook();
                // ExitLockdownRequested is no longer used - exit is handled via command window
            }

            // Don't start focus monitoring here - it will be started after windows are shown
        }

        private void StartLockdownFocusMonitoring()
        {
            // Stop existing monitor if any
            StopLockdownFocusMonitoring();

            // Create timer to check foreground window periodically
            _lockdownFocusMonitor = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Check every 500ms
            };
            _lockdownFocusMonitor.Tick += LockdownFocusMonitor_Tick;
            _lockdownFocusMonitor.Start();
            _focusMonitoringStartTime = DateTime.Now; // Track when monitoring started
            LogMessage("SystemLockdown focus monitoring started");
        }

        private void StopLockdownFocusMonitoring()
        {
            if (_lockdownFocusMonitor != null)
            {
                _lockdownFocusMonitor.Stop();
                _lockdownFocusMonitor.Tick -= LockdownFocusMonitor_Tick;
                _lockdownFocusMonitor = null;
                _focusMonitoringStartTime = null; // Clear grace period
                LogMessage("SystemLockdown focus monitoring stopped");
            }
        }

        private void LockdownFocusMonitor_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Only monitor if we have an active SystemLockdown alert that hasn't been closed
                bool hasActiveLockdown = _activeAlert != null &&
                                        _activeAlert.Level == AlertLevel.SystemLockdown &&
                                        !string.Equals(_closedSystemLockdownSignature, _activeAlert.Signature, StringComparison.Ordinal);
                
                if (!hasActiveLockdown)
                {
                    // No active lockdown - stop monitoring to prevent false positives
                    StopLockdownFocusMonitoring();
                    return;
                }

                // Grace period: Don't lock for first 3-5 seconds after monitoring starts (allows system to stabilize)
                // If grace period was extended (negative start time), use 5 seconds
                if (_focusMonitoringStartTime.HasValue)
                {
                    var elapsed = DateTime.Now - _focusMonitoringStartTime.Value;
                    var gracePeriodSeconds = _focusMonitoringStartTime.Value < DateTime.Now.AddSeconds(-1) ? 5.0 : 3.0; // Extended if started in past
                    
                    if (elapsed.TotalSeconds < gracePeriodSeconds)
                    {
                        // Still in grace period - don't check yet
                        return;
                    }
                }

                // Collect handles of allowed windows (SystemLockdown windows and CommandWindow)
                var allowedHandles = new List<IntPtr>();
                
                if (_systemLockdownWindows != null)
                {
                    foreach (var w in _systemLockdownWindows)
                    {
                        try
                        {
                            if (w.IsVisible)
                            {
                                var helper = new WindowInteropHelper(w);
                                var handle = helper.Handle;
                                if (handle != IntPtr.Zero)
                                {
                                    allowedHandles.Add(handle);
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Add command window if open
                if (_commandWindow != null && _commandWindow.IsVisible)
                {
                    try
                    {
                        var helper = new WindowInteropHelper(_commandWindow);
                        var handle = helper.Handle;
                        if (handle != IntPtr.Zero)
                        {
                            allowedHandles.Add(handle);
                        }
                    }
                    catch { }
                }

                // Only check if we have at least one visible SystemLockdown window
                if (allowedHandles.Count == 0)
                {
                    // No visible windows yet - don't check (might be initializing)
                    return;
                }

                // Check if foreground window is allowed
                // If not, lock immediately - be aggressive about preventing escape
                if (!SystemLockdownFocusMonitor.IsForegroundWindowAllowed(allowedHandles.ToArray()))
                {
                    LogMessage("Foreground window is not allowed during SystemLockdown - locking system immediately");
                    SystemLockHelper.LockSystem();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in focus monitor: {ex.Message}");
            }
        }

        private void ExitSystemLockdown()
        {
            if (_activeAlert?.Level != AlertLevel.SystemLockdown)
            {
                return;
            }

            _closedSystemLockdownSignature = _activeAlert.Signature;
            HideAllSystemLockdownWindows();
            
            // Stop focus monitoring
            StopLockdownFocusMonitoring();
            
            // Dispose keyboard hook
            _lockdownKeyboardHook?.Dispose();
            _lockdownKeyboardHook = null;

            // Re-show the non-dismissible sub-bar immediately
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

        private void CloseAllSystemLockdownWindows()
        {
            foreach (var w in _systemLockdownWindows.ToList())
            {
                try { w.Close(); } catch { /* ignore */ }
            }
            _systemLockdownWindows.Clear();
            _primarySystemLockdownWindow = null;
            _systemLockdownWindowsInitialized = false;
        }

        private void HideAllSystemLockdownWindows()
        {
            foreach (var w in _systemLockdownWindows)
            {
                try { w.Hide(); } catch { /* ignore */ }
            }
        }

        private void ApplyAlert(AlertMessage? alert)
        {
            // If alert is cleared, reset silenced/hidden flags
            if (alert == null)
            {
                _alertSilenced = false;
                _alertHidden = false;
                _activeAlert = null;
                _dismissedAlertSignature = null;
                _closedSuperCriticalSignature = null;
                _closedSystemLockdownSignature = null;
                _alertSoundPlayer.Update(null, dismissed: false);
                HideAllAlertWindows();
                HideAllSuperCriticalWindows();
                HideAllSystemLockdownWindows();
                
                // Stop SystemLockdown monitoring and cleanup
                StopLockdownFocusMonitoring();
                _lockdownKeyboardHook?.Dispose();
                _lockdownKeyboardHook = null;
                
                // In tray-only mode, hide banners when alert is cleared
                if (_trayOnlyMode)
                {
                    CloseAllBanners();
                }
                return;
            }
            
            // Check if this is a new alert (different signature)
            var isNewAlert = _activeAlert == null || !string.Equals(_activeAlert.Signature, alert.Signature, StringComparison.Ordinal);
            
            // If alert is hidden and it's the same alert, don't show it
            if (_alertHidden && !isNewAlert)
            {
                // Same alert that's hidden - keep it hidden, but update the active alert reference
                _activeAlert = alert;
                // Keep audio silenced if it was silenced
                if (_alertSilenced)
                {
                    _alertSoundPlayer.Silence();
                }
                return;
            }
            
            // New alert or alert was unhidden - reset flags
            if (isNewAlert)
            {
                _alertSilenced = false;
                _alertHidden = false;
            }
            
            _activeAlert = alert;
            
            // In tray-only mode, show banners when alert is active
            if (_trayOnlyMode && _bannerWindows.Count == 0)
            {
                var config = ConfigManager.LoadConfig();
                var userInfo = UserInfoHelper.GetUserInfo();
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
                var computerName = Environment.MachineName;
                var username = userInfo.GetValueOrDefault("username", Environment.UserName);
                var orgOverride = config.GetValueOrDefault("org_name", string.Empty);
                var orgName = !string.IsNullOrWhiteSpace(orgOverride)
                    ? orgOverride.Trim()
                    : userInfo.GetValueOrDefault("org_name", "ORGANIZATION");
                var siteNames = config.GetValueOrDefault("site_name", string.Empty).Trim();
                var complianceEnabled = ParseInt(config.GetValueOrDefault("compliance_check_enabled", "1"), 1) == 1;
                var complianceStatusFallback = ParseInt(config.GetValueOrDefault("compliance_status", "0"), 0);
                complianceStatusFallback = complianceStatusFallback == 1 ? 1 : 0;
                var complianceCommand = config.GetValueOrDefault("compliance_check_command", string.Empty).Trim();
                var complianceStatus = 0;
                if (!complianceEnabled)
                {
                    // Compliance disabled
                }
                else if (!string.IsNullOrWhiteSpace(complianceCommand))
                {
                    if (TryRunComplianceCommand(complianceCommand, out var commandCompliant, out var details))
                    {
                        complianceStatus = commandCompliant ? 1 : 0;
                    }
                    else
                    {
                        complianceStatus = 0;
                    }
                }
                else
                {
                    complianceStatus = complianceStatusFallback;
                }
                var alertUrl = config.GetValueOrDefault("alert_url", string.Empty).Trim();
                var alertServerConfigured = !string.IsNullOrWhiteSpace(alertUrl);
                var permitTerminate = ParseInt(config.GetValueOrDefault("permit_terminate", "0"), 0) == 1;
                var tripleClickMenuEnabled = ParseInt(config.GetValueOrDefault("disable_triple_click_menu", "0"), 0) == 0;
                
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
                    complianceStatus,
                    alertServerConfigured,
                    permitTerminate,
                    tripleClickMenuEnabled,
                    _keyringEnabled);
            }

            var isDismissable = alert.Level == AlertLevel.Routine || alert.Level == AlertLevel.Urgent;
            var dismissed = isDismissable && string.Equals(_dismissedAlertSignature, alert.Signature, StringComparison.Ordinal);

            var bgBrush = new SolidColorBrush(ParseColor(alert.BackgroundColor));
            var fgBrush = new SolidColorBrush(ParseColor(alert.ForegroundColor));

            // System Lockdown: show full-screen overlay with keyboard blocking, no acknowledge button
            if (alert.Level == AlertLevel.SystemLockdown)
            {
                if (_systemLockdownWindows.Count == 0)
                {
                    CreateOrRefreshSystemLockdownWindows();
                }

                var overlayClosedLocally = string.Equals(_closedSystemLockdownSignature, alert.Signature, StringComparison.Ordinal);
                if (!overlayClosedLocally)
                {
                    // Reset initialization flag
                    _systemLockdownWindowsInitialized = false;
                    
                    // First, show all windows
                    foreach (var w in _systemLockdownWindows)
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
                    
                    // Focus the primary window first - use the tracked primary window, not just first window
                    if (_primarySystemLockdownWindow != null)
                    {
                        try
                        {
                            _primarySystemLockdownWindow.Activate();
                            _primarySystemLockdownWindow.Focus();
                            LogMessage("Focused primary SystemLockdown window");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error focusing primary window: {ex.Message}");
                        }
                    }
                    else if (_systemLockdownWindows.Count > 0)
                    {
                        // Fallback to first window if primary not found
                        try
                        {
                            var primaryWindow = _systemLockdownWindows[0];
                            primaryWindow.Activate();
                            primaryWindow.Focus();
                            LogMessage("Focused first SystemLockdown window (primary not found)");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error focusing first window: {ex.Message}");
                        }
                    }
                    
                    // Mark windows as initialized after a short delay
                    var initTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500) // Short delay to let windows render
                    };
                    initTimer.Tick += (sender, e) =>
                    {
                        initTimer.Stop();
                        _systemLockdownWindowsInitialized = true;
                        LogMessage("SystemLockdown windows marked as initialized");
                    };
                    initTimer.Start();
                    
                    // Start focus monitoring AFTER windows are shown, focused, and given time to render
                    // Normal startup delay (unlock restoration is handled separately)
                    var delayTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2.0) // Normal delay for initial startup
                    };
                    delayTimer.Tick += (sender, e) =>
                    {
                        delayTimer.Stop();
                        
                        // Verify primary window is visible before starting monitoring
                        if (_primarySystemLockdownWindow != null && _primarySystemLockdownWindow.IsVisible)
                        {
                            try
                            {
                                // CRITICAL: Focus the primary window multiple times to ensure it gets focus
                                // This must happen at the END of the initialization sequence
                                for (int i = 0; i < 3; i++)
                                {
                                    _primarySystemLockdownWindow.Activate();
                                    _primarySystemLockdownWindow.Focus();
                                    System.Windows.Forms.Application.DoEvents(); // Process window messages
                                    System.Threading.Thread.Sleep(150); // Small delay between attempts
                                }
                                
                                // Wait a bit more to ensure focus is fully established
                                var focusWaitTimer = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromMilliseconds(500)
                                };
                                focusWaitTimer.Tick += (s, args) =>
                                {
                                    focusWaitTimer.Stop();
                                    
                                    // One final focus attempt right before starting monitoring
                                    try
                                    {
                                        _primarySystemLockdownWindow.Activate();
                                        _primarySystemLockdownWindow.Focus();
                                        
                                        // CRITICAL: Verify the primary window actually has focus before starting monitoring
                                        var primaryHandle = new System.Windows.Interop.WindowInteropHelper(_primarySystemLockdownWindow).Handle;
                                        var foregroundWindow = SystemLockdownFocusMonitor.GetForegroundWindow();
                                        
                                        if (foregroundWindow == primaryHandle)
                                        {
                                            LogMessage("Primary window has focus - starting SystemLockdown focus monitoring");
                                            _focusMonitoringStartTime = DateTime.Now; // Set grace period start
                                            StartLockdownFocusMonitoring();
                                        }
                                        else
                                        {
                                            LogMessage($"Primary window does not have focus (foreground: {foregroundWindow}, primary: {primaryHandle}) - retrying focus");
                                            
                                            // Try focusing one more time and wait
                                            var retryTimer = new System.Windows.Threading.DispatcherTimer
                                            {
                                                Interval = TimeSpan.FromMilliseconds(500)
                                            };
                                            retryTimer.Tick += (s2, args2) =>
                                            {
                                                retryTimer.Stop();
                                                
                                                _primarySystemLockdownWindow.Activate();
                                                _primarySystemLockdownWindow.Focus();
                                                
                                                // Check again
                                                var newForeground = SystemLockdownFocusMonitor.GetForegroundWindow();
                                                var newPrimaryHandle = new System.Windows.Interop.WindowInteropHelper(_primarySystemLockdownWindow).Handle;
                                                
                                                if (newForeground == newPrimaryHandle)
                                                {
                                                    LogMessage("Primary window now has focus - starting SystemLockdown focus monitoring");
                                                    _focusMonitoringStartTime = DateTime.Now;
                                                    StartLockdownFocusMonitoring();
                                                }
                                                else
                                                {
                                                    LogMessage($"Primary window still doesn't have focus - starting monitoring with extended grace period anyway");
                                                    // Start with extended grace period (5 seconds instead of 3)
                                                    _focusMonitoringStartTime = DateTime.Now.AddSeconds(-2); // Start grace period 2 seconds ago (effectively 5 seconds total)
                                                    StartLockdownFocusMonitoring();
                                                }
                                            };
                                            retryTimer.Start();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogMessage($"Error in final focus attempt: {ex.Message}");
                                        // Start monitoring with extended grace period
                                        _focusMonitoringStartTime = DateTime.Now.AddSeconds(-2);
                                        StartLockdownFocusMonitoring();
                                    }
                                };
                                focusWaitTimer.Start();
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error starting focus monitoring: {ex.Message}");
                            }
                        }
                        else
                        {
                            LogMessage("Primary SystemLockdown window not visible - skipping focus monitoring start");
                        }
                    };
                    delayTimer.Start();
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
                if (!_alertSilenced)
                {
                    _alertSoundPlayer.Update(alert, dismissed: false);
                }
                else
                {
                    _alertSoundPlayer.Silence();
                }
                return;
            }

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
            if (!_alertSilenced)
            {
                _alertSoundPlayer.Update(alert, dismissed: false);
            }
            else
            {
                _alertSoundPlayer.Silence();
            }
            return;
            }

            if (dismissed)
            {
                _alertSoundPlayer.Update(alert, dismissed: true);
                HideAllAlertWindows();
                HideAllSuperCriticalWindows();
                HideAllSystemLockdownWindows();
                StopLockdownFocusMonitoring();
                _lockdownKeyboardHook?.Dispose();
                _lockdownKeyboardHook = null;
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

            if (!_alertSilenced)
            {
                _alertSoundPlayer.Update(alert, dismissed: false);
            }
            else
            {
                _alertSoundPlayer.Silence();
            }
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
            
            // In tray-only mode, close banners when alert is dismissed
            if (_trayOnlyMode)
            {
                CloseAllBanners();
            }
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
                    // Hide alert windows but DON'T clear the alert state
                    HideAllAlertWindows();
                    HideAllSuperCriticalWindows();
                    HideAllSystemLockdownWindows();
                    // Stop monitoring but keep alert active
                    StopLockdownFocusMonitoring();
                    _lockdownKeyboardHook?.Dispose();
                    _lockdownKeyboardHook = null;
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    _isSessionLocked = false;
                    LogMessage("Session unlocked: starting restoration sequence.");
                    
                    // Restore alert if it was active before lock
                    if (_activeAlert != null && _activeAlert.Level == AlertLevel.SystemLockdown)
                    {
                        LogMessage($"Restoring SystemLockdown alert after unlock: {_activeAlert.Summary}");
                        
                        // Step 1: IMMEDIATELY show SystemLockdown windows on all monitors (don't wait for banners)
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Reset initialization state for fresh start
                            _systemLockdownWindowsInitialized = false;
                            
                            // Ensure windows exist
                            if (_systemLockdownWindows.Count == 0)
                            {
                                CreateOrRefreshSystemLockdownWindows();
                            }
                            
                            // Show all SystemLockdown windows immediately
                            var bgBrush = new SolidColorBrush(ParseColor(_activeAlert.BackgroundColor));
                            var fgBrush = new SolidColorBrush(ParseColor(_activeAlert.ForegroundColor));
                            
                            foreach (var w in _systemLockdownWindows)
                            {
                                try
                                {
                                    w.Apply(_activeAlert, bgBrush, fgBrush);
                                    if (!w.IsVisible)
                                    {
                                        w.Show();
                                    }
                                    w.Topmost = true;
                                }
                                catch { }
                            }
                            
                            HideAllAlertWindows();
                            LogMessage("SystemLockdown windows shown immediately after unlock");
                            
                            // Step 2: Now load banners (if not in tray-only mode)
                            if (!_trayOnlyMode)
                            {
                                MainWindow_Loaded(this, new RoutedEventArgs());
                            }
                            
                            // Step 3: After banners load, focus primary and start monitoring
                            var bannerWaitTimer = new System.Windows.Threading.DispatcherTimer
                            {
                                Interval = TimeSpan.FromSeconds(3.0) // Wait for all 9 banners to load
                            };
                            bannerWaitTimer.Tick += (sender, e) =>
                            {
                                bannerWaitTimer.Stop();
                                LogMessage("Banners loaded, focusing primary window and starting monitoring");
                                
                                // Focus primary window
                                if (_primarySystemLockdownWindow != null)
                                {
                                    try
                                    {
                                        _primarySystemLockdownWindow.Activate();
                                        _primarySystemLockdownWindow.Focus();
                                        LogMessage("Focused primary SystemLockdown window after unlock");
                                    }
                                    catch (Exception ex)
                                    {
                                        LogMessage($"Error focusing primary window: {ex.Message}");
                                    }
                                }
                                
                                // Mark windows as initialized
                                var initTimer = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromMilliseconds(500)
                                };
                                initTimer.Tick += (s, args) =>
                                {
                                    initTimer.Stop();
                                    _systemLockdownWindowsInitialized = true;
                                    LogMessage("SystemLockdown windows marked as initialized after unlock");
                                };
                                initTimer.Start();
                                
                                // Start focus monitoring after additional delay
                                var monitoringTimer = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromSeconds(2.0) // Additional delay for system to stabilize
                                };
                                monitoringTimer.Tick += (s, args) =>
                                {
                                    monitoringTimer.Stop();
                                    LogMessage("Starting SystemLockdown focus monitoring after unlock");
                                    _focusMonitoringStartTime = DateTime.Now; // Set grace period start
                                    StartLockdownFocusMonitoring();
                                };
                                monitoringTimer.Start();
                            };
                            bannerWaitTimer.Start();
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                    else
                    {
                        // For non-SystemLockdown alerts, just reload normally
                        LogMessage("Session unlocked: recreating banners.");
                        MainWindow_Loaded(this, new RoutedEventArgs());
                        
                        if (_activeAlert != null)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                ApplyAlert(_activeAlert);
                            }), System.Windows.Threading.DispatcherPriority.Loaded);
                        }
                    }
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

        private void SetupTrayIcon(System.Windows.Media.Color backgroundColor)
        {
            try
            {
                // Clean up existing tray icon if any
                CleanupTrayIcon();
                
                // Create a simple colored icon from the background color
                var icon = CreateColoredIcon(backgroundColor);
                
                _trayIcon = new NotifyIcon
                {
                    Icon = icon,
                    Text = "GridBanner",
                    Visible = true
                };
                
                // Try to keep the tray icon visible (not in hidden area)
                // Note: Windows manages this automatically, but we can try to prevent hiding
                try
                {
                    // Show a balloon tip briefly to ensure icon is visible
                    _trayIcon.ShowBalloonTip(100, "GridBanner", "Running in system tray", ToolTipIcon.Info);
                }
                catch { /* ignore */ }
                
                // Create context menu
                var contextMenu = new ContextMenuStrip();
                
                // Reload Config
                var reloadConfigItem = new ToolStripMenuItem("Reload Config");
                reloadConfigItem.Click += (s, e) => ReloadConfig();
                contextMenu.Items.Add(reloadConfigItem);
                
                contextMenu.Items.Add(new ToolStripSeparator());
                
                // View Logs
                var logsItem = new ToolStripMenuItem("View Logs");
                logsItem.Click += (s, e) => ShowLogs();
                contextMenu.Items.Add(logsItem);
                
                // Manage Keys (if enabled)
                if (_keyringEnabled)
                {
                    var manageKeysItem = new ToolStripMenuItem("Manage Keys");
                    manageKeysItem.Click += (s, e) => ShowManageKeysWindow();
                    contextMenu.Items.Add(manageKeysItem);
                }
                
                contextMenu.Items.Add(new ToolStripSeparator());
                
                // About
                var aboutItem = new ToolStripMenuItem("About");
                aboutItem.Click += (s, e) => ShowAbout();
                contextMenu.Items.Add(aboutItem);
                
                // View License
                var licenseItem = new ToolStripMenuItem("View License");
                licenseItem.Click += (s, e) => ShowLicense();
                contextMenu.Items.Add(licenseItem);
                
                // GitHub Project
                var githubItem = new ToolStripMenuItem("GitHub Project");
                githubItem.Click += (s, e) => OpenGitHub();
                contextMenu.Items.Add(githubItem);
                
                contextMenu.Items.Add(new ToolStripSeparator());
                
                // Terminate (if permitted)
                var config = ConfigManager.LoadConfig();
                var permitTerminate = ParseInt(config.GetValueOrDefault("permit_terminate", "0"), 0) == 1;
                if (permitTerminate)
                {
                    var terminateItem = new ToolStripMenuItem("Terminate");
                    terminateItem.Click += (s, e) =>
                    {
                        if (System.Windows.MessageBox.Show(
                            "Are you sure you want to terminate GridBanner?",
                            "Confirm Termination",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
                        {
                            System.Windows.Application.Current.Shutdown();
                        }
                    };
                    contextMenu.Items.Add(terminateItem);
                }
                
                _trayIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting up tray icon: {ex.Message}");
            }
        }
        
        private void CleanupTrayIcon()
        {
            if (_trayIcon != null)
            {
                try
                {
                    _trayIcon.Visible = false;
                    // Dispose of the icon if it exists
                    if (_trayIcon.Icon != null)
                    {
                        _trayIcon.Icon.Dispose();
                    }
                    _trayIcon.Dispose();
                }
                catch { /* ignore */ }
                _trayIcon = null;
            }
        }
        
        private System.Drawing.Icon CreateColoredIcon(System.Windows.Media.Color color)
        {
            // Create a 16x16 bitmap with the background color
            using var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Convert WPF color to GDI+ color
            var gdiColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            graphics.Clear(gdiColor);
            
            // Add a subtle border for visibility
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(128, 0, 0, 0), 1);
            graphics.DrawRectangle(pen, 0, 0, 15, 15);
            
            // Convert to icon - we need to clone it because GetHicon() creates an unmanaged handle
            // that will be disposed when the bitmap is disposed
            var hIcon = bitmap.GetHicon();
            var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
            TrayIconNativeMethods.DestroyIcon(hIcon); // Clean up the handle
            return icon;
        }
        
        private static class TrayIconNativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }
        
        public void ReloadConfig()
        {
            try
            {
                LogMessage("Reloading configuration...");
                // Trigger a reload by calling MainWindow_Loaded
                MainWindow_Loaded(this, new RoutedEventArgs());
                System.Windows.MessageBox.Show("Configuration reloaded successfully.", "Config Reloaded", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Error reloading config: {ex.Message}");
                System.Windows.MessageBox.Show($"Failed to reload configuration:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        
        private void ShowLogs()
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
                    System.Windows.MessageBox.Show(
                        $"Failed to open log file:\n{ex.Message}",
                        "Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Log file not found:\n{logPath}",
                    "Log File Not Found",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        
        private void ShowAbout()
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
                              $"  Tray Only Mode: {GetConfigValue("tray_only")}\n" +
                              $"  Alert File Location: {GetConfigValue("alert_file_location")}\n" +
                              $"  Alert URL: {GetConfigValue("alert_url")}\n" +
                              $"  Alert Poll Seconds: {GetConfigValue("alert_poll_seconds")}";

            System.Windows.MessageBox.Show(
                aboutMessage,
                "About GridBanner",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        
        private void ShowLicense()
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

            System.Windows.MessageBox.Show(
                licenseText,
                "MIT License",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        
        private void OpenGitHub()
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
                System.Windows.MessageBox.Show(
                    $"Failed to open GitHub page:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Fetch GridBanner URL and global settings from the server and apply them.
        /// </summary>
        private async Task FetchAndApplyServerSettingsAsync(string? currentAlertUrl)
        {
            try
            {
                // Only fetch if we have Azure auth configured
                if (_azureAuthManager == null || !_azureAuthManager.IsEnabled)
                {
                    return;
                }
                
                // Try to get a base URL from the current alert URL, or use a discovery endpoint
                string? baseUrl = null;
                if (!string.IsNullOrWhiteSpace(currentAlertUrl) && Uri.TryCreate(currentAlertUrl, UriKind.Absolute, out var uri))
                {
                    baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                }
                
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    // Try to discover the server URL
                    var config = ConfigManager.LoadConfig();
                    var azureTenantId = config.GetValueOrDefault("azure_tenant_id", string.Empty).Trim();
                    var resolver = new AlertServerUrlResolver(_azureAuthManager, azureTenantId);
                    resolver.LogMessage += (_, msg) => LogMessage($"URL Resolver: {msg}");
                    
                    // Try to fetch GridBanner URL from a known discovery endpoint
                    // For now, we'll need the user to configure an initial alert_url
                    // In the future, this could use Microsoft Graph API
                    return;
                }
                
                // Fetch GridBanner URL
                var urlResolver = new AlertServerUrlResolver(_azureAuthManager, null);
                urlResolver.LogMessage += (_, msg) => LogMessage($"URL Resolver: {msg}");
                var gridbannerUrl = await urlResolver.FetchGridBannerUrlFromServerAsync(baseUrl);
                
                if (!string.IsNullOrWhiteSpace(gridbannerUrl))
                {
                    LogMessage($"Fetched GridBanner URL from server: {gridbannerUrl}");
                    // Note: We don't automatically update the config, but we could use this URL
                    // for future connections. For now, the user still needs to configure alert_url.
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error fetching server settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Fetch global settings from the server and apply them.
        /// </summary>
        private async Task FetchAndApplyGlobalSettingsAsync(bool currentPermitTerminate, bool currentTripleClick, bool currentKeyring, bool currentTrayOnly)
        {
            try
            {
                // Only fetch if we have Azure auth and an alert URL configured
                if (_azureAuthManager == null || !_azureAuthManager.IsEnabled || _alertManager == null)
                {
                    return;
                }
                
                var baseUrl = _alertManager.BaseUrl;
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return;
                }
                
                var fetcher = new GlobalSettingsFetcher(_azureAuthManager, baseUrl);
                fetcher.LogMessage += (_, msg) => LogMessage($"Global Settings: {msg}");
                var settings = await fetcher.FetchGlobalSettingsAsync();
                
                if (settings != null)
                {
                    // Apply global settings (they override local config)
                    Dispatcher.Invoke(() =>
                    {
                        if (settings.TripleClickEnabled.HasValue)
                        {
                            // Update triple-click menu setting
                            // This would need to be stored and applied when creating banners
                            LogMessage($"Global setting: triple_click_enabled = {settings.TripleClickEnabled.Value}");
                        }
                        
                        if (settings.TerminateEnabled.HasValue)
                        {
                            // Update terminate setting
                            LogMessage($"Global setting: terminate_enabled = {settings.TerminateEnabled.Value}");
                        }
                        
                        if (settings.KeyringEnabled.HasValue)
                        {
                            // Update keyring setting
                            _keyringEnabled = settings.KeyringEnabled.Value;
                            LogMessage($"Global setting: keyring_enabled = {settings.KeyringEnabled.Value}");
                        }
                        
                        if (settings.TrayOnlyMode.HasValue)
                        {
                            // Update tray-only mode
                            var newTrayOnly = settings.TrayOnlyMode.Value;
                            if (_trayOnlyMode != newTrayOnly)
                            {
                                _trayOnlyMode = newTrayOnly;
                                if (_trayOnlyMode)
                                {
                                    SetupTrayIcon(ParseColor(ConfigManager.LoadConfig().GetValueOrDefault("background_color", "#FFA500")));
                                }
                                else
                                {
                                    CleanupTrayIcon();
                                }
                                LogMessage($"Global setting: tray_only_mode = {settings.TrayOnlyMode.Value}");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error fetching global settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Fetch system's Azure/Entra groups and update AlertManager for group-based alert filtering.
        /// </summary>
        private async Task FetchAndUpdateSystemGroupsAsync()
        {
            try
            {
                if (_azureAuthManager == null || !_azureAuthManager.IsEnabled || _alertManager == null)
                {
                    return;
                }
                
                var baseUrl = _alertManager.BaseUrl;
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return;
                }
                
                // Get current user's email/UPN
                var userEmail = _azureAuthManager.GetUserPrincipalName();
                if (string.IsNullOrWhiteSpace(userEmail))
                {
                    return;
                }
                
                // Fetch user's groups from the server
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var token = _azureAuthManager.GetAccessToken();
                if (string.IsNullOrEmpty(token))
                {
                    return;
                }
                
                var url = $"{baseUrl}/api/users/{Uri.EscapeDataString(userEmail)}/groups";
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                
                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<SystemGroupsResponse>(json, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (result?.Groups != null && _alertManager != null)
                    {
                        var groupIds = result.Groups.Select(g => g.Id).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToList();
                        if (groupIds.Count > 0)
                        {
                            _alertManager.UpdateSystemGroups(groupIds);
                            LogMessage($"Updated AlertManager with {groupIds.Count} Azure/Entra group(s) for group-based alert filtering");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error fetching system groups: {ex.Message}");
            }
        }
        
        private class SystemGroupsResponse
        {
            public List<GroupInfo>? Groups { get; set; }
        }
        
        private class GroupInfo
        {
            public string? Id { get; set; }
            public string? DisplayName { get; set; }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            // Unregister global hotkeys
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    HotkeyNativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID);
                    HotkeyNativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID_COMMAND);
                    LogMessage("Global hotkeys unregistered");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error unregistering global hotkeys: {ex.Message}");
            }
            
            try
            {
                _hotkeyHwndSource?.RemoveHook(HotkeyWndProc);
                _hotkeyHwndSource = null;
            }
            catch { /* ignore */ }
            
            // Close all banner windows when main window closes
            CloseAllBanners();
            CloseAllAlertWindows();
            CloseAllSuperCriticalWindows();
            try { _alertManager?.Dispose(); } catch { /* ignore */ }
            _alertManager = null;
            try { _keyringCheckTimer?.Stop(); } catch { /* ignore */ }
            _keyringCheckTimer = null;
            try { _clipboardMonitor?.Dispose(); } catch { /* ignore */ }
            _clipboardMonitor = null;
            try { _pasteInterceptor?.Dispose(); } catch { /* ignore */ }
            _pasteInterceptor = null;
            try { _lockdownKeyboardHook?.Dispose(); } catch { /* ignore */ }
            _lockdownKeyboardHook = null;
            try { StopLockdownFocusMonitoring(); } catch { /* ignore */ }
            CleanupTrayIcon();

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

