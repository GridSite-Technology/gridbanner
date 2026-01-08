using System.Text.Json.Serialization;

namespace GridBanner
{
    // Matches the JSON alert file schema.
    public sealed class AlertPayload
    {
        [JsonPropertyName("level")]
        public string? Level { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("background_color")]
        public string? BackgroundColor { get; set; }

        [JsonPropertyName("foreground_color")]
        public string? ForegroundColor { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        // Optional contact info
        [JsonPropertyName("alert_contact_name")]
        public string? AlertContactName { get; set; }

        [JsonPropertyName("alert_contact_phone")]
        public string? AlertContactPhone { get; set; }

        [JsonPropertyName("alert_contact_email")]
        public string? AlertContactEmail { get; set; }

        // Can be a Teams deep link (msteams:) or an https link (meeting/chat)
        [JsonPropertyName("alert_contact_teams")]
        public string? AlertContactTeams { get; set; }

        // Optional site filter: if set, only workstations with matching site_name will show this alert
        [JsonPropertyName("site")]
        public string? Site { get; set; }

        // Optional audio file: if set, use this audio file instead of system beep
        [JsonPropertyName("audio_file")]
        public string? AudioFile { get; set; }
    }
}


