using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerManager
{
    public static class AlertFileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string DefaultAlertFilePath => @"C:\gridsite\alert.json";

        public static void EnsureAlertDir(string alertFilePath)
        {
            var dir = Path.GetDirectoryName(alertFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public static void BackupIfExists(string alertFilePath)
        {
            if (!File.Exists(alertFilePath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(alertFilePath) ?? ".";
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(dir, $"alert.json.bak-{timestamp}");
            File.Copy(alertFilePath, backupPath, overwrite: false);
        }

        public static void WriteAlert(string alertFilePath, AlertPreset preset)
        {
            EnsureAlertDir(alertFilePath);
            BackupIfExists(alertFilePath);

            var payload = new Dictionary<string, object?>
            {
                ["level"] = preset.Level,
                ["summary"] = preset.Summary,
                ["background_color"] = preset.BackgroundColor,
                ["foreground_color"] = preset.ForegroundColor,
                ["message"] = preset.Message,
            };

            // Optional contact fields
            if (!string.IsNullOrWhiteSpace(preset.AlertContactName))
                payload["alert_contact_name"] = preset.AlertContactName;
            if (!string.IsNullOrWhiteSpace(preset.AlertContactPhone))
                payload["alert_contact_phone"] = preset.AlertContactPhone;
            if (!string.IsNullOrWhiteSpace(preset.AlertContactEmail))
                payload["alert_contact_email"] = preset.AlertContactEmail;
            if (!string.IsNullOrWhiteSpace(preset.AlertContactTeams))
                payload["alert_contact_teams"] = preset.AlertContactTeams;

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(alertFilePath, json);
        }

        public static void ClearAlert(string alertFilePath)
        {
            EnsureAlertDir(alertFilePath);
            BackupIfExists(alertFilePath);
            File.WriteAllText(alertFilePath, string.Empty);
        }

        public static AlertPreset ImportFromJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return ImportFromJsonString(json, nameOverride: Path.GetFileNameWithoutExtension(filePath));
        }

        public static AlertPreset ImportFromJsonString(string json, string? nameOverride = null)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Alert JSON is empty.");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string GetString(string prop) =>
                root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() ?? string.Empty
                    : string.Empty;

            var preset = new AlertPreset
            {
                Name = nameOverride ?? "Imported Alert",
                Level = GetString("level"),
                Summary = GetString("summary"),
                BackgroundColor = GetString("background_color"),
                ForegroundColor = GetString("foreground_color"),
                Message = GetString("message"),
                AlertContactName = GetString("alert_contact_name"),
                AlertContactPhone = GetString("alert_contact_phone"),
                AlertContactEmail = GetString("alert_contact_email"),
                AlertContactTeams = GetString("alert_contact_teams"),
            };

            // Minimal validation / defaults
            if (string.IsNullOrWhiteSpace(preset.Level)) preset.Level = "routine";
            if (string.IsNullOrWhiteSpace(preset.BackgroundColor)) preset.BackgroundColor = "#B00020";
            if (string.IsNullOrWhiteSpace(preset.ForegroundColor)) preset.ForegroundColor = "#FFFFFF";

            return preset;
        }
    }
}


