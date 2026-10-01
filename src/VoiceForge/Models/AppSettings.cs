using System.Collections.Generic;

namespace VoiceForge.Models
{
    /// <summary>
    /// Everything the app remembers between launches. Persisted as JSON in
    /// %AppData%\VoiceForge\settings.json by SettingsService.
    /// </summary>
    public class AppSettings
    {
        public string Theme { get; set; }                 // "dark" | "light"
        public string SelectedVoiceId { get; set; }
        public string MicDeviceName { get; set; }
        public string OutputDeviceName { get; set; }
        public int LatencyMs { get; set; }
        public float MasterVolume { get; set; }
        public bool HotkeysEnabled { get; set; }
        public bool PowerOnStart { get; set; }
        public List<string> SoundboardFiles { get; set; }

        // Custom Voice Lab parameters.
        public double CustomPitch { get; set; }
        public double CustomReverb { get; set; }
        public double CustomDecay { get; set; }
        public double CustomEchoDelay { get; set; }
        public double CustomEchoMix { get; set; }
        public double CustomDrive { get; set; }

        // AI voice (RVC via ONNX Runtime).
        public bool AiEnabled { get; set; }
        public string AiModelFile { get; set; }
        public int AiTranspose { get; set; }
        public int AiBlockMs { get; set; }
        public double AiGain { get; set; }

        public bool WindowMaximized { get; set; }

        public AppSettings()
        {
            Theme = "dark";
            SelectedVoiceId = "helium";
            MicDeviceName = "";
            OutputDeviceName = "";
            LatencyMs = 70;
            MasterVolume = 1.0f;
            HotkeysEnabled = true;
            PowerOnStart = false;
            SoundboardFiles = new List<string>();
            CustomPitch = 0.0;
            CustomReverb = 25.0;
            CustomDecay = 30.0;
            CustomEchoDelay = 200.0;
            CustomEchoMix = 30.0;
            CustomDrive = 0.0;
            AiEnabled = false;
            AiModelFile = "";
            AiTranspose = 0;
            AiBlockMs = 500;
            AiGain = 1.0;
            WindowMaximized = false;
        }
    }
}
