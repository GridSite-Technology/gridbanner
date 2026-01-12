using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GridBanner
{
    /// <summary>
    /// Fetches global settings from the alert server.
    /// </summary>
    public class GlobalSettingsFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly AzureAuthManager? _authManager;
        private readonly string? _baseUrl;
        
        public event EventHandler<string>? LogMessage;
        
        public GlobalSettingsFetcher(AzureAuthManager? authManager, string? baseUrl)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _authManager = authManager;
            _baseUrl = baseUrl;
        }
        
        /// <summary>
        /// Fetch global settings from the server.
        /// </summary>
        public async Task<GlobalSettings?> FetchGlobalSettingsAsync()
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                Log("No base URL configured for fetching global settings");
                return null;
            }
            
            try
            {
                var url = _baseUrl.TrimEnd('/') + "/api/admin/global-settings";
                
                // Add authentication if available
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (_authManager != null && _authManager.IsEnabled)
                {
                    try
                    {
                        var token = _authManager.GetAccessToken();
                        if (!string.IsNullOrEmpty(token))
                        {
                            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Could not get auth token for global settings: {ex.Message}");
                    }
                }
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var settings = JsonSerializer.Deserialize<GlobalSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (settings != null)
                    {
                        Log($"Fetched global settings: triple_click={settings.TripleClickEnabled}, terminate={settings.TerminateEnabled}, keyring={settings.KeyringEnabled}, tray_only={settings.TrayOnlyMode}");
                        return settings;
                    }
                }
                else
                {
                    Log($"Failed to fetch global settings: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching global settings: {ex.Message}");
            }
            
            return null;
        }
        
        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
    
    /// <summary>
    /// Global settings from the server.
    /// </summary>
    public class GlobalSettings
    {
        public bool? TripleClickEnabled { get; set; }
        public bool? TerminateEnabled { get; set; }
        public bool? KeyringEnabled { get; set; }
        public bool? TrayOnlyMode { get; set; }
    }
}

