using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace GridBanner
{
    public partial class BannerWindow : Window
    {
        public string Username { get; set; } = string.Empty;
        public string OrgName { get; set; } = string.Empty;
        public string ClassificationLevel { get; set; } = string.Empty;
        public Brush BackgroundColor { get; set; } = Brushes.Navy;
        public Brush ForegroundColor { get; set; } = Brushes.White;
        public double BannerHeight { get; set; } = 60;

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
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
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
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
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

            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
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

            Left = abd.rc.left;
            Top = abd.rc.top;
            Width = abd.rc.right - abd.rc.left;
            Height = abd.rc.bottom - abd.rc.top;
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
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }
    }
}

