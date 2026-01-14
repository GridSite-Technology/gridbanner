using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace GridBanner
{
    /// <summary>
    /// Monitors foreground window changes during system lockdown to detect escape attempts
    /// </summary>
    public static class SystemLockdownFocusMonitor
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Checks if the foreground window is a SystemLockdown window or CommandWindow
        /// </summary>
        public static bool IsForegroundWindowAllowed(IntPtr[] allowedWindowHandles)
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    return false;
                }

                // Check if it's one of our allowed windows
                foreach (var handle in allowedWindowHandles)
                {
                    if (foregroundWindow == handle)
                    {
                        return true;
                    }
                }

                // If it's not one of our allowed windows, it's an escape attempt
                // Check if it's a suspicious window for logging purposes
                if (IsSuspiciousWindow(foregroundWindow))
                {
                    LogMessage("Detected suspicious window - will lock system");
                }
                else
                {
                    LogMessage($"Detected non-allowed foreground window - will lock system");
                }

                // Any window that's not our SystemLockdown or CommandWindow is not allowed
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"Error checking foreground window: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a window is suspicious (Task Manager, Windows Security screen, etc.)
        /// </summary>
        private static bool IsSuspiciousWindow(IntPtr hwnd)
        {
            try
            {
                if (!IsWindowVisible(hwnd))
                {
                    return false;
                }

                uint processId = 0;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId == 0)
                {
                    return false;
                }

                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToLowerInvariant();
                var windowTitle = GetWindowTitle(hwnd).ToLowerInvariant();

                // Check for Task Manager
                if (processName == "taskmgr" || windowTitle.Contains("task manager"))
                {
                    LogMessage("Detected Task Manager window");
                    return true;
                }

                // Check for Windows Security screen (Ctrl+Alt+Delete)
                // This appears as a window with specific characteristics
                if (processName == "winlogon" || processName == "csrss")
                {
                    // Windows Security screen is typically handled by winlogon
                    if (windowTitle.Contains("windows security") || 
                        windowTitle.Contains("security options") ||
                        windowTitle == "")
                    {
                        LogMessage("Detected Windows Security screen (Ctrl+Alt+Delete)");
                        return true;
                    }
                }

                // Check for other suspicious processes
                var suspiciousProcesses = new[] { "procexp", "procexp64", "processhacker", "processexplorer" };
                if (Array.Exists(suspiciousProcesses, p => processName.Contains(p)))
                {
                    LogMessage($"Detected suspicious process: {processName}");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            try
            {
                int length = GetWindowTextLength(hwnd);
                if (length == 0)
                {
                    return "";
                }

                StringBuilder sb = new StringBuilder(length + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static void LogMessage(string message)
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SystemLockdownFocusMonitor] {message}\n");
            }
            catch { }
        }
    }
}
