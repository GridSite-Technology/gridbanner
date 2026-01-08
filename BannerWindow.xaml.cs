using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace GridBanner
{
    public partial class BannerWindow : Window, INotifyPropertyChanged
    {
        public string ComputerName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string OrgName { get; set; } = string.Empty;
        public string SiteNames { get; set; } = string.Empty;  // Comma-separated
        public string ClassificationLevel { get; set; } = string.Empty;
        public Brush BackgroundColor { get; set; } = Brushes.Navy;
        public Brush ForegroundColor { get; set; } = Brushes.White;
        public double BannerHeight { get; set; } = 60;

        // Computed properties for site display
        public string DisplayOrgText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SiteNames))
                {
                    return OrgName;  // No sites, just show org name
                }

                var sites = SiteNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (sites.Count == 0)
                {
                    return OrgName;
                }

                // First site: "Company - Location"
                var firstSite = sites[0];
                if (sites.Count == 1)
                {
                    return $"{OrgName} - {firstSite}";
                }

                // Multiple sites: "Company - FirstLocation" + badge
                return $"{OrgName} - {firstSite}";
            }
        }

        public Visibility SiteBadgeVisibility
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SiteNames))
                {
                    return Visibility.Collapsed;
                }

                var sites = SiteNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                return sites.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public string SiteBadgeText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SiteNames))
                {
                    return string.Empty;
                }

                var sites = SiteNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (sites.Count <= 1)
                {
                    return string.Empty;
                }

                return $"+{sites.Count - 1}";
            }
        }

        public string SiteTooltipText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SiteNames))
                {
                    return string.Empty;
                }

                var sites = SiteNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (sites.Count <= 1)
                {
                    return string.Empty;
                }

                // Format: Primary Site: [first], then Additional Site(s): [rest]
                var primary = sites[0];
                var additional = sites.Skip(1).ToList();
                
                var tooltip = $"Primary Site:{Environment.NewLine}{primary}";
                
                if (additional.Count > 0)
                {
                    tooltip += $"{Environment.NewLine}{Environment.NewLine}Additional Site(s):{Environment.NewLine}";
                    tooltip += string.Join(Environment.NewLine, additional);
                }
                
                return tooltip;
            }
        }

        public bool ComplianceEnabled { get; set; } = true;
        public int ComplianceStatus { get; set; } = 1; // 1=compliant, 0=non-compliant
        public Visibility ComplianceVisibility => ComplianceEnabled ? Visibility.Visible : Visibility.Collapsed;
        public string ComplianceText => ComplianceStatus == 1 ? "DEVICE COMPLIANT" : "DEVICE NOT COMPLIANT";
        public Brush ComplianceBackground => ComplianceStatus == 1 ? Brushes.ForestGreen : Brushes.Firebrick;

        // Connectivity warning
        private DateTime? _lastServerConnection;
        private bool _alertServerConfigured = false;
        
        public DateTime? LastServerConnection
        {
            get => _lastServerConnection;
            set
            {
                if (_lastServerConnection != value)
                {
                    _lastServerConnection = value;
                    OnPropertyChanged(nameof(LastServerConnection));
                    OnPropertyChanged(nameof(ConnectivityWarningVisibility));
                    OnPropertyChanged(nameof(ConnectivityWarningTooltip));
                }
            }
        }
        
        public bool AlertServerConfigured
        {
            get => _alertServerConfigured;
            set
            {
                if (_alertServerConfigured != value)
                {
                    _alertServerConfigured = value;
                    OnPropertyChanged(nameof(AlertServerConfigured));
                    OnPropertyChanged(nameof(ConnectivityWarningVisibility));
                }
            }
        }
        
        public Visibility ConnectivityWarningVisibility
        {
            get
            {
                if (!AlertServerConfigured || LastServerConnection == null)
                {
                    return Visibility.Collapsed;
                }
                
                // Show warning if last connection was more than 30 seconds ago
                var timeSince = DateTime.UtcNow - LastServerConnection.Value;
                return timeSince.TotalSeconds > 30 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        public string ConnectivityWarningTooltip
        {
            get
            {
                if (LastServerConnection == null)
                {
                    return "No connection to alert server";
                }
                
                var timeSince = DateTime.UtcNow - LastServerConnection.Value;
                var minutes = (int)timeSince.TotalMinutes;
                var seconds = (int)(timeSince.TotalSeconds % 60);
                
                if (minutes > 0)
                {
                    return $"Last connected to alert server: {minutes} minute{(minutes != 1 ? "s" : "")} and {seconds} second{(seconds != 1 ? "s" : "")} ago";
                }
                else
                {
                    return $"Last connected to alert server: {seconds} second{(seconds != 1 ? "s" : "")} ago";
                }
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private Screen? _screen;
        private bool _appBarRegistered;
        private int _appBarCallbackMessage;
        private HwndSource? _hwndSource;

        public BannerWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void SetScreen(Screen screen)
        {
            _screen = screen;

            // Set window position and size to match the screen width, but only a small height at the top
            WindowStartupLocation = WindowStartupLocation.Manual;
            
            // Set background color
            Background = BackgroundColor;

            // We'll register as an AppBar (reserved space) so other windows don't overlap it.
            // That also avoids the "overlaying the title bar / nav bars" problem.
            Topmost = false;
            Visibility = Visibility.Visible;
            ShowInTaskbar = false;

            if (_hwndSource != null)
            {
                RegisterOrUpdateAppBar();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Make window stay on top and remove from taskbar
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                extendedStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                extendedStyle &= ~NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle);
            }

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);

            RegisterOrUpdateAppBar();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                UnregisterAppBar();
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
            }
            catch
            {
                // ignore
            }

            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_appBarRegistered && msg == _appBarCallbackMessage)
            {
                // ABN_POSCHANGED (1): re-apply our position when the system work area changes
                if (wParam.ToInt32() == NativeMethods.ABN_POSCHANGED)
                {
                    SetAppBarPosition();
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            return IntPtr.Zero;
        }

        private void RegisterOrUpdateAppBar()
        {
            if (_screen == null)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!_appBarRegistered)
            {
                _appBarCallbackMessage = NativeMethods.RegisterWindowMessage($"GridBannerAppBar_{hwnd}");

                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = hwnd,
                    uCallbackMessage = (uint)_appBarCallbackMessage
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
                _appBarRegistered = true;
            }

            SetAppBarPosition();
        }

        private void UnregisterAppBar()
        {
            if (!_appBarRegistered)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                hWnd = hwnd
            };

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
            _appBarRegistered = false;
        }

        private void SetAppBarPosition()
        {
            if (_screen == null)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var h = (int)Math.Round(BannerHeight <= 0 ? 30 : BannerHeight);
            if (h < 20) h = 20;
            if (h > 300) h = 300;

            var bounds = _screen.Bounds;

            try
            {
                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = hwnd,
                    uEdge = NativeMethods.ABE_TOP,
                    rc = new NativeMethods.RECT
                    {
                        left = bounds.Left,
                        top = bounds.Top,
                        right = bounds.Right,
                        bottom = bounds.Top + h
                    }
                };

                // Let Windows adjust for other appbars, then lock our desired height.
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
                abd.rc.bottom = abd.rc.top + h;
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

                // Defensive: prevent invalid negative sizes from ever reaching WPF.
                var w = abd.rc.right - abd.rc.left;
                var hh = abd.rc.bottom - abd.rc.top;
                if (w <= 0 || hh <= 0)
                {
                    // Fallback to the raw screen bounds if the shell returned a bad rect.
                    abd.rc.left = bounds.Left;
                    abd.rc.top = bounds.Top;
                    abd.rc.right = bounds.Right;
                    abd.rc.bottom = bounds.Top + h;
                    w = abd.rc.right - abd.rc.left;
                    hh = abd.rc.bottom - abd.rc.top;
                }

                Left = abd.rc.left;
                Top = abd.rc.top;
                Width = Math.Max(1, w);
                Height = Math.Max(1, hh);
            }
            catch (Exception ex)
            {
                // Never crash on session lock/unlock / work-area changes.
                System.Diagnostics.Debug.WriteLine($"AppBar position error: {ex.Message}");
            }
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        // AppBar constants
        public const int ABM_NEW = 0x00000000;
        public const int ABM_REMOVE = 0x00000001;
        public const int ABM_QUERYPOS = 0x00000002;
        public const int ABM_SETPOS = 0x00000003;

        public const int ABN_POSCHANGED = 0x00000001;

        public const uint ABE_TOP = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }
    }
}

