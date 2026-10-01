using System;
using System.IO;
using System.Web.Script.Serialization;

namespace VoiceForge.AI
{
    /// <summary>
    /// Sidecar configuration that travels with every ONNX voice model
    /// ("MyVoice.onnx" + "MyVoice.json"). Every ONNX export names its
    /// inputs/outputs slightly differently, so instead of hard-coding anything
    /// the loader reads the names from here. Missing sidecar = sane RVC v2
    /// 40k defaults.
    ///
    /// Frame-rate contract: HuBERT emits one feature frame per 20 ms
    /// (320 samples @ 16 kHz). The generator hop is whatever the sidecar says;
    /// AiVoiceStage interpolates features + F0 to the generator's frame grid,
    /// so output duration always matches input duration for any hop/rate pair.
    /// </summary>
    public class AiModelConfig
    {
        // ---------- model geometry ----------
        public int SampleRate = 40000;     // model's native sample rate
        public int HopLength = 480;        // synthesis hop at the model rate
        public int SpeakerId = 0;          // multi-speaker models: speaker index

        // ---------- pitch tracking ----------
        public double F0Min = 65.0;        // Hz
        public double F0Max = 1100.0;      // Hz
        public double YinThreshold = 0.15;

        // ---------- HuBERT feature extractor ----------
        public string HubertFile = "hubert.onnx";
        public string HubertInput = "source";
        public string HubertOutput = "embed";

        // ---------- RVC generator tensor names (standard export) ----------
        public string GenInputPhone = "phone";
        public string GenInputPhoneLength = "phone_length";
        public string GenInputPitch = "pitch";
        public string GenInputPitchLength = "pitch_length";
        public string GenInputPitchf = "pitchf";
        public string GenInputPitchfLength = "pitchf_length";
        public string GenInputSid = "sid";
        public string GenOutput = "audio";

        /// <summary>True: length inputs are long[1] tensors; false: long[0] scalars.</summary>
        public bool LengthsAsScalar = true;

        // ---------- persistence ----------

        public static string SidecarPathFor(string onnxPath)
        {
            return onnxPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                ? onnxPath.Substring(0, onnxPath.Length - 5) + ".json"
                : onnxPath + ".json";
        }

        public static AiModelConfig Load(string onnxPath)
        {
            AiModelConfig cfg = new AiModelConfig();
            string sidecar = SidecarPathFor(onnxPath);
            try
            {
                if (File.Exists(sidecar))
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    AiModelConfig loaded = ser.Deserialize<AiModelConfig>(File.ReadAllText(sidecar));
                    if (loaded != null) cfg = loaded;
                }
            }
            catch
            {
                // A broken sidecar falls back to defaults rather than failing the load.
            }

            if (cfg.SampleRate < 8000) cfg.SampleRate = 8000;
            if (cfg.SampleRate > 96000) cfg.SampleRate = 96000;
            if (cfg.HopLength < 64) cfg.HopLength = 64;
            if (cfg.HopLength > 4096) cfg.HopLength = 4096;
            if (cfg.SpeakerId < 0) cfg.SpeakerId = 0;
            if (cfg.F0Min < 30) cfg.F0Min = 30;
            if (cfg.F0Max <= cfg.F0Min) cfg.F0Max = 1100;
            return cfg;
        }

        /// <summary>Writes a starter sidecar next to a model that has none.</summary>
        public static void WriteTemplate(string onnxPath)
        {
            try
            {
                string sidecar = SidecarPathFor(onnxPath);
                if (!File.Exists(sidecar))
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    File.WriteAllText(sidecar, ser.Serialize(new AiModelConfig()));
                }
            }
            catch
            {
                // Non-fatal: the in-memory defaults still work.
            }
        }
    }
}
