using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Identity.Client;

namespace GridBanner
{
    public partial class DeviceCodeWindow : Window
    {
        private readonly DeviceCodeResult _deviceCodeResult;
        private bool _cancelled = false;
        private System.Windows.Threading.DispatcherTimer? _statusTimer;
        private System.Windows.Threading.DispatcherTimer? _checkAuthTimer;
        private AzureAuthManager? _authManager;
        
        // Expose controls for external updates
        public System.Windows.Controls.TextBox DeviceCodeTextBox => DeviceCodeText;
        public System.Windows.Controls.TextBlock VerificationUrlTextBox => VerificationUrlText;
        public System.Windows.Controls.TextBlock StatusTextBox => StatusText;
        
        // Static reference for external closing
        public static DeviceCodeWindow? CurrentInstance { get; private set; }
        
        /// <summary>
        /// Set the auth manager to poll for completion.
        /// </summary>
        public void SetAuthManager(AzureAuthManager authManager)
        {
            _authManager = authManager;
            
            // Poll every 500ms to check if authentication completed
            _checkAuthTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _checkAuthTimer.Tick += (s, e) =>
            {
                if (_authManager != null && _authManager.IsAuthenticationCompleted)
                {
                    System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Authentication completed detected via polling. Closing window...");
                    StatusText.Text = "Authentication successful! Closing...";
                    _checkAuthTimer?.Stop();
                    
                    // Close on next tick to allow status update to show
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Actually closing window now...");
                        Close();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
            _checkAuthTimer.Start();
            System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Started polling timer for authentication completion.");
        }
        
        public DeviceCodeWindow(DeviceCodeResult deviceCodeResult)
        {
            InitializeComponent();
            _deviceCodeResult = deviceCodeResult;
            CurrentInstance = this;
            
            // Display device code and URL
            DeviceCodeText.Text = deviceCodeResult.UserCode;
            VerificationUrlText.Text = deviceCodeResult.VerificationUrl;
            
            // Update status message
            StatusText.Text = deviceCodeResult.Message;
            
            // Automatically open browser
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = deviceCodeResult.VerificationUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore if browser can't be opened
            }
            
            // Update status periodically to show we're waiting
            _statusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _statusTimer.Tick += (s, e) =>
            {
                if (StatusText.Text == "Device code copied to clipboard!")
                {
                    // Reset to waiting message after copy feedback
                    StatusText.Text = "Waiting for authentication...";
                }
                else if (!StatusText.Text.Contains("copied") && !StatusText.Text.Contains("Failed"))
                {
                    // Keep showing we're waiting
                    StatusText.Text = "Waiting for authentication... (check your browser)";
                }
            };
            _statusTimer.Start();
            
            // Clean up static reference when window closes
            Closed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Window Closed event fired. Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                CurrentInstance = null;
                _statusTimer?.Stop();
                _checkAuthTimer?.Stop();
                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Cleanup completed.");
            };
            
            // Log when window is actually closing
            Closing += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Window Closing event fired. Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            };
        }
        
        /// <summary>
        /// Static method to close the current instance from anywhere.
        /// </summary>
        public static void CloseCurrent()
        {
            System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] CloseCurrent() called. CurrentInstance is null: {CurrentInstance == null}");
            
            if (CurrentInstance != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] CurrentInstance found. IsVisible: {CurrentInstance.IsVisible}, Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                
                if (CurrentInstance.IsVisible)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Calling Dispatcher.Invoke to close window...");
                        CurrentInstance.Dispatcher.Invoke(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Inside Dispatcher.Invoke. Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Calling Close()...");
                                CurrentInstance.Close();
                                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Close() completed successfully.");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] ERROR in Close(): {ex.Message}");
                                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Stack trace: {ex.StackTrace}");
                            }
                        });
                        System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Dispatcher.Invoke completed.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] ERROR in Dispatcher.Invoke: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] Window is not visible, not closing.");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceCodeWindow] CurrentInstance is null, cannot close.");
            }
        }
        
        private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
        {
            CopyDeviceCodeToClipboard();
        }
        
        private void DeviceCodeText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                CopyDeviceCodeToClipboard();
            }
        }
        
        private void CopyDeviceCodeToClipboard()
        {
            try
            {
                Clipboard.SetText(_deviceCodeResult.UserCode);
                StatusText.Text = "Device code copied to clipboard!";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to copy: {ex.Message}";
            }
        }
        
        private void VerificationUrlText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _deviceCodeResult.VerificationUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Failed to open browser: {ex.Message}";
                }
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancelled = true;
            DialogResult = false;
            Close();
        }
        
        public bool WasCancelled => _cancelled;
    }
}

