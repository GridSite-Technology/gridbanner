using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Configuration;

namespace GridBanner
{
    /// <summary>
    /// Handles configuration file reading with system/user precedence.
    /// </summary>
    public class ConfigManager
    {
        private static readonly string SystemConfigPath = @"C:\gridbanner\conf.ini";
        private static readonly string UserConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "userdata", "gridbanner", "conf.ini");

        private const string ConfigVersionMarker = "gridbanner-config-version=2";

        private static readonly Dictionary<string, string> DefaultConfig = new()
        {
            { "background_color", "#FFA500" },  // Orange
            { "foreground_color", "#FFFFFF" },  // White
            { "classification_level", "UNSPECIFIED CLASSIFICATION" },
            { "banner_height", "30" },

            // Device compliance indicator (optional check)
            { "compliance_check_enabled", "1" },
            // Conservative default: NOT compliant unless proven compliant by an actual check
            { "compliance_status", "0" },
            { "compliance_check_command", "" }
        };

        /// <summary>
        /// Load configuration with system config overriding user config.
        /// </summary>
        public static Dictionary<string, string> LoadConfig()
        {
            var mergedConfig = new Dictionary<string, string>(DefaultConfig);

            try
            {
                // Load user config first (if exists)
                if (File.Exists(UserConfigPath))
                {
                    TryMigrateOldUserDefaults(UserConfigPath);
                    TryAugmentUserConfigWithMissingDefaults(UserConfigPath);
                    var userConfig = ReadIniFile(UserConfigPath);
                    foreach (var kvp in userConfig)
                    {
                        mergedConfig[kvp.Key] = kvp.Value;
                    }
                }

                // Load system config (overrides user config)
                if (File.Exists(SystemConfigPath))
                {
                    var systemConfig = ReadIniFile(SystemConfigPath);
                    foreach (var kvp in systemConfig)
                    {
                        mergedConfig[kvp.Key] = kvp.Value;
                    }
                }

                // If no config exists, create default user config
                if (!File.Exists(SystemConfigPath) && !File.Exists(UserConfigPath))
                {
                    try
                    {
                        EnsureUserConfigDir();
                        CreateDefaultConfig(UserConfigPath);
                        
                        // Verify the file was created
                        if (File.Exists(UserConfigPath))
                        {
                            // Reload the newly created config
                            var newConfig = ReadIniFile(UserConfigPath);
                            foreach (var kvp in newConfig)
                            {
                                mergedConfig[kvp.Key] = kvp.Value;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Warning: Failed to create config file at {UserConfigPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error creating default config: {ex.Message}");
                        // Continue with default values
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
                // Return defaults if config loading fails
            }

            return mergedConfig;
        }

        private static void EnsureUserConfigDir()
        {
            var dir = Path.GetDirectoryName(UserConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void CreateDefaultConfig(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = new List<string>
            {
                $"; {ConfigVersionMarker}",
                "[Display]",
                $"background_color = {DefaultConfig["background_color"]}",
                $"foreground_color = {DefaultConfig["foreground_color"]}",
                $"classification_level = {DefaultConfig["classification_level"]}",
                $"banner_height = {DefaultConfig["banner_height"]}",
                "; Optional override if Azure AD reports a short tenant name (e.g. set to \"PrecisionX Technology LLC\")",
                "org_name =",
                string.Empty,
                "; Device compliance badge (right side)",
                "; compliance_check_enabled: 1=show badge (and optionally run compliance_check_command), 0=hide badge",
                $"compliance_check_enabled = {DefaultConfig["compliance_check_enabled"]}",
                "; compliance_status: 1=compliant (green), 0=NOT compliant (red). Used only if no command is set or command fails.",
                $"compliance_status = {DefaultConfig["compliance_status"]}",
                "; Optional: command to determine compliance. Exit code 0 => compliant, non-zero => non-compliant.",
                "; Example: compliance_check_command = powershell.exe -NoProfile -Command \"exit 0\"",
                $"compliance_check_command = {DefaultConfig["compliance_check_command"]}"
            };

            File.WriteAllLines(path, lines);
        }

        private static void TryMigrateOldUserDefaults(string path)
        {
            // Only migrate if there's no system config (system-wide config should remain authoritative)
            if (File.Exists(SystemConfigPath))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch
            {
                return;
            }

            // If it's already versioned, don't touch it
            if (lines.Any(l => l.Contains(ConfigVersionMarker, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // Heuristic: if the user config looks exactly like our old autogenerated defaults, upgrade it.
            // This avoids unexpectedly clobbering user-customized config values.
            var cfg = ReadIniFile(path);

            var oldBg = cfg.GetValueOrDefault("background_color");
            var oldFg = cfg.GetValueOrDefault("foreground_color");
            var oldLevel = cfg.GetValueOrDefault("classification_level");
            var oldHeight = cfg.GetValueOrDefault("banner_height");

            var matchesOldDefaults =
                string.Equals(oldBg, "#000080", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldFg, "#FFFFFF", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldLevel, "UNCLASSIFIED", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(oldHeight) || string.Equals(oldHeight, "60", StringComparison.OrdinalIgnoreCase));

            // If any non-default keys exist, assume user customized it
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "background_color",
                "foreground_color",
                "classification_level",
                "banner_height",
                "org_name",
                "compliance_check_enabled",
                "compliance_status",
                "compliance_check_command"
            };
            var hasUnknownKeys = cfg.Keys.Any(k => !allowedKeys.Contains(k));

            if (!matchesOldDefaults || hasUnknownKeys)
            {
                return;
            }

            try
            {
                var backup = path + ".bak";
                if (!File.Exists(backup))
                {
                    File.Copy(path, backup);
                }

                CreateDefaultConfig(path);
            }
            catch
            {
                // ignore migration failures
            }
        }

        private static void TryAugmentUserConfigWithMissingDefaults(string path)
        {
            // Non-destructive: if the user config exists but is missing newer keys, add them without
            // changing any existing values. This helps keep "we don't have a file yet" defaults visible
            // and ensures new knobs (like banner_height / org_name) appear for users.
            try
            {
                var lines = File.ReadAllLines(path).ToList();

                if (!lines.Any(l => l.Contains(ConfigVersionMarker, StringComparison.OrdinalIgnoreCase)))
                {
                    lines.Insert(0, $"; {ConfigVersionMarker}");
                }

                // Find the [Display] section
                var displayIdx = lines.FindIndex(l => l.Trim().Equals("[Display]", StringComparison.OrdinalIgnoreCase));
                if (displayIdx < 0)
                {
                    // Append a Display section with defaults at the end
                    lines.Add(string.Empty);
                    lines.Add("[Display]");
                    lines.Add($"background_color = {DefaultConfig["background_color"]}");
                    lines.Add($"foreground_color = {DefaultConfig["foreground_color"]}");
                    lines.Add($"classification_level = {DefaultConfig["classification_level"]}");
                    lines.Add($"banner_height = {DefaultConfig["banner_height"]}");
                    lines.Add("; Optional override if Azure AD reports a short tenant name (e.g. set to \"PrecisionX Technology LLC\")");
                    lines.Add("org_name =");
                    lines.Add(string.Empty);
                    lines.Add("; Device compliance badge (right side)");
                    lines.Add("; compliance_check_enabled: 1=show badge (and optionally run compliance_check_command), 0=hide badge");
                    lines.Add($"compliance_check_enabled = {DefaultConfig["compliance_check_enabled"]}");
                    lines.Add("; compliance_status: 1=compliant (green), 0=non-compliant (red) (used when no command is set or command fails)");
                    lines.Add($"compliance_status = {DefaultConfig["compliance_status"]}");
                    lines.Add("; Optional: command to determine compliance. Exit code 0 => compliant, non-zero => non-compliant.");
                    lines.Add("; Example: compliance_check_command = powershell.exe -NoProfile -Command \"exit 0\"");
                    lines.Add($"compliance_check_command = {DefaultConfig["compliance_check_command"]}");
                    File.WriteAllLines(path, lines);
                    return;
                }

                bool HasKey(string key)
                {
                    for (var i = displayIdx + 1; i < lines.Count; i++)
                    {
                        var t = lines[i].Trim();
                        if (t.StartsWith("[") && t.EndsWith("]"))
                        {
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(t))
                        {
                            continue;
                        }

                        // Treat commented keys as "present" so we don't spam duplicates.
                        // Example: "; org_name =" should count as existing.
                        var normalized = t.TrimStart(';', '#').TrimStart();
                        var eq = normalized.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }
                        var k = normalized.Substring(0, eq).Trim();
                        if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                // Insert missing keys at the end of the [Display] section (before the next section, if any)
                var insertIdx = lines.Count;
                for (var i = displayIdx + 1; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        insertIdx = i;
                        break;
                    }
                }

                var toInsert = new List<string>();
                if (!HasKey("banner_height"))
                {
                    toInsert.Add($"banner_height = {DefaultConfig["banner_height"]}");
                }
                if (!HasKey("org_name"))
                {
                    toInsert.Add("; Optional override if Azure AD reports a short tenant name (e.g. set to \"PrecisionX Technology LLC\")");
                    toInsert.Add("org_name =");
                }
                if (!HasKey("compliance_check_enabled") || !HasKey("compliance_status") || !HasKey("compliance_check_command"))
                {
                    toInsert.Add(string.Empty);
                    toInsert.Add("; Device compliance badge (right side)");
                    toInsert.Add("; compliance_check_enabled: 1=show badge (and optionally run compliance_check_command), 0=hide badge");

                    if (!HasKey("compliance_check_enabled"))
                    {
                        toInsert.Add($"compliance_check_enabled = {DefaultConfig["compliance_check_enabled"]}");
                    }

                    toInsert.Add("; compliance_status: 1=compliant (green), 0=non-compliant (red) (used when no command is set or command fails)");
                    if (!HasKey("compliance_status"))
                    {
                        toInsert.Add($"compliance_status = {DefaultConfig["compliance_status"]}");
                    }

                    toInsert.Add("; Optional: command to determine compliance. Exit code 0 => compliant, non-zero => non-compliant.");
                    toInsert.Add("; Example: compliance_check_command = powershell.exe -NoProfile -Command \"exit 0\"");
                    if (!HasKey("compliance_check_command"))
                    {
                        toInsert.Add($"compliance_check_command = {DefaultConfig["compliance_check_command"]}");
                    }
                }

                if (toInsert.Count > 0)
                {
                    // Ensure a blank line before we add new knobs if the section already has content
                    if (insertIdx > 0 && insertIdx <= lines.Count && lines[insertIdx - 1].Trim().Length > 0)
                    {
                        toInsert.Insert(0, string.Empty);
                    }
                    lines.InsertRange(insertIdx, toInsert);
                }

                // De-duplicate org_name blocks within the [Display] section (keep first occurrence).
                // This can happen if older versions wrote commented org_name and augmentation added another.
                var seenOrg = false;
                var seenOrgComment = false;
                var seenComplianceComment = false;
                for (var i = displayIdx + 1; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        break;
                    }

                    if (t.Contains("Optional override if Azure AD reports a short tenant name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenOrgComment)
                        {
                            lines[i] = string.Empty;
                        }
                        else
                        {
                            seenOrgComment = true;
                        }
                        continue;
                    }

                    if (t.Contains("Device compliance badge", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenComplianceComment)
                        {
                            lines[i] = string.Empty;
                        }
                        else
                        {
                            seenComplianceComment = true;
                        }
                        continue;
                    }

                    var normalized = t.TrimStart(';', '#').TrimStart();
                    var eq = normalized.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = normalized.Substring(0, eq).Trim();
                    if (k.Equals("org_name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenOrg)
                        {
                            lines[i] = string.Empty;
                        }
                        else
                        {
                            // Ensure it's an active key (not commented)
                            var value = normalized[(eq + 1)..].Trim();
                            lines[i] = $"org_name = {value}".TrimEnd();
                            seenOrg = true;
                        }
                    }
                }

                // Write back only if we changed anything (insertions or deduping)
                if (toInsert.Count > 0 || seenOrgComment || seenOrg)
                {
                    File.WriteAllLines(path, lines.Where(l => l != null).ToArray());
                }
            }
            catch
            {
                // ignore augmentation failures
            }
        }

        private static Dictionary<string, string> ReadIniFile(string path)
        {
            var config = new Dictionary<string, string>();

            if (!File.Exists(path))
                return config;

            var lines = File.ReadAllLines(path);
            string? currentSection = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                    continue;

                // Check for section
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    continue;
                }

                // Parse key = value
                var equalIndex = trimmedLine.IndexOf('=');
                if (equalIndex > 0 && currentSection == "Display")
                {
                    var key = trimmedLine.Substring(0, equalIndex).Trim();
                    var value = trimmedLine.Substring(equalIndex + 1).Trim();
                    config[key] = value;
                }
            }

            return config;
        }
    }
}

