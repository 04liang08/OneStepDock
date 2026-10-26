using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OneStepDock
{
    internal sealed class AppSettings
    {
        public const int MinLabelFontSize = 11;
        public const int MaxLabelFontSize = 26;
        public const int DefaultLabelFontSize = 14;

        public int Mode { get; set; }
        public int DockWidth { get; set; } = 250;
        public int ThemeMode { get; set; }
        public int StaticPreviewRefreshMode { get; set; }
        public int HotkeyModifiers { get; set; } = 0x0001 | 0x0002;
        public int HotkeyKey { get; set; } = 0x20;
        public List<string> PinnedApps { get; set; } = new List<string>();
        public List<string> ExcludedApps { get; set; } = new List<string>();
        public string DockMonitor { get; set; } = "";
        public int HoverPreviewMode { get; set; } = 1;
        public bool ShowAmbiguityLabels { get; set; } = true;

        /// <summary>卡片标签字号（逻辑像素，96 DPI 基准）。</summary>
        public int LabelFontSize { get; set; } = 14;

        /// <summary>窗口身份键 → 用户自定义标签。</summary>
        public Dictionary<string, string> CustomLabels { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class SettingsStore
    {
        private static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OneStepDock");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.json"); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }

            Normalize(settings);
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Normalize(settings);
                System.IO.Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(
                    FilePath,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        private static void Normalize(AppSettings settings)
        {
            settings.Mode = settings.Mode == 1 ? 1 : 0;
            if (settings.DockWidth < 200 || settings.DockWidth > 560)
            {
                settings.DockWidth = 250;
            }

            if (settings.ThemeMode < 0 || settings.ThemeMode > 2)
            {
                settings.ThemeMode = 0;
            }

            settings.StaticPreviewRefreshMode = settings.StaticPreviewRefreshMode == 1 ? 1 : 0;

            const int allowedModifiers = 0x0001 | 0x0002 | 0x0004 | 0x0008;
            settings.HotkeyModifiers &= allowedModifiers;
            if (settings.HotkeyModifiers == 0)
            {
                settings.HotkeyModifiers = 0x0001 | 0x0002;
            }
            if (settings.HotkeyKey <= 0 || settings.HotkeyKey > 0xFF)
            {
                settings.HotkeyKey = 0x20;
            }

            settings.PinnedApps = NormalizeApps(settings.PinnedApps);
            settings.ExcludedApps = NormalizeApps(settings.ExcludedApps);
            settings.DockMonitor = (settings.DockMonitor ?? "").Trim();

            if (settings.HoverPreviewMode < 0 || settings.HoverPreviewMode > 2)
            {
                settings.HoverPreviewMode = 1;
            }

            if (settings.LabelFontSize < AppSettings.MinLabelFontSize ||
                settings.LabelFontSize > AppSettings.MaxLabelFontSize)
            {
                settings.LabelFontSize = AppSettings.DefaultLabelFontSize;
            }

            settings.CustomLabels = NormalizeLabels(settings.CustomLabels);
        }

        private static Dictionary<string, string> NormalizeLabels(
            Dictionary<string, string> labels)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (labels == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, string> pair in labels)
            {
                string key = (pair.Key ?? "").Trim();
                string value = (pair.Value ?? "").Trim();
                if (key.Length == 0 || value.Length == 0)
                {
                    continue;
                }

                if (value.Length > 40)
                {
                    value = value.Substring(0, 40).Trim();
                }

                result[key] = value;
            }

            return result;
        }

        private static List<string> NormalizeApps(List<string> apps)
        {
            var result = new List<string>();
            if (apps == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string app in apps)
            {
                string value = (app ?? "").Trim().ToLowerInvariant();
                if (value.Length == 0 || !seen.Add(value))
                {
                    continue;
                }
                result.Add(value);
            }
            return result;
        }
    }
}
