using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace EnvSecured.WinForms
{
    internal sealed class RecentProjectsService
    {
        private readonly string settingsPath;

        public RecentProjectsService()
        {
            settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EnvSecured",
                "settings.json");
        }

        public List<string> Load()
        {
            return LoadSettings().RecentProjects
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        public string LoadLastProject()
        {
            var path = LoadSettings().LastProjectPath;
            return File.Exists(path) ? path : null;
        }

        public WindowBounds LoadWindowBounds()
        {
            return LoadSettings().WindowBounds;
        }

        public Dictionary<string, int> LoadGridColumnWidths(string gridKey)
        {
            var settings = LoadSettings();
            if (settings.GridColumnWidths == null || !settings.GridColumnWidths.ContainsKey(gridKey))
            {
                return new Dictionary<string, int>();
            }
            return new Dictionary<string, int>(settings.GridColumnWidths[gridKey]);
        }

        public double? LoadSplitterRatio(string splitterKey)
        {
            var settings = LoadSettings();
            if (settings.SplitterRatios == null || !settings.SplitterRatios.ContainsKey(splitterKey))
            {
                return null;
            }
            return settings.SplitterRatios[splitterKey];
        }

        public bool LoadBooleanPreference(string key, bool defaultValue)
        {
            var settings = LoadSettings();
            if (settings.BooleanPreferences == null || !settings.BooleanPreferences.ContainsKey(key))
            {
                return defaultValue;
            }
            return settings.BooleanPreferences[key];
        }

        private Settings LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new Settings();
                return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        public void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var settings = LoadSettings();
            var recent = settings.RecentProjects
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            recent.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            recent.Insert(0, path);
            settings.LastProjectPath = path;
            settings.RecentProjects = recent.Take(10).ToList();
            Save(settings);
        }

        public void SaveWindowBounds(WindowBounds bounds)
        {
            var settings = LoadSettings();
            settings.WindowBounds = bounds;
            Save(settings);
        }

        public void SaveGridColumnWidths(string gridKey, Dictionary<string, int> widths)
        {
            var settings = LoadSettings();
            settings.GridColumnWidths = settings.GridColumnWidths ?? new Dictionary<string, Dictionary<string, int>>();
            settings.GridColumnWidths[gridKey] = widths;
            Save(settings);
        }

        public void SaveSplitterRatio(string splitterKey, double ratio)
        {
            var settings = LoadSettings();
            settings.SplitterRatios = settings.SplitterRatios ?? new Dictionary<string, double>();
            settings.SplitterRatios[splitterKey] = ratio;
            Save(settings);
        }

        public void SaveBooleanPreference(string key, bool value)
        {
            var settings = LoadSettings();
            settings.BooleanPreferences = settings.BooleanPreferences ?? new Dictionary<string, bool>();
            settings.BooleanPreferences[key] = value;
            Save(settings);
        }

        public void ResetLayoutSettings()
        {
            var settings = LoadSettings();
            settings.WindowBounds = null;
            settings.GridColumnWidths = new Dictionary<string, Dictionary<string, int>>();
            settings.SplitterRatios = new Dictionary<string, double>();
            Save(settings);
        }

        private void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var json = new JavaScriptSerializer().Serialize(settings);
            File.WriteAllText(settingsPath, json);
        }

        private sealed class Settings
        {
            public string LastProjectPath { get; set; }
            public List<string> RecentProjects { get; set; } = new List<string>();
            public WindowBounds WindowBounds { get; set; }
            public Dictionary<string, Dictionary<string, int>> GridColumnWidths { get; set; } = new Dictionary<string, Dictionary<string, int>>();
            public Dictionary<string, double> SplitterRatios { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, bool> BooleanPreferences { get; set; } = new Dictionary<string, bool>();
        }
    }

    internal sealed class WindowBounds
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }
}
