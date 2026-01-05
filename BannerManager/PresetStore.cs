using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BannerManager
{
    public sealed class PresetStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public string StorePath { get; }

        public PresetStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GridBanner",
                "BannerManager");
            Directory.CreateDirectory(dir);
            StorePath = Path.Combine(dir, "presets.json");
        }

        public List<AlertPreset> Load()
        {
            if (!File.Exists(StorePath))
            {
                return new List<AlertPreset>();
            }

            try
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<AlertPreset>>(json, JsonOptions);
                return list ?? new List<AlertPreset>();
            }
            catch
            {
                return new List<AlertPreset>();
            }
        }

        public void Save(List<AlertPreset> presets)
        {
            var json = JsonSerializer.Serialize(presets, JsonOptions);
            File.WriteAllText(StorePath, json);
        }
    }
}


