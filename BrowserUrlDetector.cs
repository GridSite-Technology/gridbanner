using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GridBanner
{
    /// <summary>
    /// Detects URLs from browser windows (Chrome, Edge, etc.)
    /// </summary>
    public class BrowserUrlDetector
    {
        /// <summary>
        /// Gets the current URL from a browser window
        /// </summary>
        public static string? GetUrlFromBrowserWindow(IntPtr windowHandle, string processName)
        {
            try
            {
                if (processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase))
                {
                    return GetUrlFromChromium(windowHandle);
                }
                else if (processName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                {
                    return GetUrlFromFirefox(windowHandle);
                }
                else if (processName.Equals("iexplore", StringComparison.OrdinalIgnoreCase))
                {
                    return GetUrlFromIE(windowHandle);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting URL from browser: {ex.Message}");
                return null;
            }
        }

        private static string? GetUrlFromChromium(IntPtr windowHandle)
        {
            try
            {
                // For Chromium-based browsers, we can try to get URL from:
                // 1. Window title (often contains the page title, not URL)
                // 2. Automation/UIA (more reliable but complex)
                // 3. Browser's internal APIs (requires browser extension or COM)
                
                // Method 1: Try to get from window title (limited reliability)
                var title = GetWindowTitle(windowHandle);
                
                // Method 2: Try UIA (UI Automation) - more reliable
                var url = GetUrlFromUIA(windowHandle);
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }

                // Method 3: For Chrome/Edge, we can try reading from browser's process memory
                // This is complex and may require elevated permissions
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetUrlFromFirefox(IntPtr windowHandle)
        {
            // Firefox uses different mechanisms
            // Would need Mozilla-specific APIs or UIA
            return GetUrlFromUIA(windowHandle);
        }

        private static string? GetUrlFromIE(IntPtr windowHandle)
        {
            try
            {
                // Internet Explorer uses COM interfaces
                // This is deprecated but can still work
                return null; // Placeholder
            }
            catch
            {
                return null;
            }
        }

        private static string? GetUrlFromUIA(IntPtr windowHandle)
        {
            try
            {
                // Use UI Automation to get URL from address bar
                // This requires System.Windows.Automation namespace
                // For now, return null - full UIA implementation needed
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetWindowTitle(IntPtr hwnd)
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

        /// <summary>
        /// Extracts URL from window title (fallback method)
        /// </summary>
        public static string? ExtractUrlFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // Try to find URL pattern in title
            var urlPattern = new Regex(@"https?://[^\s\)]+", RegexOptions.IgnoreCase);
            var match = urlPattern.Match(title);
            if (match.Success)
            {
                var url = match.Value.TrimEnd(')', ']', '}', '.', ',', ';');
                return url;
            }

            // Also check for precisionxtech.sharepoint.com in title (even without https://)
            if (title.Contains("precisionxtech.sharepoint.com", StringComparison.OrdinalIgnoreCase))
            {
                // Try to extract the full URL
                var sharepointPattern = new Regex(@"https?://[^\s\)]*precisionxtech\.sharepoint\.com[^\s\)]*", RegexOptions.IgnoreCase);
                var sharepointMatch = sharepointPattern.Match(title);
                if (sharepointMatch.Success)
                {
                    return sharepointMatch.Value.TrimEnd(')', ']', '}', '.', ',', ';');
                }
                
                // If no full URL found, construct one
                return "https://precisionxtech.sharepoint.com";
            }

            return null;
        }

        private static void LogMessage(string message)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [BrowserUrlDetector] {message}\n");
            }
            catch { }
        }

        internal static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern int GetWindowTextLength(IntPtr hWnd);
        }
    }
}
