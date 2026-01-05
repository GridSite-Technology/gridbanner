namespace BannerManager
{
    public sealed class AlertPreset
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New Alert";

        // Alert payload
        public string Level { get; set; } = "routine"; // routine|urgent|critical|super_critical
        public string Summary { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = "#B00020";
        public string ForegroundColor { get; set; } = "#FFFFFF";

        // Optional contact info
        public string AlertContactName { get; set; } = string.Empty;
        public string AlertContactPhone { get; set; } = string.Empty;
        public string AlertContactEmail { get; set; } = string.Empty;
        public string AlertContactTeams { get; set; } = string.Empty;
    }
}


