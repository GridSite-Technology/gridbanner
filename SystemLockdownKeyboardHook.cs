using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GridBanner
{
    /// <summary>
    /// Keyboard hook to block dangerous key combinations during system lockdown
    /// </summary>
    public class SystemLockdownKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        // Virtual key codes
        private const int VK_LWIN = 0x5B;      // Left Windows key
        private const int VK_RWIN = 0x5C;      // Right Windows key
        private const int VK_DELETE = 0x2E;
        private const int VK_L = 0x4C;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F4 = 0x73;
        private const int VK_F1 = 0x70;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_ALT = 0x12;
        private const int VK_MENU = 0x12;      // Alt key (same as VK_ALT)

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private bool _disposed = false;

        public SystemLockdownKeyboardHook()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule != null)
                {
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                        GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;

                if (isKeyDown)
                {
                    // Check modifier keys
                    bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool altPressed = (GetKeyState(VK_ALT) & 0x8000) != 0;
                    bool winPressed = (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;

                    // Block Ctrl+Alt+Delete
                    if (ctrlPressed && altPressed && vkCode == VK_DELETE)
                    {
                        LogMessage("Blocked Ctrl+Alt+Delete");
                        return (IntPtr)1; // Block the key
                    }

                    // Block Windows+L (lock screen)
                    if (winPressed && vkCode == VK_L)
                    {
                        LogMessage("Blocked Windows+L");
                        return (IntPtr)1; // Block the key
                    }

                    // Block Ctrl+Shift+Escape (Task Manager)
                    if (ctrlPressed && shiftPressed && vkCode == VK_ESCAPE)
                    {
                        LogMessage("Blocked Ctrl+Shift+Escape");
                        return (IntPtr)1; // Block the key
                    }

                    // Block Alt+F4 (close window)
                    if (altPressed && vkCode == VK_F4)
                    {
                        LogMessage("Blocked Alt+F4");
                        return (IntPtr)1; // Block the key
                    }

                    // Block Windows key alone (Start menu)
                    if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    {
                        LogMessage("Blocked Windows key");
                        return (IntPtr)1; // Block the key
                    }

                    // Note: Ctrl+Alt+Shift+F1 is handled by the global hotkey system
                    // We don't intercept it here - let it pass through to open the command window
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                }
                _disposed = true;
            }
        }

        private void LogMessage(string message)
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SystemLockdownHook] {message}\n");
            }
            catch { }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();
    }

    /// <summary>
    /// Helper class to lock the Windows workstation
    /// </summary>
    public static class SystemLockHelper
    {
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        public static void LockSystem()
        {
            try
            {
                LockWorkStation();
            }
            catch (Exception ex)
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "userdata", "gridbanner", "gridbanner.log");

                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SystemLockHelper] Failed to lock system: {ex.Message}\n");
                }
                catch { }
            }
        }
    }
}
