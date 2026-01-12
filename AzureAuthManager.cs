using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using System.IO;
using System.Collections.Generic;

namespace GridBanner
{
    /// <summary>
    /// Manages Azure AD authentication using OAuth 2.0 Device Code Flow.
    /// </summary>
    public class AzureAuthManager
    {
        private readonly IPublicClientApplication _app;
        private AuthenticationResult? _authResult;
        private readonly string[] _scopes;
        private bool _isEnabled;
        private DeviceCodeWindow? _currentDialog;
        private volatile bool _authenticationCompleted = false;
        
        public event EventHandler<string>? LogMessage;
        public event EventHandler? AuthenticationCompleted;
        
        public AzureAuthManager(string? clientId, string? tenantId, string? apiScope, bool enabled)
        {
            _isEnabled = enabled;
            
            if (!enabled || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
            {
                Log("Azure AD authentication is disabled or not configured.");
                _app = null!;
                _scopes = Array.Empty<string>();
                return;
            }
            
            try
            {
                // Build the app first
                _app = PublicClientApplicationBuilder
                    .Create(clientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
                    .WithDefaultRedirectUri()
                    .Build();
                
                // Configure persistent token cache using MsalCacheHelper
                // This ensures tokens persist across app restarts
                try
                {
                    var cacheDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GridBanner",
                        "msal_cache");
                    
                    // Ensure directory exists
                    Directory.CreateDirectory(cacheDir);
                    
                    var storageProperties = new StorageCreationPropertiesBuilder(
                        "msal_cache.dat",
                        cacheDir)
                        .WithMacKeyChain("GridBanner", "MSALCache")
                        .WithLinuxKeyring(
                            "gridbanner_msal_cache",
                            MsalCacheHelper.LinuxKeyRingDefaultCollection,
                            "GridBanner MSAL Cache",
                            new KeyValuePair<string, string>("Version", "1"),
                            new KeyValuePair<string, string>("ProductGroup", "GridBanner"))
                        .Build();
                    
                    var cacheHelper = MsalCacheHelper.CreateAsync(storageProperties).GetAwaiter().GetResult();
                    cacheHelper.RegisterCache(_app.UserTokenCache);
                    
                    Log($"Token cache configured: {cacheDir}");
                }
                catch (Exception cacheEx)
                {
                    // Log but don't fail - app will still work, just won't persist tokens
                    Log($"Warning: Could not configure persistent token cache: {cacheEx.Message}. Tokens will only persist for this session.");
                }
                
                _scopes = string.IsNullOrEmpty(apiScope) 
                    ? new[] { $"{clientId}/.default" }
                    : new[] { apiScope };
                
                Log($"Azure AD authentication initialized: ClientId={clientId}, TenantId={tenantId}, Scopes=[{string.Join(", ", _scopes)}]");
                
                // Check for cached accounts on initialization (fire and forget)
                Task.Run(async () =>
                {
                    try
                    {
                        var cachedAccounts = await _app.GetAccountsAsync();
                        if (cachedAccounts.Any())
                        {
                            var logMsg = $"Found {cachedAccounts.Count()} cached account(s) on startup.";
                            Log(logMsg);
                            foreach (var account in cachedAccounts)
                            {
                                Log($"  - Cached account: {account.Username} (HomeAccountId: {account.HomeAccountId})");
                            }
                        }
                        else
                        {
                            Log("No cached accounts found on startup.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error checking cached accounts: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Error initializing Azure AD authentication: {ex.Message}");
                _app = null!;
                _scopes = Array.Empty<string>();
            }
        }
        
        /// <summary>
        /// Check if authentication is enabled and configured.
        /// </summary>
        public bool IsEnabled => _isEnabled && _app != null;
        
        /// <summary>
        /// Get the current access token, or null if not authenticated.
        /// </summary>
        public string? GetAccessToken()
        {
            return _authResult?.AccessToken;
        }
        
        /// <summary>
        /// Get the authenticated user's principal name (UPN or email).
        /// </summary>
        public string? GetUserPrincipalName()
        {
            return _authResult?.Account?.Username;
        }
        
        /// <summary>
        /// Get the authenticated user's object ID.
        /// </summary>
        public string? GetUserObjectId()
        {
            return _authResult?.Account?.HomeAccountId?.ObjectId;
        }
        
        /// <summary>
        /// Check if authentication has completed (for polling).
        /// </summary>
        public bool IsAuthenticationCompleted => _authenticationCompleted;
        
        /// <summary>
        /// Ensure the user is authenticated. Returns true if authenticated, false otherwise.
        /// </summary>
        public async Task<bool> EnsureAuthenticatedAsync()
        {
            if (!IsEnabled)
            {
                return false;
            }
            
            try
            {
                // Try silent authentication first (if cached)
                Log("Checking for cached accounts...");
                var accounts = await _app.GetAccountsAsync();
                Log($"Found {accounts.Count()} cached account(s).");
                
                if (accounts.Any())
                {
                    // Try each account until one works
                    foreach (var account in accounts)
                    {
                        try
                        {
                            Log($"Attempting silent authentication for account: {account.Username}");
                            _authResult = await _app.AcquireTokenSilent(_scopes, account)
                                .WithForceRefresh(false) // Use cached token if valid
                                .ExecuteAsync();
                            Log($"Silent authentication successful for {account.Username} (using cached token).");
                            _authenticationCompleted = true;
                            return true;
                        }
                        catch (MsalUiRequiredException ex)
                        {
                            // Token expired or not found for this account, try next one
                            Log($"Silent auth failed for {account.Username}: {ex.Message}. Trying next account...");
                            continue;
                        }
                        catch (MsalException ex)
                        {
                            Log($"Silent auth error for {account.Username}: {ex.Message} (Error Code: {ex.ErrorCode})");
                            // Continue to next account
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Log($"Silent authentication failed for {account.Username}: {ex.Message}");
                            continue;
                        }
                    }
                    
                    // If we get here, all cached accounts failed
                    Log("All cached accounts failed silent authentication. Requesting new authentication.");
                }
                else
                {
                    Log("No cached accounts found. Requesting new authentication.");
                }
                
                // Device code flow for interactive authentication
                Log("=== Starting device code flow authentication ===");
                Log($"Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                Log($"Application.Current is null: {Application.Current == null}");
                Log($"Application.Current.Dispatcher is null: {Application.Current?.Dispatcher == null}");
                
                _authResult = await _app.AcquireTokenWithDeviceCode(
                    _scopes,
                    deviceCodeResult =>
                    {
                        Log($"=== Device code callback invoked ===");
                        Log($"UserCode: {deviceCodeResult.UserCode}");
                        Log($"VerificationUrl: {deviceCodeResult.VerificationUrl}");
                        Log($"Message: {deviceCodeResult.Message}");
                        Log($"Callback thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                        
                        // Show device code to user in a non-modal dialog
                        try
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                Log($"=== Inside Dispatcher.Invoke ===");
                                Log($"Dispatcher thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                                Log($"Current dialog is null: {_currentDialog == null}");
                                Log($"Current dialog IsVisible: {_currentDialog?.IsVisible ?? false}");
                                
                                if (_currentDialog == null || !_currentDialog.IsVisible)
                                {
                                    Log("Creating new DeviceCodeWindow...");
                                    _currentDialog = new DeviceCodeWindow(deviceCodeResult);
                                    Log($"DeviceCodeWindow created. IsVisible: {_currentDialog.IsVisible}");
                                    
                                    // Set auth manager so window can poll for completion
                                    _currentDialog.SetAuthManager(this);
                                    Log("SetAuthManager called on DeviceCodeWindow.");
                                    
                                    _currentDialog.Show(); // Show non-modally so authentication can continue
                                    Log($"DeviceCodeWindow.Show() called. IsVisible after Show: {_currentDialog.IsVisible}");
                                    Log($"Device code window opened. Code: {deviceCodeResult.UserCode}, URL: {deviceCodeResult.VerificationUrl}");
                                }
                                else
                                {
                                    Log("Updating existing DeviceCodeWindow...");
                                    // Update existing window
                                    _currentDialog.DeviceCodeTextBox.Text = deviceCodeResult.UserCode;
                                    _currentDialog.VerificationUrlTextBox.Text = deviceCodeResult.VerificationUrl;
                                    _currentDialog.StatusTextBox.Text = deviceCodeResult.Message;
                                }
                            });
                            Log("Dispatcher.Invoke completed successfully");
                        }
                        catch (Exception ex)
                        {
                            Log($"ERROR in Dispatcher.Invoke: {ex.Message}");
                            Log($"Stack trace: {ex.StackTrace}");
                        }
                        return Task.CompletedTask;
                    })
                    .ExecuteAsync();
                
                Log($"=== ExecuteAsync completed ===");
                Log($"Authentication successful for user: {_authResult.Account.Username}");
                Log($"Current thread after ExecuteAsync: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                Log($"Current dialog is null: {_currentDialog == null}");
                Log($"Current dialog IsVisible: {_currentDialog?.IsVisible ?? false}");
                
                // Mark authentication as completed
                _authenticationCompleted = true;
                Log("Authentication completed flag set to true.");
                
                // Fire event
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
                Log("AuthenticationCompleted event fired.");
                
                // Close the dialog when authentication completes
                // Use multiple approaches to ensure it closes
                Log("=== Attempting to close device code window ===");
                CloseDeviceCodeWindow();
                Log("=== CloseDeviceCodeWindow() call completed ===");
                
                return true;
            }
            catch (MsalException ex)
            {
                Log($"Authentication failed: {ex.Message} (Error Code: {ex.ErrorCode})");
                if (ex.InnerException != null)
                {
                    Log($"Inner exception: {ex.InnerException.Message}");
                }
                _authResult = null;
                return false;
            }
            catch (Exception ex)
            {
                Log($"Unexpected authentication error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Inner exception: {ex.InnerException.Message}");
                }
                _authResult = null;
                return false;
            }
        }
        
        /// <summary>
        /// Close the device code window using multiple approaches to ensure it works.
        /// </summary>
        private void CloseDeviceCodeWindow()
        {
            Log("=== CloseDeviceCodeWindow() called ===");
            Log($"Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            Log($"Application.Current is null: {Application.Current == null}");
            Log($"Application.Current.Dispatcher is null: {Application.Current?.Dispatcher == null}");
            Log($"Static CurrentInstance is null: {DeviceCodeWindow.CurrentInstance == null}");
            Log($"Instance _currentDialog is null: {_currentDialog == null}");
            Log($"Instance _currentDialog IsVisible: {_currentDialog?.IsVisible ?? false}");
            
            // Approach 1: Use static method (most reliable)
            Log("--- Approach 1: Static method ---");
            try
            {
                Log($"Calling DeviceCodeWindow.CloseCurrent()...");
                DeviceCodeWindow.CloseCurrent();
                Log("DeviceCodeWindow.CloseCurrent() completed without exception.");
            }
            catch (Exception ex)
            {
                Log($"ERROR: Static close failed: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
            
            // Approach 2: Use instance reference on UI thread
            Log("--- Approach 2: Instance reference via Dispatcher ---");
            if (Application.Current?.Dispatcher != null)
            {
                Log("Application.Current.Dispatcher is available, calling BeginInvoke...");
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Send, // High priority
                    new Action(() =>
                    {
                        Log($"=== Inside BeginInvoke callback ===");
                        Log($"BeginInvoke thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                        try
                        {
                            Log($"Checking _currentDialog: null={_currentDialog == null}, IsVisible={_currentDialog?.IsVisible ?? false}");
                            if (_currentDialog != null)
                            {
                                Log("_currentDialog is not null, checking IsVisible...");
                                if (_currentDialog.IsVisible)
                                {
                                    Log("Window is visible, calling Close()...");
                                    _currentDialog.Close();
                                    Log("Close() called successfully.");
                                }
                                else
                                {
                                    Log("Window is not visible, skipping Close().");
                                }
                                _currentDialog = null;
                                Log("_currentDialog set to null.");
                            }
                            else
                            {
                                Log("_currentDialog is null, nothing to close.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"ERROR: Instance close failed: {ex.Message}");
                            Log($"Stack trace: {ex.StackTrace}");
                        }
                        Log("=== BeginInvoke callback completed ===");
                    }));
                Log("BeginInvoke called (async, may not have completed yet).");
            }
            else
            {
                Log("Application.Current.Dispatcher is null, skipping Approach 2.");
            }
            
            // Approach 3: Direct close if on same thread
            Log("--- Approach 3: Direct close (same thread) ---");
            try
            {
                if (_currentDialog != null)
                {
                    Log($"Checking if on same thread... CheckAccess()={_currentDialog.Dispatcher.CheckAccess()}");
                    if (_currentDialog.Dispatcher.CheckAccess())
                    {
                        Log("On same thread, calling Close() directly...");
                        _currentDialog.Close();
                        _currentDialog = null;
                        Log("Direct close successful.");
                    }
                    else
                    {
                        Log("Not on same thread, cannot close directly.");
                    }
                }
                else
                {
                    Log("_currentDialog is null, cannot close directly.");
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Direct close failed: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
            
            Log("=== CloseDeviceCodeWindow() completed ===");
        }
        
        /// <summary>
        /// Sign out the current user.
        /// </summary>
        public async Task SignOutAsync()
        {
            if (!IsEnabled || _authResult == null)
            {
                return;
            }
            
            try
            {
                var accounts = await _app.GetAccountsAsync();
                foreach (var account in accounts)
                {
                    await _app.RemoveAsync(account);
                }
                _authResult = null;
                Log("User signed out.");
            }
            catch (Exception ex)
            {
                Log($"Error signing out: {ex.Message}");
            }
        }
        
        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
}

