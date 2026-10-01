using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using VoiceForge.Models;

namespace VoiceForge.Services
{
    /// <summary>
    /// Loads/saves AppSettings as JSON under %AppData%\VoiceForge\settings.json.
    /// Uses JavaScriptSerializer (System.Web.Extensions) - no extra NuGet needed.
    /// </summary>
    public class SettingsService
    {
        private static readonly string AppDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceForge");

        private static readonly string FilePath = Path.Combine(AppDir, "settings.json");

        public AppSettings Settings { get; private set; }

        public SettingsService()
        {
            Settings = Load();
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    AppSettings s = ser.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (s.SoundboardFiles == null) s.SoundboardFiles = new List<string>();
                        if (string.IsNullOrEmpty(s.Theme)) s.Theme = "dark";
                        if (string.IsNullOrEmpty(s.SelectedVoiceId)) s.SelectedVoiceId = "helium";
                        if (s.LatencyMs < 40 || s.LatencyMs > 200) s.LatencyMs = 70;
                        if (s.MasterVolume <= 0f || s.MasterVolume > 2f) s.MasterVolume = 1.0f;
                        return s;
                    }
                }
            }
            catch
            {
                // Corrupt settings should never prevent the app from starting.
            }
            return new AppSettings();
        }

        public void ResetToDefaults()
        {
            Settings = new AppSettings();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppDir)) Directory.CreateDirectory(AppDir);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(Settings);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Saving must never crash the app (e.g. disk full).
            }
        }

        public static string RecordingsFolder
        {
            get
            {
                string dir = Path.Combine(AppDir, "Recordings");
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                catch { }
                return dir;
            }
        }
    }
}
