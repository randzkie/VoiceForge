using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace VoiceForge.AI
{
    /// <summary>
    /// Streaming bridge between the 48 kHz voice engine and the ONNX voice.
    ///
    /// The audio thread only ever copies samples in/out of two FIFOs; all
    /// inference happens on a worker thread, so a slow CPU degrades into a
    /// little extra silence instead of stuttering the whole engine.
    ///
    /// Chunk timing uses "carry" buffers so every sample eventually reaches
    /// the models exactly once: HuBERT consumes whole 320-sample frames and
    /// the generator consumes whole hop-sized frames, leftovers waiting for
    /// the next chunk. Output duration therefore matches input duration with
    /// no cumulative drift and no boundary clicks.
    /// </summary>
    public class AiVoiceStage : IDisposable
    {
        private static readonly string AppDataModels = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceForge", "models");
        private static readonly string ExeModels = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        private readonly object gate = new object();
        private readonly List<float> inFifo = new List<float>(1 << 16);   // 48 kHz mic audio
        private readonly List<float> outFifo = new List<float>(1 << 16);  // 48 kHz converted audio

        private readonly List<float> hubCarry = new List<float>();        // 16 kHz, sub-frame remainder
        private readonly List<float> modelCarry = new List<float>();      // model rate, sub-hop remainder

        private Thread worker;
        private volatile bool workerAlive;
        private bool enabled;
        private int blockMs = 500;
        private int transpose;
        private double gain = 1.0;

        private OnnxRvcVoice voice;
        private AiModelConfig cfg;
        private YinPitchTracker pitch;
        private LinearResampler toHubert;      // 48k -> 16k
        private LinearResampler toModel;       // 48k -> model rate
        private LinearResampler toEngine;      // model rate -> 48k

        public bool IsReady { get; private set; }
        public string LoadedModelPath { get; private set; }
        public string LoadedModelName { get; private set; }
        public string LastError { get; private set; }

        /// <summary>Raised on load/unload/errors. Handlers must marshal to the UI thread.</summary>
        public event Action<string> StatusChanged;

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                ClearQueues();
            }
        }

        public int Transpose
        {
            get { return transpose; }
            set { transpose = Math.Max(-12, Math.Min(12, value)); }
        }

        public int BlockMs
        {
            get { return blockMs; }
            set
            {
                int v = value < 120 ? 120 : (value > 2000 ? 2000 : value);
                if (blockMs == v) return;
                blockMs = v;
                ClearQueues();
            }
        }

        public double Gain
        {
            get { return gain; }
            set { gain = Math.Max(0.0, Math.Min(2.0, value)); }
        }

        // ---------- model discovery ----------

        public static string PrimaryModelsFolder
        {
            get
            {
                try { if (!Directory.Exists(AppDataModels)) Directory.CreateDirectory(AppDataModels); }
                catch { }
                return AppDataModels;
            }
        }

        public static string[] SearchFolders
        {
            get { return new[] { AppDataModels, ExeModels }; }
        }

        /// <summary>
        /// True when a file name looks like the shared feature extractor
        /// (HuBERT / ContentVec export) rather than a voice generator.
        /// Covers hubert*.onnx, contentvec*.onnx, content-encoder.onnx and
        /// vec-256-layer-9.onnx / vec-768-layer-12.onnx style downloads.
        /// </summary>
        public static bool IsExtractorName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string lower = Path.GetFileName(fileName).ToLowerInvariant();
            if (lower.IndexOf("hubert", StringComparison.Ordinal) >= 0) return true;
            if (lower.IndexOf("contentvec", StringComparison.Ordinal) >= 0) return true;
            if (lower.IndexOf("content-encoder", StringComparison.Ordinal) >= 0) return true;
            if (lower.StartsWith("vec-", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>All candidate voice models (*.onnx except the feature extractor).</summary>
        public static List<string> ScanModels()
        {
            List<string> found = new List<string>();
            string[] folders = SearchFolders;
            for (int f = 0; f < folders.Length; f++)
            {
                try
                {
                    if (!Directory.Exists(folders[f])) continue;
                    foreach (string file in Directory.GetFiles(folders[f], "*.onnx"))
                    {
                        if (IsExtractorName(file)) continue;
                        if (!found.Contains(file)) found.Add(file);
                    }
                }
                catch { }
            }
            return found;
        }

        /// <summary>
        /// Locates the shared feature extractor. Exact names win (hubert.onnx
        /// first), then any ONNX file whose name looks like a HuBERT / ContentVec
        /// export - so downloads such as "vec-256-layer-9.onnx" or
        /// "contentvec_768l12.onnx" load without renaming.
        /// </summary>
        public static string FindHubert()
        {
            string[] names = { "hubert.onnx", "hubert_base.onnx", "content-encoder.onnx" };
            string[] folders = SearchFolders;
            for (int f = 0; f < folders.Length; f++)
            {
                for (int n = 0; n < names.Length; n++)
                {
                    string p = Path.Combine(folders[f], names[n]);
                    if (File.Exists(p)) return p;
                }
            }
            for (int f = 0; f < folders.Length; f++)
            {
                try
                {
                    if (!Directory.Exists(folders[f])) continue;
                    string best = null;
                    foreach (string file in Directory.GetFiles(folders[f], "*.onnx"))
                    {
                        if (!IsExtractorName(file)) continue;
                        if (best == null ||
                            string.Compare(Path.GetFileName(file), Path.GetFileName(best), StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            best = file;
                        }
                    }
                    if (best != null) return best;
                }
                catch { }
            }
            return null;
        }

        // ---------- lifecycle ----------

        /// <summary>
        /// Loads (or switches) the AI voice. Blocks for a second or two while
        /// ONNX warms up - call from the UI thread with a wait cursor.
        /// </summary>
        public void LoadModel(string onnxPath)
        {
            StopWorker();
            IsReady = false;
            LoadedModelPath = null;
            LoadedModelName = null;
            LastError = null;

            try
            {
                if (string.IsNullOrEmpty(onnxPath) || !File.Exists(onnxPath))
                {
                    throw new ApplicationException("Voice model file not found: " + onnxPath);
                }

                string hubertPath = FindHubert();
                if (hubertPath == null)
                {
                    throw new ApplicationException(
                        "AI feature extractor not found. Download 'vec-256-layer-9.onnx' from " +
                        "https://huggingface.co/ozada/onnx_rvc (or any hubert/contentvec .onnx file), put it into " +
                        PrimaryModelsFolder + ", then press RESCAN. Renaming it to hubert.onnx is optional.");
                }

                AiModelConfig config = AiModelConfig.Load(onnxPath);
                AiModelConfig.WriteTemplate(onnxPath);

                OnnxRvcVoice v = new OnnxRvcVoice();
                v.Load(onnxPath, hubertPath, config);   // throws with a descriptive message

                if (voice != null) voice.Dispose();
                voice = v;
                cfg = config;
                pitch = new YinPitchTracker(YinPitchTracker.InternalRate);
                toHubert = new LinearResampler(48000, YinPitchTracker.InternalRate);
                toModel = new LinearResampler(48000, config.SampleRate);
                toEngine = new LinearResampler(config.SampleRate, 48000);

                hubCarry.Clear();
                modelCarry.Clear();
                ClearQueues();

                LoadedModelPath = onnxPath;
                LoadedModelName = Path.GetFileNameWithoutExtension(onnxPath);
                IsReady = true;
                StartWorker();
                RaiseStatus("AI voice ready: " + LoadedModelName +
                    " (" + config.SampleRate / 1000.0 + " kHz, CPU, ~" + blockMs + " ms blocks)");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                if (voice != null) { voice.Dispose(); voice = null; }
                StartWorker();   // keep the thread alive so re-enabling works later
                RaiseStatus("AI voice failed: " + ex.Message);
                if (ex is DllNotFoundException)
                {
                    LastError = "ONNX Runtime native library not found. Restore NuGet packages and rebuild " +
                                "(Microsoft.ML.OnnxRuntime ships onnxruntime.dll for x64).";
                    RaiseStatus(LastError);
                }
            }
        }

        public void Unload()
        {
            StopWorker();
            IsReady = false;
            LoadedModelPath = null;
            LoadedModelName = null;
            if (voice != null) { voice.Dispose(); voice = null; }
            ClearQueues();
            RaiseStatus("AI voice unloaded.");
        }

        // ---------- audio-thread entry point ----------

        /// <summary>
        /// Called from the engine's Read loop. When AI is active it swaps the
        /// passthrough audio for converted audio from the worker's FIFO;
        /// otherwise the samples flow through untouched.
        /// </summary>
        public void Process(float[] buffer, int offset, int count)
        {
            bool active = enabled && IsReady;
            if (!active) return;

            lock (gate)
            {
                for (int i = 0; i < count; i++) inFifo.Add(buffer[offset + i]);

                // CPU keep-up guard: if the worker fell hopelessly behind, drop
                // the oldest input instead of letting latency grow forever.
                int block48 = blockMs * 48;
                int maxQueue = block48 * 6;
                if (inFifo.Count > maxQueue)
                {
                    inFifo.RemoveRange(0, inFifo.Count - block48 * 2);
                }

                int available = Math.Min(count, outFifo.Count);
                for (int i = 0; i < available; i++)
                {
                    buffer[offset + i] = outFifo[i] * (float)gain;
                }
                if (available > 0) outFifo.RemoveRange(0, available);
                // While the worker is still converting (startup, slow CPU),
                // emit silence - never leak the raw microphone mid-conversation.
                for (int i = available; i < count; i++)
                {
                    buffer[offset + i] = 0f;
                }
            }
        }

        // ---------- worker ----------

        private void StartWorker()
        {
            if (workerAlive) return;
            workerAlive = true;
            worker = new Thread(WorkerLoop);
            worker.IsBackground = true;
            worker.Name = "VoiceForge-AI";
            worker.Start();
        }

        private void StopWorker()
        {
            workerAlive = false;
            Thread w = worker;
            worker = null;
            if (w != null && w.IsAlive)
            {
                try { w.Join(3000); } catch { }
            }
        }

        private void WorkerLoop()
        {
            while (workerAlive)
            {
                float[] block = null;
                if (enabled && IsReady)
                {
                    int block48 = blockMs * 48;
                    lock (gate)
                    {
                        if (inFifo.Count >= block48)
                        {
                            block = inFifo.GetRange(0, block48).ToArray();
                            inFifo.RemoveRange(0, block48);
                        }
                    }
                }
                else
                {
                    ClearQueues();
                    Thread.Sleep(30);
                    continue;
                }

                if (block == null)
                {
                    Thread.Sleep(8);
                    continue;
                }

                try
                {
                    List<float> converted = ConvertBlock(block);
                    if (converted != null && converted.Count > 0)
                    {
                        lock (gate) outFifo.AddRange(converted);
                    }
                }
                catch (Exception ex)
                {
                    IsReady = false;
                    LastError = ex.Message;
                    RaiseStatus("AI voice stopped: " + ex.Message);
                    // Passthrough resumes automatically because IsReady is false.
                }
            }
        }

        /// <summary>The full conversion chain for one block of 48 kHz audio.</summary>
        private List<float> ConvertBlock(float[] block48)
        {
            // --- 48k -> 16k, keep whole HuBERT frames, carry the remainder ---
            List<float> wav16 = new List<float>(block48.Length / 3 + 2);
            toHubert.Process(block48, block48.Length, wav16);
            hubCarry.AddRange(wav16);
            int frames = hubCarry.Count / YinPitchTracker.Hop;
            if (frames < 1)
            {
                return new List<float>();   // not enough yet for one 20 ms frame
            }
            int hubLen = frames * YinPitchTracker.Hop;
            float[] hub = hubCarry.GetRange(0, hubLen).ToArray();
            hubCarry.RemoveRange(0, hubLen);

            // --- HuBERT features ---
            float[] featsFlat;
            int tFrames;
            voice.RunHubert(hub, out featsFlat, out tFrames);
            int dim = voice.FeatureDim;

            // --- F0 (20 ms grid, aligned with the features) ---
            float[] f0 = new float[frames];
            pitch.Compute(hub, hub.Length, frames, cfg.F0Min, cfg.F0Max, cfg.YinThreshold, f0);

            double ratio = Math.Pow(2.0, transpose / 12.0);
            for (int i = 0; i < f0.Length; i++)
            {
                if (f0[i] > 0f)
                {
                    double hz = f0[i] * ratio;
                    if (hz < 45.0) hz = 45.0;
                    if (hz > 1600.0) hz = 1600.0;
                    f0[i] = (float)hz;
                }
            }

            // --- 48k -> model rate, keep whole hop frames, carry the remainder ---
            List<float> chunkModel = new List<float>(block48.Length);
            toModel.Process(block48, block48.Length, chunkModel);
            modelCarry.AddRange(chunkModel);
            int genFrames = modelCarry.Count / cfg.HopLength;
            if (genFrames < 1) return new List<float>();
            int consumed = genFrames * cfg.HopLength;
            modelCarry.RemoveRange(0, consumed);

            // --- align features + F0 to the generator's frame grid ---
            float[] phone = new float[genFrames * dim];
            InterpRows(featsFlat, tFrames, dim, phone, genFrames);
            float[] f0g = new float[genFrames];
            InterpScalar(f0, frames, f0g, genFrames);

            long[] bins = new long[genFrames];
            OnnxRvcVoice.CoarseBins(f0g, bins);

            // --- generate + resample back to 48 kHz ---
            float[] audio = voice.RunGenerator(phone, genFrames, dim, bins, f0g, cfg.SpeakerId);
            int want = consumed;   // exact sample count that matches the input duration
            if (audio.Length > want)
            {
                float[] trimmed = new float[want];
                Array.Copy(audio, trimmed, want);
                audio = trimmed;
            }
            else if (audio.Length < want)
            {
                float[] padded = new float[want];
                Array.Copy(audio, padded, audio.Length);
                audio = padded;
            }

            List<float> out48 = new List<float>(want * 2);
            toEngine.Process(audio, audio.Length, out48);
            return out48;
        }

        /// <summary>Linear interpolation between rows of a flat [src, dim] matrix.</summary>
        private static void InterpRows(float[] src, int srcRows, int dim, float[] dst, int dstRows)
        {
            if (srcRows < 1 || dim < 1 || dstRows < 1) return;
            if (src.Length < srcRows * dim)
            {
                srcRows = src.Length / dim;
                if (srcRows < 1) return;
            }
            if (srcRows == 1)
            {
                for (int r = 0; r < dstRows; r++)
                {
                    for (int j = 0; j < dim; j++) dst[r * dim + j] = src[j];
                }
                return;
            }
            for (int r = 0; r < dstRows; r++)
            {
                double pos = r * (double)(srcRows - 1) / (dstRows - 1);
                int i0 = (int)pos;
                int i1 = Math.Min(i0 + 1, srcRows - 1);
                float a = (float)(pos - i0);
                int base0 = i0 * dim;
                int base1 = i1 * dim;
                for (int j = 0; j < dim; j++)
                {
                    dst[r * dim + j] = src[base0 + j] * (1f - a) + src[base1 + j] * a;
                }
            }
        }

        private static void InterpScalar(float[] src, int srcRows, float[] dst, int dstRows)
        {
            if (srcRows < 1 || dstRows < 1) return;
            if (srcRows == 1)
            {
                for (int r = 0; r < dstRows; r++) dst[r] = src[0];
                return;
            }
            for (int r = 0; r < dstRows; r++)
            {
                double pos = r * (double)(srcRows - 1) / (dstRows - 1);
                int i0 = (int)pos;
                int i1 = Math.Min(i0 + 1, srcRows - 1);
                float a = (float)(pos - i0);
                dst[r] = src[i0] * (1f - a) + src[i1] * a;
            }
        }

        private void ClearQueues()
        {
            lock (gate)
            {
                inFifo.Clear();
                outFifo.Clear();
                hubCarry.Clear();
                modelCarry.Clear();
            }
            if (toHubert != null) toHubert.Reset();
            if (toModel != null) toModel.Reset();
            if (toEngine != null) toEngine.Reset();
        }

        private void RaiseStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(message);
        }

        public void Dispose()
        {
            StopWorker();
            if (voice != null) { voice.Dispose(); voice = null; }
            IsReady = false;
        }
    }
}
