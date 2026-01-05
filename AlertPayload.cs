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
    }
}


