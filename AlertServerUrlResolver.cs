using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GridBanner
{
    /// <summary>
    /// Resolves the alert server URL from Azure AD or a discovery endpoint.
    /// </summary>
    public class AlertServerUrlResolver
    {
        private readonly HttpClient _httpClient;
        private readonly AzureAuthManager? _authManager;
        private readonly string? _tenantId;
        
        public event EventHandler<string>? LogMessage;
        
        public AlertServerUrlResolver(AzureAuthManager? authManager, string? tenantId)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _authManager = authManager;
            _tenantId = tenantId;
        }
        
        /// <summary>
        /// Resolve the alert server URL. Tries multiple sources in order:
        /// 1. User's local config (if set, takes precedence)
        /// 2. Azure AD organization discovery endpoint
        /// 3. Default discovery endpoint
        /// </summary>
        public async Task<string?> ResolveAlertServerUrlAsync(string? configuredUrl)
        {
            // If user has configured a URL locally, use it (highest priority)
            if (!string.IsNullOrWhiteSpace(configuredUrl))
            {
                Log($"Using configured alert server URL: {configuredUrl}");
                return configuredUrl;
            }
            
            // Try to fetch from Azure AD-based discovery
            if (_authManager != null && _authManager.IsEnabled && !string.IsNullOrEmpty(_tenantId))
            {
                try
                {
                    // Try to get from a well-known discovery endpoint
                    // This could be enhanced to use Microsoft Graph API in the future
                    var discoveryUrl = await FetchFromDiscoveryEndpointAsync();
                    if (!string.IsNullOrEmpty(discoveryUrl))
                    {
                        Log($"Resolved alert server URL from discovery endpoint: {discoveryUrl}");
                        return discoveryUrl;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error fetching from discovery endpoint: {ex.Message}");
                }
            }
            
            Log("No alert server URL resolved from discovery endpoints.");
            return null;
        }
        
        /// <summary>
        /// Fetch alert server URL from a discovery endpoint.
        /// Tries to fetch from a known discovery server or the current server.
        /// </summary>
        private async Task<string?> FetchFromDiscoveryEndpointAsync()
        {
            // Try to fetch from Microsoft Graph API in the future
            // For now, we'll try common discovery patterns
            
            // Pattern 1: Try well-known discovery endpoints
            // Pattern 2: Try to fetch from a configured discovery server URL
            
            // Common discovery server patterns (can be enhanced)
            var discoveryEndpoints = new[]
            {
                "https://discovery.gridbanner.local/api/alert-server-url",
                "http://localhost:3000/api/alert-server-url",
            };
            
            foreach (var endpoint in discoveryEndpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<AlertServerUrlResponse>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        
                        if (result != null && !string.IsNullOrEmpty(result.AlertServerUrl))
                        {
                            return result.AlertServerUrl;
                        }
                    }
                }
                catch
                {
                    // Try next endpoint
                    continue;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Fetch GridBanner URL from a specific server (requires authentication).
        /// This is the URL that GridBanner clients should use to connect.
        /// </summary>
        public async Task<string?> FetchGridBannerUrlFromServerAsync(string serverUrl)
        {
            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/admin/gridbanner-url";
                
                // Create request with authentication if available
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
                        Log($"Warning: Could not get auth token for GridBanner URL: {ex.Message}");
                        // Continue without auth - might work if endpoint is public
                    }
                }
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<GridBannerUrlResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (result != null && !string.IsNullOrEmpty(result.GridbannerUrl))
                    {
                        Log($"Fetched GridBanner URL from server: {result.GridbannerUrl}");
                        return result.GridbannerUrl;
                    }
                }
                else
                {
                    Log($"Failed to fetch GridBanner URL: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching GridBanner URL from server {serverUrl}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Fetch alert server URL from a specific discovery server.
        /// </summary>
        public async Task<string?> FetchFromServerAsync(string discoveryServerUrl)
        {
            try
            {
                var url = discoveryServerUrl.TrimEnd('/') + "/api/alert-server-url";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<AlertServerUrlResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (result != null && !string.IsNullOrEmpty(result.AlertServerUrl))
                    {
                        return result.AlertServerUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching from discovery server {discoveryServerUrl}: {ex.Message}");
            }
            
            return null;
        }
        
        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
    
    /// <summary>
    /// Response from alert server URL discovery endpoint.
    /// </summary>
    internal class AlertServerUrlResponse
    {
        public string? AlertServerUrl { get; set; }
        public bool Configured { get; set; }
    }
    
    /// <summary>
    /// Response from GridBanner URL endpoint.
    /// </summary>
    internal class GridBannerUrlResponse
    {
        public string? GridbannerUrl { get; set; }
        public bool Success { get; set; }
    }
}

