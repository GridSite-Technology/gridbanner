using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BannerManager
{
    public static class AlertServerService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public static async Task<bool> UpdateAlertAsync(string serverUrl, string adminKey, AlertPreset preset)
        {
            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/alert";
                var alert = new
                {
                    level = preset.Level,
                    summary = preset.Summary,
                    message = preset.Message,
                    background_color = preset.BackgroundColor,
                    foreground_color = preset.ForegroundColor,
                    alert_contact_name = string.IsNullOrWhiteSpace(preset.AlertContactName) ? null : preset.AlertContactName,
                    alert_contact_phone = string.IsNullOrWhiteSpace(preset.AlertContactPhone) ? null : preset.AlertContactPhone,
                    alert_contact_email = string.IsNullOrWhiteSpace(preset.AlertContactEmail) ? null : preset.AlertContactEmail,
                    alert_contact_teams = string.IsNullOrWhiteSpace(preset.AlertContactTeams) ? null : preset.AlertContactTeams,
                    site = string.IsNullOrWhiteSpace(preset.Site) ? null : preset.Site
                };

                var json = JsonSerializer.Serialize(alert, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Admin-Key", adminKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> ClearAlertAsync(string serverUrl, string adminKey)
        {
            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/alert";
                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Headers.Add("X-Admin-Key", adminKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}

