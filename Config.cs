using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace AramOverlay
{
    public class AppConfig
    {
        public string DataUrl { get; set; }
        public string FallbackDataUrl { get; set; }
        public string OcrCommand { get; set; }
        public int RefreshMinutes { get; set; }
        public int LayoutVersion { get; set; }

        // Ratios relative to the League client area.
        public double BenchFirstX { get; set; }
        public double BenchPitchX { get; set; }
        public double BenchY { get; set; }
        public int BenchSlots { get; set; }

        public double AllyX { get; set; }
        public double AllyFirstY { get; set; }
        public double AllyPitchY { get; set; }
        public int AllyRows { get; set; }

        public double BadgeFontRatio { get; set; }
        public bool Colorize { get; set; }
        public bool ShowAllyRows { get; set; }
        public bool HideWhenNotForeground { get; set; }
        public bool PreviewOnStart { get; set; }

        public AppConfig()
        {
            DataUrl = "https://raw.githubusercontent.com/Luckking77/aram-data/main/data/latest.json";
            FallbackDataUrl = "https://cdn.jsdelivr.net/gh/Luckking77/aram-data@main/data/latest.json";
            OcrCommand = "";
            RefreshMinutes = 60;
            LayoutVersion = 5;

            BenchFirstX = 0.292;
            BenchPitchX = 0.0315;
            BenchY = 0.090;
            BenchSlots = 10;

            AllyX = 0.180;
            AllyFirstY = 0.394;
            AllyPitchY = 0.093;
            AllyRows = 5;

            BadgeFontRatio = 0.0165;
            Colorize = true;
            ShowAllyRows = true;
            HideWhenNotForeground = true;
            PreviewOnStart = false;
        }

        public static string ConfigDir
        {
            get
            {
                string overrideDir = Environment.GetEnvironmentVariable("ARAM_OVERLAY_DATA_DIR");
                if (!string.IsNullOrEmpty(overrideDir))
                {
                    Directory.CreateDirectory(overrideDir);
                    return overrideDir;
                }

                // Prefer a folder next to the exe so logs/captures are easy to find.
                string localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AramOverlayData");
                try
                {
                    Directory.CreateDirectory(localDir);
                    string probe = Path.Combine(localDir, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return localDir;
                }
                catch
                {
                }

                string roaming = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AramOverlay");
                Directory.CreateDirectory(roaming);
                return roaming;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    AppConfig config = serializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        config.Normalize();
                        config.Save();
                        return config;
                    }
                }
            }
            catch
            {
                // Keep defaults; the app should still start.
            }

            AppConfig defaults = new AppConfig();
            try
            {
                defaults.Save();
            }
            catch
            {
            }
            return defaults;
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
            }
        }

        public void Normalize()
        {
            if (LayoutVersion < 3)
            {
                // 1920x1080 -> 1600x900 / 1280x720 / 1024x576 baseline.
                BenchFirstX = 0.292;
                BenchPitchX = 0.0315;
                BenchY = 0.090;
                BenchSlots = 10;
                AllyX = 0.155;
                AllyFirstY = 0.394;
                AllyPitchY = 0.093;
                AllyRows = 5;
                LayoutVersion = 3;
            }

            if (LayoutVersion < 4)
            {
                BadgeFontRatio = 0.0143;
                LayoutVersion = 4;
            }

            if (LayoutVersion < 5)
            {
                AllyX = 0.180;
                BadgeFontRatio = 0.0165;
                LayoutVersion = 5;
            }

            if (string.IsNullOrEmpty(DataUrl))
            {
                DataUrl = "https://raw.githubusercontent.com/Luckking77/aram-data/main/data/latest.json";
            }
            if (string.IsNullOrEmpty(FallbackDataUrl))
            {
                FallbackDataUrl = "https://cdn.jsdelivr.net/gh/Luckking77/aram-data@main/data/latest.json";
            }
            if (OcrCommand == null)
            {
                OcrCommand = "";
            }
            if (RefreshMinutes < 5)
            {
                RefreshMinutes = 5;
            }
            if (RefreshMinutes > 1440)
            {
                RefreshMinutes = 1440;
            }
            if (BenchSlots < 1)
            {
                BenchSlots = 10;
            }
            if (BenchSlots > 10)
            {
                BenchSlots = 10;
            }
            if (AllyRows < 1)
            {
                AllyRows = 5;
            }
            if (AllyRows > 5)
            {
                AllyRows = 5;
            }
            if (BadgeFontRatio < 0.005)
            {
                BadgeFontRatio = 0.005;
            }
            if (BadgeFontRatio > 0.05)
            {
                BadgeFontRatio = 0.05;
            }
        }
    }
}
