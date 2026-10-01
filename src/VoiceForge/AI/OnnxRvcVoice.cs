using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VoiceForge.AI
{
    /// <summary>
    /// Thin wrapper around the two ONNX sessions that make up one RVC voice:
    /// a HuBERT feature extractor (16 kHz waveform -&gt; [T, D] content features)
    /// and the RVC generator (features + F0 -&gt; waveform at the model rate).
    ///
    /// All tensor names come from the sidecar AiModelConfig because exports
    /// disagree about naming; dims are inferred from the HuBERT output at
    /// runtime so both 256-dim and 768-dim exports work unmodified.
    /// </summary>
    public class OnnxRvcVoice : IDisposable
    {
        public const int HubertHop = 320;        // samples per feature frame @ 16 kHz

        private InferenceSession hubertSession;
        private InferenceSession genSession;
        private string hubertIn, hubertOut;
        private string inPhone, inPhoneLen, inPitch, inPitchLen, inPitchf, inPitchfLen, inSid, outAudio;
        private HashSet<string> genIn, genOut, hubInNames, hubOutNames;
        private int[] hubertInDims;              // declared shape of the extractor input
        private int genPhoneDim = -1;            // declared feature width of the generator phone input
        private int featureDim = 256;

        public int FeatureDim { get { return featureDim; } }
        public bool IsLoaded { get { return genSession != null && hubertSession != null; } }

        private static SessionOptions MakeOptions()
        {
            SessionOptions options = new SessionOptions();
            try
            {
                // Leave one core for the audio thread; cap so desktop stays usable.
                int threads = Environment.ProcessorCount - 1;
                if (threads < 1) threads = 1;
                if (threads > 8) threads = 8;
                options.IntraOpNumThreads = threads;
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            }
            catch
            {
                // Defaults are fine if the tuning properties are unavailable.
            }
            return options;
        }

        /// <summary>Loads both sessions. Throws with a descriptive message on failure.</summary>
        public void Load(string genModelPath, string hubertModelPath, AiModelConfig cfg)
        {
            Dispose();

            SessionOptions opts = MakeOptions();
            try
            {
                hubertSession = new InferenceSession(hubertModelPath, opts);
            }
            catch (Exception ex)
            {
                Dispose();
                throw new ApplicationException("Could not load HuBERT model '" + hubertModelPath + "': " + ex.Message, ex);
            }

            try
            {
                genSession = new InferenceSession(genModelPath, MakeOptions());
            }
            catch (Exception ex)
            {
                Dispose();
                throw new ApplicationException("Could not load voice model '" + genModelPath + "': " + ex.Message, ex);
            }

            // RVC exports come in (at least) two flavors: the 7-input style
            // (phone, phone_length, pitch, pitch_length, pitchf, pitchf_length,
            // sid) and the official 5-input style (phone, phone_lengths, pitch,
            // pitchf, ds). Resolve every tensor name against what the model
            // actually exposes so both load without editing the sidecar.
            hubInNames = CollectNames(hubertSession.InputMetadata);
            hubOutNames = CollectNames(hubertSession.OutputMetadata);
            genIn = CollectNames(genSession.InputMetadata);
            genOut = CollectNames(genSession.OutputMetadata);

            hubertIn = PickName(cfg.HubertInput, hubInNames, new[] { "source" });
            hubertOut = PickName(cfg.HubertOutput, hubOutNames, new[] { "embed" });
            inPhone = PickName(cfg.GenInputPhone, genIn, new[] { "phone" });
            inPhoneLen = PickName(cfg.GenInputPhoneLength, genIn, new[] { "phone_lengths", "phone_length" });
            inPitch = PickName(cfg.GenInputPitch, genIn, new[] { "pitch" });
            inPitchLen = PickName(cfg.GenInputPitchLength, genIn, new[] { "pitch_length", "pitch_lengths" });
            inPitchf = PickName(cfg.GenInputPitchf, genIn, new[] { "pitchf", "f0" });
            inPitchfLen = PickName(cfg.GenInputPitchfLength, genIn, new[] { "pitchf_length", "f0_length" });
            inSid = PickName(cfg.GenInputSid, genIn, new[] { "sid", "ds" });
            outAudio = PickName(cfg.GenOutput, genOut, new[] { "audio" });

            // Remember what the models declare so inference can feed exact
            // shapes: rank-1/2/3 waveform inputs and 256/768 feature dims all
            // exist across the community exports.
            hubertInDims = DeclaredDims(hubertSession.InputMetadata, hubertIn);
            int[] phoneDims = DeclaredDims(genSession.InputMetadata, inPhone);
            if (phoneDims != null && phoneDims.Length >= 3)
            {
                genPhoneDim = phoneDims[phoneDims.Length - 1];
            }

            if (inPhone == null || inPitch == null || inPitchf == null || outAudio == null)
            {
                string actualIn = NameList(genIn);
                string actualOut = NameList(genOut);
                Dispose();
                throw new ApplicationException(
                    "Voice model does not look like a standard RVC generator.\n" +
                    "Model inputs: " + actualIn + "\nModel outputs: " + actualOut + "\n" +
                    "If those are correct, set the GenInput*/GenOutput names in the voice's .json sidecar (inspect with Netron).");
            }
        }

        private static HashSet<string> CollectNames(IReadOnlyDictionary<string, NodeMetadata> metadata)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (metadata != null)
                {
                    foreach (string key in metadata.Keys) names.Add(key);
                }
            }
            catch
            {
                // Treated as "unknown"; the per-input fallbacks below still apply.
            }
            return names;
        }

        /// <summary>
        /// Chooses a tensor name that actually exists in the model: the configured
        /// sidecar name wins when present, then known aliases, then the only name
        /// of a single-tensor model; otherwise null (input not used by this export).
        /// </summary>
        private static string PickName(string configured, HashSet<string> available, string[] aliases)
        {
            if (available == null || available.Count == 0) return null;
            if (!string.IsNullOrEmpty(configured) && available.Contains(configured)) return configured;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    if (available.Contains(aliases[i])) return aliases[i];
                }
            }
            if (available.Count == 1)
            {
                foreach (string only in available) return only;
            }
            return null;
        }

        private static string NameList(HashSet<string> names)
        {
            if (names == null || names.Count == 0) return "(unknown)";
            return string.Join(", ", names);
        }

        /// <summary>Declared input shape from the session metadata (-1 = dynamic axis); null if unknown.</summary>
        private static int[] DeclaredDims(IReadOnlyDictionary<string, NodeMetadata> metadata, string name)
        {
            try
            {
                if (metadata != null && !string.IsNullOrEmpty(name) && metadata.ContainsKey(name))
                {
                    int[] dims = metadata[name].Dimensions.ToArray();   // int[] or IReadOnlyList<int> both work
                    if (dims != null && dims.Length > 0) return dims;
                }
            }
            catch
            {
                // Metadata unavailable - the shape candidates below still apply.
            }
            return null;
        }

        /// <summary>
        /// Builds the input shape from the model's own declaration: fixed dims
        /// stay as declared, dynamic ones become batch=1, time=N and a trailing
        /// channel dim of 1 (typical rank-3 audio tensors). Null when unknown.
        /// </summary>
        private int[] TemplateShape(int n)
        {
            if (hubertInDims == null || hubertInDims.Length < 1) return null;
            int[] shape = new int[hubertInDims.Length];
            Array.Copy(hubertInDims, shape, shape.Length);
            for (int i = 0; i < shape.Length; i++)
            {
                if (shape[i] >= 1)
                {
                    continue;                       // fixed dimension: keep
                }
                if (i == 0)
                {
                    shape[i] = 1;                   // batch
                }
                else if (i == shape.Length - 1 && shape.Length >= 3)
                {
                    shape[i] = 1;                   // trailing channel on rank-3 audio
                }
                else
                {
                    shape[i] = n;                   // time axis
                }
            }
            return shape;
        }

        private static string DimText(int d)
        {
            return d < 0 ? "?" : d.ToString();
        }

        private static string DescribeShape(int[] shape)
        {
            if (shape == null) return "(none)";
            return "[" + string.Join(", ", shape.Select(DimText)) + "]";
        }

        /// <summary>
        /// Runs HuBERT on 16 kHz audio. Returns the flattened [T, D] feature
        /// matrix; <paramref name="frames"/> receives T and FeatureDim receives D.
        /// </summary>
        public void RunHubert(float[] wav16k, out float[] featuresFlat, out int frames)
        {
            int t = wav16k.Length / HubertHop;
            if (t < 1) t = 1;

            featuresFlat = null;
            frames = t;

            // Exports disagree about the waveform input rank: flat [N],
            // fairseq-style [1, N] and rank-3 [1, N, 1] audio tensors all exist
            // in the wild. The shape the model itself declares is tried first
            // (that is what ONNX Runtime validates against), then the well-known
            // community flavors.
            List<int[]> shapes = new List<int[]>();
            int[] declared = TemplateShape(wav16k.Length);
            if (declared != null) shapes.Add(declared);
            shapes.Add(new[] { 1, wav16k.Length });
            shapes.Add(new[] { 1, wav16k.Length, 1 });
            shapes.Add(new[] { wav16k.Length });
            shapes.Add(new[] { 1, 1, wav16k.Length });

            float[] result = null;
            string lastError = null;
            int[] lastShape = null;
            foreach (int[] shape in shapes)
            {
                lastShape = shape;
                result = RunHubertTry(wav16k, shape, ref lastError);
                if (result != null) break;
            }
            if (result == null)
            {
                throw new ApplicationException(
                    "HuBERT rejected input '" + hubertIn + "' in every known shape (tried " +
                    DescribeShape(lastShape) + " last; the model declares " +
                    (hubertInDims != null ? DescribeShape(hubertInDims) : "an unknown shape") + "). " +
                    "Runtime error: " + (lastError ?? "no details") + ". " +
                    "Make sure the extractor is a HuBERT/ContentVec audio model - see models/README-AI-MODELS.txt.");
            }
            featuresFlat = result;

            // Infer the real feature width from the output so 256-dim and
            // 768-dim exports both work without any configuration.
            if (featuresFlat.Length >= t)
            {
                featureDim = Math.Max(1, featuresFlat.Length / t);
            }

            int produced = featuresFlat.Length / featureDim;
            if (produced < 1) produced = 1;
            if (produced < t)
            {
                // Some exports trim the tail; pad the last frame so lengths match.
                int dim = featureDim;
                float[] padded = new float[t * dim];
                Array.Copy(featuresFlat, padded, Math.Min(featuresFlat.Length, padded.Length));
                for (int i = featuresFlat.Length; i < padded.Length; i++)
                {
                    padded[i] = padded[i - dim >= 0 ? i - dim : 0];
                }
                featuresFlat = padded;
            }
            frames = t;
        }

        /// <summary>
        /// Runs HuBERT once with an explicit input shape. Returns null (instead
        /// of throwing) when the session rejects it, so the caller can try the
        /// next shape; the runtime error text comes back via <paramref name="error"/>.
        /// </summary>
        private float[] RunHubertTry(float[] wav16k, int[] shape, ref string error)
        {
            DenseTensor<float> input;
            try
            {
                input = new DenseTensor<float>(shape);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }

            // Every candidate keeps exactly one free axis (size N) with all
            // other axes 1, so a linear fill covers [N], [1,N], [1,N,1] and
            // friends alike - it is just a reshape of a contiguous buffer.
            int freeAxis = -1;
            for (int a = 0; a < shape.Length; a++)
            {
                if (shape[a] > 1)
                {
                    freeAxis = a;
                    break;
                }
            }
            if (freeAxis < 0) freeAxis = shape.Length - 1;

            int[] index = new int[shape.Length];
            for (int i = 0; i < wav16k.Length; i++)
            {
                index[freeAxis] = i;
                input[index] = wav16k[i];
            }
            index[freeAxis] = 0;

            var inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor(hubertIn, input));

            try
            {
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = hubertSession.Run(inputs))
                {
                    foreach (DisposableNamedOnnxValue v in results)
                    {
                        if (!string.Equals(v.Name, hubertOut, StringComparison.Ordinal)) continue;
                        return v.AsEnumerable<float>().ToArray();
                    }
                }
                error = "output tensor '" + hubertOut + "' was not produced";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Runs the RVC generator. <paramref name="phoneFlat"/> is [t, D] flattened,
        /// pitch/pitchf hold one value per generator frame (already interpolated to
        /// the hop grid). Returns the raw waveform at the model's sample rate.
        /// </summary>
        public float[] RunGenerator(float[] phoneFlat, int genFrames, int featureDimUsed,
                                    long[] pitchCoarse, float[] pitchf, int speakerId)
        {
            int t = genFrames;
            int dim = featureDimUsed;

            if (genPhoneDim > 0 && dim != genPhoneDim)
            {
                throw new ApplicationException(
                    "Feature extractor / voice mismatch: this voice needs " + genPhoneDim +
                    "-dim content features (vec-" + genPhoneDim + "-layer-" + (genPhoneDim == 256 ? "9" : "12") +
                    ".onnx) but the loaded extractor produces " + dim + "-dim. Both files must come from the " +
                    "matching set - see models/README-AI-MODELS.txt.");
            }

            DenseTensor<float> phone = new DenseTensor<float>(new[] { 1, t, dim });
            for (int i = 0; i < t; i++)
            {
                int src = i * dim;
                for (int j = 0; j < dim; j++)
                {
                    phone[0, i, j] = phoneFlat[src + j];
                }
            }

            DenseTensor<long> phoneLen = MakeLength(t);
            DenseTensor<long> pitch = new DenseTensor<long>(new[] { 1, t });
            for (int i = 0; i < t; i++) pitch[0, i] = pitchCoarse[i];
            DenseTensor<long> pitchLen = MakeLength(t);
            DenseTensor<float> pitchfTensor = new DenseTensor<float>(new[] { 1, t });
            for (int i = 0; i < t; i++) pitchfTensor[0, i] = pitchf[i];
            DenseTensor<long> sid = MakeLength(speakerId);

            // Feed only the tensors this export actually declares (5-input and
            // 7-input flavors are both supported; extra names would make ONNX
            // Runtime reject the call).
            var inputs = new List<NamedOnnxValue>();
            if (inPhone != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPhone, phone));
            if (inPhoneLen != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPhoneLen, phoneLen));
            if (inPitch != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPitch, pitch));
            if (inPitchLen != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPitchLen, pitchLen));
            if (inPitchf != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPitchf, pitchfTensor));
            if (inPitchfLen != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inPitchfLen, pitchLen));
            if (inSid != null) inputs.Add(NamedOnnxValue.CreateFromTensor(inSid, sid));

            float[] audio = null;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = genSession.Run(inputs))
            {
                foreach (DisposableNamedOnnxValue v in results)
                {
                    if (!string.Equals(v.Name, outAudio, StringComparison.Ordinal)) continue;
                    audio = v.AsEnumerable<float>().ToArray();
                    break;
                }
            }

            if (audio == null)
            {
                throw new ApplicationException("Generator output tensor '" + outAudio + "' was not found. " +
                    "Check GenOutput in the model's .json sidecar (use Netron to inspect names).");
            }
            return audio;
        }

        private DenseTensor<long> MakeLength(long value)
        {
            // Standard RVC exports take lengths as long[1]; keeping one shape
            // avoids tensor-rank surprises across export flavors.
            DenseTensor<long> t = new DenseTensor<long>(new int[] { 1 });
            t[0] = value;
            return t;
        }

        private static readonly double F0MelMin = 1127.0 * Math.Log(1.0 + 50.0 / 700.0);   // RVC F0 floor
        private static readonly double F0MelMax = 1127.0 * Math.Log(1.0 + 1100.0 / 700.0); // RVC F0 ceiling

        /// <summary>
        /// RVC "coarse" F0 bins (256 log-mel bands), exactly as RVC's f0.py
        /// computes them. <paramref name="f0"/> must already include the user's
        /// transpose. Unvoiced (0 Hz) maps to bin 0.
        /// </summary>
        public static void CoarseBins(float[] f0, long[] bins)
        {
            double binSpan = (F0MelMax - F0MelMin) / 254.0;

            for (int i = 0; i < f0.Length; i++)
            {
                double hz = f0[i];
                if (hz <= 0.0)
                {
                    bins[i] = 0;
                    continue;
                }
                double mel = 1127.0 * Math.Log(1.0 + hz / 700.0);
                double b = (mel - F0MelMin) / binSpan + 1.0;
                if (b < 1.0) b = 1.0;
                if (b > 255.0) b = 255.0;
                bins[i] = (long)Math.Round(b);
            }
        }

        public void Dispose()
        {
            if (hubertSession != null)
            {
                try { hubertSession.Dispose(); } catch { }
                hubertSession = null;
            }
            if (genSession != null)
            {
                try { genSession.Dispose(); } catch { }
                genSession = null;
            }
        }
    }
}
