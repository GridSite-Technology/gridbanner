using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace GridBanner
{
    /// <summary>
    /// Represents a public key that can be published to the central keyring.
    /// </summary>
    public sealed record PublicKeyInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        
        [JsonPropertyName("key_type")]
        public string KeyType { get; init; } = "";
        
        [JsonPropertyName("key_data")]
        public string KeyData { get; init; } = "";
        
        [JsonPropertyName("key_name")]
        public string? KeyName { get; init; }
        
        [JsonPropertyName("fingerprint")]
        public string? Fingerprint { get; init; }
        
        [JsonPropertyName("uploaded_at")]
        public string? UploadedAt { get; init; }
        
        /// <summary>
        /// Local file path where this key was found (not serialized to server).
        /// </summary>
        [JsonIgnore]
        public string? SourcePath { get; init; }
        
        /// <summary>
        /// Whether the private key is password-protected (not serialized to server).
        /// </summary>
        [JsonIgnore]
        public bool IsPasswordProtected { get; init; }
    }

    /// <summary>
    /// Manages public key detection, upload, and synchronization with the central keyring.
    /// </summary>
    public class KeyringManager
    {
        private readonly HttpClient _httpClient;
        private string? _baseUrl;
        private string? _username;
        private bool _enabled;
        private AzureAuthManager? _authManager;
        
        // Local storage paths
        private static readonly string UserDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner");
        private static readonly string IgnoredKeysPath = Path.Combine(UserDataPath, "ignored_keys.json");
        private static readonly string UploadedKeysPath = Path.Combine(UserDataPath, "uploaded_keys.json");
        
        // SSH directory path
        private static readonly string SshDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        
        // Additional custom key paths (user can add more)
        private static readonly string CustomKeysPath = Path.Combine(UserDataPath, "custom_key_paths.json");

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public event EventHandler<string>? LogMessage;
        
        // Reserved for future use - allows subscribers to be notified when new keys are detected
        #pragma warning disable CS0067
        public event EventHandler<List<PublicKeyInfo>>? NewKeysDetected;
        #pragma warning restore CS0067

        public KeyringManager()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; // Increased timeout for key operations
        }

        /// <summary>
        /// Configure the keyring manager with the alert server URL and username.
        /// </summary>
        public void Configure(string? alertUrl, string? username, bool enabled, AzureAuthManager? authManager = null)
        {
            _enabled = enabled;
            _username = username;
            _authManager = authManager;
            
            if (!string.IsNullOrEmpty(alertUrl))
            {
                // Extract base URL from alert URL (remove /api/alert suffix)
                var uri = new Uri(alertUrl);
                _baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            
            Log($"Keyring configured: enabled={enabled}, username={username}, baseUrl={_baseUrl}, azureAuth={(_authManager?.IsEnabled ?? false)}");
        }
        
        /// <summary>
        /// Get the effective username to use for API calls.
        /// Uses Azure AD UPN/email if available, otherwise falls back to configured username.
        /// </summary>
        private string GetEffectiveUsername()
        {
            // If Azure AD is enabled and authenticated, use the Azure AD identity
            if (_authManager != null && _authManager.IsEnabled)
            {
                var upn = _authManager.GetUserPrincipalName();
                if (!string.IsNullOrEmpty(upn))
                {
                    return upn;
                }
            }
            
            // Fall back to configured username
            return _username ?? string.Empty;
        }

        /// <summary>
        /// Detect local public keys that haven't been uploaded yet.
        /// </summary>
        public async Task<List<PublicKeyInfo>> DetectNewLocalKeysAsync(CancellationToken ct = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_baseUrl))
            {
                return new List<PublicKeyInfo>();
            }

            var newKeys = new List<PublicKeyInfo>();
            
            try
            {
                // Get local keys
                var localKeys = DetectLocalKeys();
                if (localKeys.Count == 0)
                {
                    Log("No local SSH keys found.");
                    return newKeys;
                }
                
                Log($"Found {localKeys.Count} local SSH keys.");
                
                // Get ignored keys
                var ignoredFingerprints = LoadIgnoredKeys();
                
                // Get already uploaded keys
                var uploadedFingerprints = await GetUploadedKeyFingerprintsAsync(ct);
                
                // Find keys that are neither ignored nor uploaded
                foreach (var key in localKeys)
                {
                    if (!string.IsNullOrEmpty(key.Fingerprint))
                    {
                        if (ignoredFingerprints.Contains(key.Fingerprint))
                        {
                            Log($"Key {key.KeyName} is ignored.");
                            continue;
                        }
                        if (uploadedFingerprints.Contains(key.Fingerprint))
                        {
                            Log($"Key {key.KeyName} is already uploaded.");
                            continue;
                        }
                        
                        newKeys.Add(key);
                        Log($"New key detected: {key.KeyName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error detecting new keys: {ex.Message}");
            }
            
            return newKeys;
        }

        /// <summary>
        /// Detect SSH public keys from the .ssh directory and any custom paths.
        /// </summary>
        public List<PublicKeyInfo> DetectLocalKeys()
        {
            var keys = new List<PublicKeyInfo>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Scan all .pub files in the .ssh directory
            if (Directory.Exists(SshDirectory))
            {
                try
                {
                    foreach (var path in Directory.GetFiles(SshDirectory, "*.pub"))
                    {
                        if (processedPaths.Contains(path)) continue;
                        processedPaths.Add(path);
                        
                        var keyInfo = TryLoadKeyFromPath(path);
                        if (keyInfo != null)
                        {
                            keys.Add(keyInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error scanning .ssh directory: {ex.Message}");
                }
            }
            
            // Also load any custom key paths the user has added
            var customPaths = LoadCustomKeyPaths();
            foreach (var path in customPaths)
            {
                if (processedPaths.Contains(path)) continue;
                processedPaths.Add(path);
                
                var keyInfo = TryLoadKeyFromPath(path);
                if (keyInfo != null)
                {
                    keys.Add(keyInfo);
                }
            }
            
            return keys;
        }
        
        /// <summary>
        /// Try to load a key from a file path.
        /// </summary>
        private PublicKeyInfo? TryLoadKeyFromPath(string path)
        {
            if (!File.Exists(path)) return null;
            
            try
            {
                var keyData = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(keyData)) return null;
                
                var parts = keyData.Split(' ');
                var keyType = parts.Length > 0 ? parts[0] : "unknown";
                var keyName = Path.GetFileName(path);
                var fingerprint = ComputeKeyFingerprint(keyData);
                
                // Check if the private key is password-protected
                var isPasswordProtected = false;
                try
                {
                    isPasswordProtected = SshKeySigner.IsKeyPasswordProtected(path);
                }
                catch
                {
                    // Ignore errors checking password protection
                }
                
                Log($"Detected key: {keyName} ({keyType}){(isPasswordProtected ? " [password protected]" : "")}");
                
                return new PublicKeyInfo
                {
                    KeyType = keyType,
                    KeyData = keyData,
                    KeyName = keyName,
                    Fingerprint = fingerprint,
                    SourcePath = path,
                    IsPasswordProtected = isPasswordProtected
                };
            }
            catch (Exception ex)
            {
                Log($"Error reading key at {path}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Add a custom key path that the user has manually imported.
        /// </summary>
        public void AddCustomKeyPath(string path)
        {
            try
            {
                var customPaths = LoadCustomKeyPaths();
                if (!customPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    customPaths.Add(path);
                    SaveCustomKeyPaths(customPaths);
                    Log($"Added custom key path: {path}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error adding custom key path: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load custom key paths from storage.
        /// </summary>
        private List<string> LoadCustomKeyPaths()
        {
            try
            {
                if (File.Exists(CustomKeysPath))
                {
                    var json = File.ReadAllText(CustomKeysPath);
                    return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading custom key paths: {ex.Message}");
            }
            return new List<string>();
        }
        
        /// <summary>
        /// Save custom key paths to storage.
        /// </summary>
        private void SaveCustomKeyPaths(List<string> paths)
        {
            try
            {
                EnsureDataDir();
                var json = JsonSerializer.Serialize(paths, _jsonOptions);
                File.WriteAllText(CustomKeysPath, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving custom key paths: {ex.Message}");
            }
        }

        /// <summary>
        /// Upload a public key to the central keyring with proof of possession.
        /// </summary>
        /// <param name="key">The public key to upload</param>
        /// <param name="keyPath">Path to the public key file (to find private key)</param>
        /// <param name="password">Optional password for encrypted private keys</param>
        /// <param name="ct">Cancellation token</param>
        public async Task<KeyUploadResult> UploadKeyAsync(PublicKeyInfo key, string? keyPath = null, string? password = null, CancellationToken ct = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_username))
            {
                Log("Keyring not properly configured for upload.");
                return new KeyUploadResult { Success = false, Error = "Keyring not configured" };
            }

            try
            {
                string? challenge = null;
                string? signature = null;
                
                // If we have a key path, do challenge-response verification
                if (!string.IsNullOrEmpty(keyPath) && !string.IsNullOrEmpty(key.Fingerprint))
                {
                    // Check if key needs password
                    var needsPassword = SshKeySigner.IsKeyPasswordProtected(keyPath);
                    if (needsPassword && string.IsNullOrEmpty(password))
                    {
                        Log($"Key {key.KeyName} requires a password.");
                        return new KeyUploadResult 
                        { 
                            Success = false, 
                            NeedsPassword = true,
                            Error = "Private key is password protected" 
                        };
                    }
                    
                    // Request a challenge
                    Log($"Requesting challenge for key {key.Fingerprint}...");
                    challenge = await RequestChallengeAsync(key.Fingerprint, ct);
                    if (string.IsNullOrEmpty(challenge))
                    {
                        Log("Failed to get challenge from server.");
                        return new KeyUploadResult { Success = false, Error = "Failed to get challenge from server" };
                    }
                    Log($"Challenge received successfully.");
                    
                    Log($"Received challenge, signing with private key...");
                    Log($"Challenge length: {(challenge != null ? challenge.Length : 0)} characters");
                    
                    // Sign the challenge on a background thread with timeout
                    try
                    {
                        Log($"=== Starting signature process ===");
                        Log($"Attempting to sign challenge with key at: {keyPath}");
                        Log($"Password provided: {(!string.IsNullOrEmpty(password) ? "Yes (length: " + password.Length + ")" : "No")}");
                        if (challenge != null)
                        {
                            Log($"Challenge to sign: {challenge.Substring(0, Math.Min(20, challenge.Length))}...");
                        }
                        
                        // Run signing on background thread with timeout
                        var signStartTime = DateTime.Now;
                        using var signCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        signCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second timeout for signing
                        
                        Log($"Starting signing operation (timeout: 60s)...");
                        
                        // Set up logging for SshKeySigner
                        SshKeySigner.LogDelegate = (msg) => Log($"SshKeySigner: {msg}");
                        
                        signature = await Task.Run(() => 
                        {
                            try
                            {
                                Log($"[Background Thread] Calling SshKeySigner.SignChallenge...");
                                var result = SshKeySigner.SignChallenge(keyPath, challenge!, password);
                                Log($"[Background Thread] SshKeySigner.SignChallenge completed.");
                                return result;
                            }
                            catch (Exception ex)
                            {
                                Log($"[Background Thread] Error in SignChallenge: {ex.Message}");
                                Log($"[Background Thread] Stack trace: {ex.StackTrace}");
                                throw;
                            }
                            finally
                            {
                                // Clear logging delegate
                                SshKeySigner.LogDelegate = null;
                            }
                        }, signCts.Token);
                        
                        var signDuration = DateTime.Now - signStartTime;
                        
                        Log($"Challenge signed successfully in {signDuration.TotalMilliseconds:F0}ms.");
                        Log($"Signature length: {signature?.Length ?? 0} characters");
                        Log($"=== Signature process completed ===");
                    }
                    catch (TaskCanceledException)
                    {
                        Log("Signature process timed out after 60 seconds.");
                        return new KeyUploadResult 
                        { 
                            Success = false, 
                            Error = "Signing timed out. The key decryption is taking too long. Please try again.",
                            NeedsPassword = false
                        };
                    }
                    catch (OperationCanceledException)
                    {
                        Log("Signature process was cancelled.");
                        return new KeyUploadResult 
                        { 
                            Success = false, 
                            Error = "Signing was cancelled.",
                            NeedsPassword = false
                        };
                    }
                    catch (System.Security.Cryptography.CryptographicException cryptEx)
                    {
                        // Check for password-related errors
                        var errorMsg = cryptEx.Message;
                        Log($"CryptographicException caught: {errorMsg}");
                        Log($"Exception type: {cryptEx.GetType().Name}");
                        if (cryptEx.InnerException != null)
                        {
                            Log($"Inner exception: {cryptEx.InnerException.Message}");
                        }
                        
                        // Check for specific password error patterns
                        var isWrongPassword = errorMsg.Contains("check values don't match", StringComparison.OrdinalIgnoreCase) ||
                                           errorMsg.Contains("Wrong password", StringComparison.OrdinalIgnoreCase);
                        var isPasswordNeeded = errorMsg.Contains("password", StringComparison.OrdinalIgnoreCase) || 
                                             errorMsg.Contains("decrypt", StringComparison.OrdinalIgnoreCase);
                        
                        Log($"isWrongPassword: {isWrongPassword}, isPasswordNeeded: {isPasswordNeeded}, password provided: {!string.IsNullOrEmpty(password)}");
                        
                        if (isWrongPassword && !string.IsNullOrEmpty(password))
                        {
                            // Password was provided but wrong
                            Log("Password provided but incorrect (check values don't match)");
                            return new KeyUploadResult 
                            { 
                                Success = false, 
                                Error = "Incorrect password. Please try again.",
                                NeedsPassword = true
                            };
                        }
                        else if (isPasswordNeeded && string.IsNullOrEmpty(password))
                        {
                            // Password needed but not provided
                            Log("Password required but not provided");
                            return new KeyUploadResult 
                            { 
                                Success = false, 
                                Error = "Private key is password protected",
                                NeedsPassword = true
                            };
                        }
                        else if (isPasswordNeeded && !string.IsNullOrEmpty(password))
                        {
                            // Password provided but might be wrong (generic decrypt error)
                            Log("Password provided but decryption failed - may be incorrect");
                            return new KeyUploadResult 
                            { 
                                Success = false, 
                                Error = "Incorrect password. Please try again.",
                                NeedsPassword = true
                            };
                        }
                        else
                        {
                            // Other cryptographic error
                            Log($"Other cryptographic error: {errorMsg}");
                            return new KeyUploadResult 
                            { 
                                Success = false, 
                                Error = $"Failed to sign challenge: {errorMsg}",
                                NeedsPassword = false
                            };
                        }
                    }
                    catch (Exception signEx)
                    {
                        Log($"Failed to sign challenge: {signEx.Message}");
                        var errorMsg = signEx.Message;
                        var isPasswordError = errorMsg.Contains("password", StringComparison.OrdinalIgnoreCase) || 
                                            errorMsg.Contains("decrypt", StringComparison.OrdinalIgnoreCase);
                        
                        return new KeyUploadResult 
                        { 
                            Success = false, 
                            Error = $"Failed to sign challenge: {errorMsg}",
                            NeedsPassword = isPasswordError
                        };
                    }
                }
                
                // Ensure authenticated if Azure AD is enabled
                if (_authManager != null && _authManager.IsEnabled)
                {
                    if (!await _authManager.EnsureAuthenticatedAsync())
                    {
                        Log("Azure AD authentication required but failed.");
                        return new KeyUploadResult { Success = false, Error = "Authentication required" };
                    }
                }
                
                var effectiveUsername = GetEffectiveUsername();
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(effectiveUsername)}/keys";
                Log($"Uploading key to: {url}");
                
                var payload = JsonSerializer.Serialize(new
                {
                    key_type = key.KeyType,
                    key_data = key.KeyData,
                    key_name = key.KeyName,
                    fingerprint = key.Fingerprint,
                    challenge = challenge,
                    signature = signature
                }, _jsonOptions);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                
                // Add Azure AD token if available
                if (_authManager != null && _authManager.IsEnabled)
                {
                    var token = _authManager.GetAccessToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        Log("Added Azure AD bearer token to request.");
                    }
                }
                
                Log($"=== Starting HTTP upload request ===");
                Log($"Request URL: {url}");
                Log($"Request method: POST");
                Log($"Payload size: {payload.Length} bytes");
                Log($"Has challenge: {!string.IsNullOrEmpty(challenge)}");
                Log($"Has signature: {!string.IsNullOrEmpty(signature)}");
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for upload
                
                try
                {
                    Log($"Sending HTTP request (timeout: 30s)...");
                    var requestStartTime = DateTime.Now;
                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    var requestDuration = DateTime.Now - requestStartTime;
                    Log($"HTTP request completed in {requestDuration.TotalMilliseconds:F0}ms");
                    
                    Log($"Server responded with status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"Key {key.KeyName} uploaded successfully (verified: {signature != null}).");
                        
                        // Save to local uploaded keys
                        SaveUploadedKey(key);
                        return new KeyUploadResult { Success = true, Verified = signature != null };
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync(cts.Token);
                        Log($"Failed to upload key: {response.StatusCode} - {error}");
                        
                        // Check if it's a signature verification failure
                        if (error.Contains("signature") || error.Contains("verification"))
                        {
                            return new KeyUploadResult 
                            { 
                                Success = false, 
                                Error = "Signature verification failed - do you have the correct private key?",
                                NeedsPassword = error.Contains("password")
                            };
                        }
                        
                        return new KeyUploadResult { Success = false, Error = error };
                    }
                }
                catch (TaskCanceledException)
                {
                    Log("Upload request timed out after 30 seconds.");
                    return new KeyUploadResult { Success = false, Error = "Upload timed out. Please check your network connection and try again." };
                }
                catch (OperationCanceledException)
                {
                    Log("Upload request was cancelled.");
                    return new KeyUploadResult { Success = false, Error = "Upload was cancelled." };
                }
            }
            catch (Exception ex)
            {
                Log($"Error uploading key: {ex.Message}");
                return new KeyUploadResult { Success = false, Error = ex.Message };
            }
        }
        
        /// <summary>
        /// Request a challenge from the server for proof of possession.
        /// </summary>
        private async Task<string?> RequestChallengeAsync(string fingerprint, CancellationToken ct)
        {
            try
            {
                var effectiveUsername = GetEffectiveUsername();
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(effectiveUsername)}/challenge";
                Log($"Requesting challenge from: {url}");
                
                var payload = JsonSerializer.Serialize(new { fingerprint }, _jsonOptions);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                
                // Add Azure AD token if available
                if (_authManager != null && _authManager.IsEnabled)
                {
                    var token = _authManager.GetAccessToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        Log("Added Azure AD bearer token to challenge request.");
                    }
                }
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15 second timeout for challenge
                
                try
                {
                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    Log($"Challenge request responded with status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cts.Token);
                        var result = JsonSerializer.Deserialize<ChallengeResponse>(json, _jsonOptions);
                        Log($"Challenge received successfully.");
                        return result?.Challenge;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync(cts.Token);
                        Log($"Challenge request failed: {response.StatusCode} - {error}");
                    }
                }
                catch (TaskCanceledException)
                {
                    Log("Challenge request timed out after 15 seconds.");
                    return null;
                }
                catch (OperationCanceledException)
                {
                    Log("Challenge request was cancelled.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log($"Error requesting challenge: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
            
            return null;
        }

        /// <summary>
        /// Mark a key as ignored (user chose not to upload).
        /// </summary>
        public void IgnoreKey(PublicKeyInfo key)
        {
            if (string.IsNullOrEmpty(key.Fingerprint)) return;
            
            try
            {
                var ignoredKeys = LoadIgnoredKeys();
                if (!ignoredKeys.Contains(key.Fingerprint))
                {
                    ignoredKeys.Add(key.Fingerprint);
                    SaveIgnoredKeys(ignoredKeys);
                    Log($"Key {key.KeyName} marked as ignored.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error ignoring key: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a key from the ignored list.
        /// </summary>
        public void UnignoreKey(string fingerprint)
        {
            try
            {
                var ignoredKeys = LoadIgnoredKeys();
                if (ignoredKeys.Remove(fingerprint))
                {
                    SaveIgnoredKeys(ignoredKeys);
                    Log($"Key unignored: {fingerprint}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error unignoring key: {ex.Message}");
            }
        }

        /// <summary>
        /// Get keys that have been uploaded to the server for this user.
        /// </summary>
        public async Task<List<PublicKeyInfo>> GetServerKeysAsync(CancellationToken ct = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_username))
            {
                return new List<PublicKeyInfo>();
            }

            try
            {
                // Ensure authenticated if Azure AD is enabled
                if (_authManager != null && _authManager.IsEnabled)
                {
                    if (!await _authManager.EnsureAuthenticatedAsync())
                    {
                        Log("Azure AD authentication required but failed.");
                        return new List<PublicKeyInfo>();
                    }
                }
                
                var effectiveUsername = GetEffectiveUsername();
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(effectiveUsername)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                
                // Add Azure AD token if available
                if (_authManager != null && _authManager.IsEnabled)
                {
                    var token = _authManager.GetAccessToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                }
                
                using var response = await _httpClient.SendAsync(request, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var result = JsonSerializer.Deserialize<KeyringResponse>(json, _jsonOptions);
                    return result?.Keys ?? new List<PublicKeyInfo>();
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching server keys: {ex.Message}");
            }
            
            return new List<PublicKeyInfo>();
        }
        
        /// <summary>
        /// Delete a key from the server.
        /// </summary>
        public async Task<bool> DeleteKeyAsync(string keyId, CancellationToken ct = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_baseUrl))
            {
                return false;
            }

            try
            {
                // Ensure authenticated if Azure AD is enabled
                if (_authManager != null && _authManager.IsEnabled)
                {
                    if (!await _authManager.EnsureAuthenticatedAsync())
                    {
                        Log("Azure AD authentication required but failed.");
                        return false;
                    }
                }
                
                var effectiveUsername = GetEffectiveUsername();
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(effectiveUsername)}/keys/{Uri.EscapeDataString(keyId)}";
                Log($"Deleting key from: {url}");
                
                using var request = new HttpRequestMessage(HttpMethod.Delete, url);
                
                // Add Azure AD token if available
                if (_authManager != null && _authManager.IsEnabled)
                {
                    var token = _authManager.GetAccessToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        Log("Added Azure AD bearer token to delete request.");
                    }
                }
                
                using var response = await _httpClient.SendAsync(request, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"Key {keyId} deleted successfully.");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    Log($"Failed to delete key: {response.StatusCode} - {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error deleting key: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a summary of key status for the Manage Keys dialog.
        /// </summary>
        public async Task<KeySummary> GetKeySummaryAsync(CancellationToken ct = default)
        {
            var summary = new KeySummary();
            
            try
            {
                summary.LocalKeys = DetectLocalKeys();
                summary.ServerKeys = await GetServerKeysAsync(ct);
                summary.IgnoredFingerprints = LoadIgnoredKeys();
                
                // Categorize local keys
                var serverFingerprints = summary.ServerKeys
                    .Where(k => !string.IsNullOrEmpty(k.Fingerprint))
                    .Select(k => k.Fingerprint!)
                    .ToHashSet();
                
                foreach (var key in summary.LocalKeys)
                {
                    if (string.IsNullOrEmpty(key.Fingerprint)) continue;
                    
                    if (serverFingerprints.Contains(key.Fingerprint))
                    {
                        summary.UploadedLocalKeys.Add(key);
                    }
                    else if (summary.IgnoredFingerprints.Contains(key.Fingerprint))
                    {
                        summary.IgnoredLocalKeys.Add(key);
                    }
                    else
                    {
                        summary.PendingLocalKeys.Add(key);
                    }
                }
                
                // Find server keys not present locally
                var localFingerprints = summary.LocalKeys
                    .Where(k => !string.IsNullOrEmpty(k.Fingerprint))
                    .Select(k => k.Fingerprint!)
                    .ToHashSet();
                
                summary.ServerOnlyKeys = summary.ServerKeys
                    .Where(k => !string.IsNullOrEmpty(k.Fingerprint) && !localFingerprints.Contains(k.Fingerprint!))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log($"Error getting key summary: {ex.Message}");
            }
            
            return summary;
        }

        private async Task<HashSet<string>> GetUploadedKeyFingerprintsAsync(CancellationToken ct)
        {
            var fingerprints = new HashSet<string>();
            
            try
            {
                var serverKeys = await GetServerKeysAsync(ct);
                foreach (var key in serverKeys)
                {
                    if (!string.IsNullOrEmpty(key.Fingerprint))
                    {
                        fingerprints.Add(key.Fingerprint);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting uploaded fingerprints: {ex.Message}");
            }
            
            return fingerprints;
        }

        private HashSet<string> LoadIgnoredKeys()
        {
            try
            {
                if (File.Exists(IgnoredKeysPath))
                {
                    var json = File.ReadAllText(IgnoredKeysPath);
                    var list = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
                    return list != null ? new HashSet<string>(list) : new HashSet<string>();
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading ignored keys: {ex.Message}");
            }
            
            return new HashSet<string>();
        }

        private void SaveIgnoredKeys(HashSet<string> ignoredKeys)
        {
            try
            {
                EnsureDataDir();
                var json = JsonSerializer.Serialize(ignoredKeys.ToList(), _jsonOptions);
                File.WriteAllText(IgnoredKeysPath, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving ignored keys: {ex.Message}");
            }
        }

        private void SaveUploadedKey(PublicKeyInfo key)
        {
            try
            {
                EnsureDataDir();
                var uploadedKeys = LoadUploadedKeysLocal();
                if (!string.IsNullOrEmpty(key.Fingerprint) && !uploadedKeys.Any(k => k.Fingerprint == key.Fingerprint))
                {
                    uploadedKeys.Add(key);
                    var json = JsonSerializer.Serialize(uploadedKeys, _jsonOptions);
                    File.WriteAllText(UploadedKeysPath, json);
                }
            }
            catch (Exception ex)
            {
                Log($"Error saving uploaded key: {ex.Message}");
            }
        }

        private List<PublicKeyInfo> LoadUploadedKeysLocal()
        {
            try
            {
                if (File.Exists(UploadedKeysPath))
                {
                    var json = File.ReadAllText(UploadedKeysPath);
                    return JsonSerializer.Deserialize<List<PublicKeyInfo>>(json, _jsonOptions) ?? new List<PublicKeyInfo>();
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading uploaded keys: {ex.Message}");
            }
            
            return new List<PublicKeyInfo>();
        }

        private void EnsureDataDir()
        {
            if (!Directory.Exists(UserDataPath))
            {
                Directory.CreateDirectory(UserDataPath);
            }
        }

        private static string ComputeKeyFingerprint(string keyData)
        {
            try
            {
                // Parse SSH public key format: "type base64data comment"
                var parts = keyData.Trim().Split(' ');
                if (parts.Length < 2) return "";
                
                var base64Data = parts[1];
                var keyBytes = Convert.FromBase64String(base64Data);
                
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(keyBytes);
                return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
            }
            catch
            {
                // If we can't compute fingerprint, use a hash of the whole key
                try
                {
                    using var sha256 = SHA256.Create();
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
                    return "HASH:" + Convert.ToBase64String(hash).TrimEnd('=');
                }
                catch
                {
                    return "";
                }
            }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }

    /// <summary>
    /// Response from the keyring API.
    /// </summary>
    public class KeyringResponse
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        
        [JsonPropertyName("keys")]
        public List<PublicKeyInfo> Keys { get; set; } = new();
    }
    
    /// <summary>
    /// Response from the challenge endpoint.
    /// </summary>
    public class ChallengeResponse
    {
        [JsonPropertyName("challenge")]
        public string? Challenge { get; set; }
    }
    
    /// <summary>
    /// Result of a key upload attempt.
    /// </summary>
    public class KeyUploadResult
    {
        public bool Success { get; set; }
        public bool Verified { get; set; }
        public bool NeedsPassword { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Summary of key status for the Manage Keys dialog.
    /// </summary>
    public class KeySummary
    {
        public List<PublicKeyInfo> LocalKeys { get; set; } = new();
        public List<PublicKeyInfo> ServerKeys { get; set; } = new();
        public HashSet<string> IgnoredFingerprints { get; set; } = new();
        
        // Categorized local keys
        public List<PublicKeyInfo> UploadedLocalKeys { get; set; } = new();
        public List<PublicKeyInfo> IgnoredLocalKeys { get; set; } = new();
        public List<PublicKeyInfo> PendingLocalKeys { get; set; } = new();
        
        // Keys on server but not locally
        public List<PublicKeyInfo> ServerOnlyKeys { get; set; } = new();
    }
}

