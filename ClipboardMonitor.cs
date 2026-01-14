using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GridBanner
{
    /// <summary>
    /// Monitors clipboard changes and detects source application and sensitivity information
    /// </summary>
    public class ClipboardMonitor : IDisposable
    {
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private bool _disposed = false;

        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

        public ClipboardMonitor(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _hwnd = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            // Add this window to the clipboard viewer chain
            if (!NativeMethods.AddClipboardFormatListener(_hwnd))
            {
                throw new InvalidOperationException("Failed to add clipboard format listener");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnClipboardChanged()
        {
            try
            {
                // Get clipboard data
                if (!System.Windows.Clipboard.ContainsText())
                {
                    return;
                }

                var clipboardText = System.Windows.Clipboard.GetText();
                
                // Detect source application
                var sourceInfo = DetectSourceApplication();
                
                // Create event args
                var args = new ClipboardChangedEventArgs
                {
                    ClipboardText = clipboardText,
                    SourceApplication = sourceInfo.ApplicationName,
                    SourceProcessId = sourceInfo.ProcessId,
                    SourceWindowTitle = sourceInfo.WindowTitle,
                    Timestamp = DateTime.Now
                };

                // Fire event on UI thread
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ClipboardChanged?.Invoke(this, args);
                }), DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in OnClipboardChanged: {ex.Message}");
            }
        }

        private SourceApplicationInfo DetectSourceApplication()
        {
            try
            {
                // Get the window that currently has focus (likely the source of the copy)
                var foregroundWindow = NativeMethods.GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    return new SourceApplicationInfo { ApplicationName = "Unknown" };
                }

                uint processId = 0;
                NativeMethods.GetWindowThreadProcessId(foregroundWindow, out processId);

                if (processId == 0)
                {
                    return new SourceApplicationInfo { ApplicationName = "Unknown" };
                }

                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                var windowTitle = GetWindowTitle(foregroundWindow);

                return new SourceApplicationInfo
                {
                    ApplicationName = process.ProcessName,
                    ProcessId = (int)processId,
                    WindowTitle = windowTitle,
                    ProcessPath = process.MainModule?.FileName ?? ""
                };
            }
            catch (Exception ex)
            {
                LogMessage($"Error detecting source application: {ex.Message}");
                return new SourceApplicationInfo { ApplicationName = "Unknown" };
            }
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
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ClipboardMonitor] {message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    NativeMethods.RemoveClipboardFormatListener(_hwnd);
                }
                _hwndSource?.RemoveHook(WndProc);
                _disposed = true;
            }
        }

        internal static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowTextLength(IntPtr hWnd);
        }
    }

    public class ClipboardChangedEventArgs : EventArgs
    {
        public string ClipboardText { get; set; } = "";
        public string SourceApplication { get; set; } = "";
        public int SourceProcessId { get; set; }
        public string SourceWindowTitle { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class SourceApplicationInfo
    {
        public string ApplicationName { get; set; } = "";
        public int ProcessId { get; set; }
        public string WindowTitle { get; set; } = "";
        public string ProcessPath { get; set; } = "";
    }
}
