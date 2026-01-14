using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GridBanner
{
    /// <summary>
    /// Intercepts paste events and blocks them if sensitivity levels don't match
    /// </summary>
    public class PasteInterceptor : IDisposable
    {
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;
        private const int WM_PASTE = 0x0302;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_V = 0x56;
        private const int VK_INSERT = 0x2D;
        private const int VK_SHIFT = 0x10;
        private bool _disposed = false;
        private SensitivityInfo? _currentClipboardSensitivity;
        private SensitivityConfig _config;

        public event EventHandler<PasteBlockedEventArgs>? PasteBlocked;

        public PasteInterceptor(Window window, SensitivityConfig config)
        {
            _config = config;
            var helper = new WindowInteropHelper(window);
            _hwnd = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            // Install low-level keyboard hook to catch paste shortcuts globally
            InstallKeyboardHook();
            LogMessage($"PasteInterceptor initialized. PasteBlockingEnabled={_config.PasteBlockingEnabled}, KeyboardHookId={_keyboardHookId}");
        }

        public void Start()
        {
            // Already started in constructor, but this method exists for consistency
            LogMessage("PasteInterceptor.Start() called");
        }

        public void SetClipboardSensitivity(SensitivityInfo? sensitivity)
        {
            _currentClipboardSensitivity = sensitivity;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_PASTE)
            {
                if (ShouldBlockPaste())
                {
                    OnPasteBlocked();
                    handled = true; // Block the paste
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }

        private NativeMethods.LowLevelKeyboardProc? _keyboardHook;
        private IntPtr _keyboardHookId = IntPtr.Zero;

        private void InstallKeyboardHook()
        {
            _keyboardHook = LowLevelKeyboardProc;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _keyboardHookId = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _keyboardHook,
                    NativeMethods.GetModuleHandle(curModule?.ModuleName ?? "GridBanner.exe"),
                    0);
                
                if (_keyboardHookId == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    LogMessage($"ERROR: Failed to install keyboard hook. Error code: {error}");
                }
                else
                {
                    LogMessage($"Keyboard hook installed successfully. Hook ID: {_keyboardHookId}");
                }
            }
        }

        private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    
                    // Check for Ctrl+V or Shift+Insert (paste shortcuts)
                    bool ctrlPressed = (NativeMethods.GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
                    bool shiftPressed = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
                    
                    if ((ctrlPressed && vkCode == VK_V) || (shiftPressed && vkCode == VK_INSERT))
                    {
                        LogMessage($"Paste shortcut detected (Ctrl+V or Shift+Insert). Checking if should block...");
                        if (ShouldBlockPaste())
                        {
                            LogMessage("Paste BLOCKED by ShouldBlockPaste()");
                            OnPasteBlocked();
                            return (IntPtr)1; // Block the key press
                        }
                        else
                        {
                            LogMessage("Paste ALLOWED by ShouldBlockPaste()");
                        }
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private bool ShouldBlockPaste()
        {
            LogMessage($"ShouldBlockPaste() called. PasteBlockingEnabled={_config.PasteBlockingEnabled}, CurrentSensitivity={_currentClipboardSensitivity?.Level ?? SensitivityLevel.None}");
            
            if (!_config.PasteBlockingEnabled)
            {
                LogMessage("Paste blocking is disabled in config");
                return false;
            }

            if (_currentClipboardSensitivity == null)
            {
                LogMessage("No clipboard sensitivity info - allowing paste");
                return false; // No sensitivity info, allow paste
            }

            LogMessage($"Current clipboard sensitivity: Level={_currentClipboardSensitivity.Level}, Source={_currentClipboardSensitivity.Source}, Label={_currentClipboardSensitivity.LabelName}");

            // Simplified logic: If source is sensitive (Internal or higher), block paste to anywhere else
            if (_currentClipboardSensitivity.Level >= SensitivityLevel.Internal)
            {
                // Get destination sensitivity
                var destinationSensitivity = GetDestinationSensitivity();
                LogMessage($"Destination sensitivity: Level={destinationSensitivity.Level}, Source={destinationSensitivity.Source}");
                
                // Block if destination is not sensitive (Public or None)
                // OR if destination has lower sensitivity than source
                if (destinationSensitivity.Level < _currentClipboardSensitivity.Level)
                {
                    LogMessage($"✓ BLOCKING paste: Source={_currentClipboardSensitivity.Level} ({_currentClipboardSensitivity.Source}) > Destination={destinationSensitivity.Level} ({destinationSensitivity.Source})");
                    return true;
                }
                else
                {
                    LogMessage($"✗ ALLOWING paste: Source={_currentClipboardSensitivity.Level} <= Destination={destinationSensitivity.Level}");
                }
            }
            else
            {
                LogMessage($"Source sensitivity ({_currentClipboardSensitivity.Level}) is below Internal threshold - allowing paste");
            }

            return false;
        }

        private SensitivityInfo GetDestinationSensitivity()
        {
            try
            {
                var foregroundWindow = NativeMethods.GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    return new SensitivityInfo { Level = SensitivityLevel.None };
                }

                uint processId = 0;
                NativeMethods.GetWindowThreadProcessId(foregroundWindow, out processId);
                if (processId == 0)
                {
                    return new SensitivityInfo { Level = SensitivityLevel.None };
                }

                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToUpperInvariant();

                // Check if it's a browser
                if (processName == "CHROME" || processName == "MSEDGE" || processName == "FIREFOX")
                {
                    // Try to get URL from window title first (more reliable)
                    var windowTitle = GetWindowTitle(foregroundWindow);
                    var url = BrowserUrlDetector.ExtractUrlFromTitle(windowTitle);
                    
                    if (string.IsNullOrEmpty(url))
                    {
                        url = BrowserUrlDetector.GetUrlFromBrowserWindow(foregroundWindow, processName);
                    }
                    
                    if (!string.IsNullOrEmpty(url))
                    {
                        // Check if it's precisionxtech.sharepoint.com
                        if (SensitivityLabelManager.IsSharePointUrl(url))
                        {
                            return new SensitivityInfo
                            {
                                Level = SensitivityLevel.Internal,
                                Source = "SharePoint",
                                LabelName = url
                            };
                        }
                        
                        var level = SensitivityLabelManager.GetSensitivityFromUrl(url);
                        return new SensitivityInfo
                        {
                            Level = level,
                            Source = "Browser",
                            LabelName = url
                        };
                    }
                }

                // Check if it's an Office app
                if (processName == "WINWORD" || processName == "EXCEL" || processName == "POWERPNT")
                {
                    // Try to get sensitivity from Office document
                    var officeSensitivity = SensitivityLabelManager.GetSensitivityFromActiveOfficeApp(foregroundWindow);
                    if (officeSensitivity != null && officeSensitivity.Level >= SensitivityLevel.Internal)
                    {
                        return officeSensitivity;
                    }
                }

                // Default: assume public/none for unknown applications
                // This means paste will be blocked if source is sensitive
                return new SensitivityInfo { Level = SensitivityLevel.Public, Source = "Unknown" };
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting destination sensitivity: {ex.Message}");
                return new SensitivityInfo { Level = SensitivityLevel.None };
            }
        }

        private void OnPasteBlocked()
        {
            if (_currentClipboardSensitivity == null)
            {
                return;
            }

            var destinationSensitivity = GetDestinationSensitivity();
            
            var args = new PasteBlockedEventArgs
            {
                SourceSensitivity = _currentClipboardSensitivity,
                DestinationSensitivity = destinationSensitivity,
                Timestamp = DateTime.Now
            };

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                PasteBlocked?.Invoke(this, args);
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            try
            {
                int length = NativeMethods.GetWindowTextLength(hwnd);
                if (length == 0) return "";

                StringBuilder sb = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void LogMessage(string message)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PasteInterceptor] {message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_keyboardHookId != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                }
                _hwndSource?.RemoveHook(WndProc);
                _disposed = true;
            }
        }

        internal static class NativeMethods
        {
            public const int WH_KEYBOARD_LL = 13;
            public const int WM_KEYDOWN = 0x0100;
            public const int WM_SYSKEYDOWN = 0x0104;

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern short GetKeyState(int nVirtKey);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowTextLength(IntPtr hWnd);

            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        }
    }

    public class PasteBlockedEventArgs : EventArgs
    {
        public SensitivityInfo SourceSensitivity { get; set; } = new();
        public SensitivityInfo DestinationSensitivity { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
