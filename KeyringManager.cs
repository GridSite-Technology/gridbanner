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
        
        // Local storage paths
        private static readonly string UserDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner");
        private static readonly string IgnoredKeysPath = Path.Combine(UserDataPath, "ignored_keys.json");
        private static readonly string UploadedKeysPath = Path.Combine(UserDataPath, "uploaded_keys.json");
        
        // Common SSH key locations
        private static readonly string[] SshKeyPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa.pub"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519.pub"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ecdsa.pub"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_dsa.pub"),
        };

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
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// Configure the keyring manager with the alert server URL and username.
        /// </summary>
        public void Configure(string? alertUrl, string? username, bool enabled)
        {
            _enabled = enabled;
            _username = username;
            
            if (!string.IsNullOrEmpty(alertUrl))
            {
                // Extract base URL from alert URL (remove /api/alert suffix)
                var uri = new Uri(alertUrl);
                _baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            
            Log($"Keyring configured: enabled={enabled}, username={username}, baseUrl={_baseUrl}");
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
        /// Detect SSH public keys from common locations.
        /// </summary>
        public List<PublicKeyInfo> DetectLocalKeys()
        {
            var keys = new List<PublicKeyInfo>();
            
            foreach (var path in SshKeyPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var keyData = File.ReadAllText(path).Trim();
                        if (string.IsNullOrEmpty(keyData)) continue;
                        
                        var parts = keyData.Split(' ');
                        var keyType = parts.Length > 0 ? parts[0] : "unknown";
                        var keyName = Path.GetFileName(path);
                        var fingerprint = ComputeKeyFingerprint(keyData);
                        
                        keys.Add(new PublicKeyInfo
                        {
                            KeyType = keyType,
                            KeyData = keyData,
                            KeyName = keyName,
                            Fingerprint = fingerprint
                        });
                        
                        Log($"Detected key: {keyName} ({keyType})");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error reading key at {path}: {ex.Message}");
                    }
                }
            }
            
            return keys;
        }

        /// <summary>
        /// Upload a public key to the central keyring.
        /// </summary>
        public async Task<bool> UploadKeyAsync(PublicKeyInfo key, CancellationToken ct = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_username))
            {
                Log("Keyring not properly configured for upload.");
                return false;
            }

            try
            {
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(_username)}/keys";
                Log($"Uploading key to: {url}");
                
                var payload = JsonSerializer.Serialize(new
                {
                    key_type = key.KeyType,
                    key_data = key.KeyData,
                    key_name = key.KeyName,
                    fingerprint = key.Fingerprint
                }, _jsonOptions);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                
                using var response = await _httpClient.SendAsync(request, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"Key {key.KeyName} uploaded successfully.");
                    
                    // Save to local uploaded keys
                    SaveUploadedKey(key);
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    Log($"Failed to upload key: {response.StatusCode} - {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error uploading key: {ex.Message}");
                return false;
            }
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
                var url = $"{_baseUrl}/api/keyring/{Uri.EscapeDataString(_username)}";
                using var response = await _httpClient.GetAsync(url, ct);
                
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

