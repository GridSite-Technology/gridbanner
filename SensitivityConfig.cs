using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GridBanner
{
    /// <summary>
    /// Configuration for sensitivity levels and domain mappings
    /// </summary>
    public class SensitivityConfig
    {
        public List<SensitiveDomainConfig> SensitiveDomains { get; set; } = new();
        public Dictionary<string, SensitivityLevel> ApplicationSensitivityLevels { get; set; } = new();
        public bool ClipboardMonitoringEnabled { get; set; } = true;
        public bool PasteBlockingEnabled { get; set; } = true;
        public bool ShowWarnings { get; set; } = true;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner", "sensitivity.json");

        public static SensitivityConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return System.Text.Json.JsonSerializer.Deserialize<SensitivityConfig>(json) ?? new SensitivityConfig();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading sensitivity config: {ex.Message}");
            }

            // Return default config
            var config = new SensitivityConfig();
            config.SetDefaults();
            return config;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                LogMessage($"Error saving sensitivity config: {ex.Message}");
            }
        }

        private void SetDefaults()
        {
            // Default sensitive domains
            SensitiveDomains.Add(new SensitiveDomainConfig
            {
                Domain = "sharepoint.com",
                SensitivityLevel = SensitivityLevel.Internal
            });
            SensitiveDomains.Add(new SensitiveDomainConfig
            {
                Domain = "office.com",
                SensitivityLevel = SensitivityLevel.Internal
            });
            SensitiveDomains.Add(new SensitiveDomainConfig
            {
                Domain = "microsoft.com",
                SensitivityLevel = SensitivityLevel.Internal
            });
            SensitiveDomains.Add(new SensitiveDomainConfig
            {
                Domain = ".local",
                SensitivityLevel = SensitivityLevel.Internal
            });
            SensitiveDomains.Add(new SensitiveDomainConfig
            {
                Domain = ".internal",
                SensitivityLevel = SensitivityLevel.Internal
            });

            // Default application sensitivity levels
            ApplicationSensitivityLevels["WINWORD"] = SensitivityLevel.None; // Will be overridden by document label
            ApplicationSensitivityLevels["EXCEL"] = SensitivityLevel.None;
            ApplicationSensitivityLevels["POWERPNT"] = SensitivityLevel.None;
            ApplicationSensitivityLevels["OUTLOOK"] = SensitivityLevel.Internal;
        }

        private static void LogMessage(string message)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SensitivityConfig] {message}\n");
            }
            catch { }
        }
    }

    public class SensitiveDomainConfig
    {
        public string Domain { get; set; } = "";
        public SensitivityLevel SensitivityLevel { get; set; }
    }
}
