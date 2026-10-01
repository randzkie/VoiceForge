using System;

namespace VoiceForge.AI
{
    /// <summary>
    /// YIN fundamental-frequency tracker (de Cheveigne &amp; Kawahara 2002) with
    /// cumulative mean normalized difference and parabolic refinement.
    ///
    /// Runs on 16 kHz mono audio, one estimate per 20 ms frame (320 samples),
    /// which matches the HuBERT frame grid exactly - so F0 frames line up with
    /// feature frames with no extra bookkeeping. Returns 0 Hz for unvoiced
    /// frames, which the RVC generator interprets as "no pitch".
    /// </summary>
    public class YinPitchTracker
    {
        public const int InternalRate = 16000;
        public const int Hop = 320;          // 20 ms
        private const int Window = 1024;     // 64 ms analysis window

        private readonly int sampleRate;
        private double[] diff;               // difference function scratch
        private double[] cmnd;               // cumulative mean normalized diff scratch

        public YinPitchTracker(int sampleRate)
        {
            this.sampleRate = sampleRate <= 0 ? InternalRate : sampleRate;
            int maxTau = Math.Min(Window / 2 - 1, this.sampleRate / 30);
            diff = new double[Math.Max(16, maxTau + 2)];
            cmnd = new double[Math.Max(16, maxTau + 2)];
        }

        /// <summary>
        /// Estimates F0 for <paramref name="frameCount"/> frames starting at
        /// frame 0 (frame f begins at sample f * Hop). Audio past the end of the
        /// buffer is treated as silence.
        /// </summary>
        public void Compute(float[] wav, int wavLen, int frameCount,
                            double f0Min, double f0Max, double threshold,
                            float[] outF0)
        {
            int tauMin = Math.Max(2, (int)(sampleRate / Math.Max(1.0, f0Max)));
            int tauMax = Math.Min(Window / 2 - 1, (int)(sampleRate / Math.Max(1.0, f0Min)));
            if (tauMax <= tauMin + 2) tauMax = tauMin + 2;
            if (diff.Length < tauMax + 2)
            {
                diff = new double[tauMax + 2];
                cmnd = new double[tauMax + 2];
            }

            for (int f = 0; f < frameCount; f++)
            {
                int start = f * Hop;
                outF0[f] = TrackFrame(wav, wavLen, start, tauMin, tauMax, threshold);
            }
        }

        private float TrackFrame(float[] wav, int wavLen, int start,
                                 int tauMin, int tauMax, double threshold)
        {
            int avail = wavLen - start;
            if (avail < Window / 8) return 0f;   // frame is essentially empty

            // ---- difference function d(tau) ----
            // The analysis window is clamped to the samples we actually have
            // (a = zero beyond the buffer, b = always in range).
            int n = Math.Min(Window, avail);
            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                double sum = 0.0;
                for (int i = 0; i < n; i++)
                {
                    float a = (start + i + tau) < wavLen ? wav[start + i + tau] : 0f;
                    float b = wav[start + i];
                    double d = a - b;
                    sum += d * d;
                }
                diff[tau] = sum;
            }

            // ---- cumulative mean normalized difference ----
            double running = 0.0;
            cmnd[tauMin - 1] = 1.0;
            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                running += diff[tau];
                cmnd[tau] = running > 1e-12
                    ? diff[tau] * (tau - tauMin + 1) / running
                    : 1.0;
            }

            // ---- absolute threshold + descent to local minimum ----
            int best = -1;
            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                if (cmnd[tau] < threshold)
                {
                    best = tau;
                    while (tau + 1 <= tauMax && cmnd[tau + 1] < cmnd[tau])
                    {
                        tau++;
                        best = tau;
                    }
                    break;
                }
            }

            if (best < 0)
            {
                // No valley under the threshold: accept the global minimum only
                // if it is reasonably deep (loose voicing decision).
                double minVal = double.MaxValue;
                for (int tau = tauMin; tau <= tauMax; tau++)
                {
                    if (cmnd[tau] < minVal)
                    {
                        minVal = cmnd[tau];
                        best = tau;
                    }
                }
                if (minVal > threshold * 2.5) return 0f;
            }

            // ---- parabolic interpolation around the valley ----
            double tauEst = best;
            if (best > tauMin && best < tauMax)
            {
                double s0 = cmnd[best - 1];
                double s1 = cmnd[best];
                double s2 = cmnd[best + 1];
                double denom = 2.0 * (2.0 * s1 - s0 - s2);
                if (Math.Abs(denom) > 1e-12)
                {
                    tauEst = best + (s2 - s0) / denom;
                }
            }

            if (tauEst < 1.0) return 0f;
            float hz = (float)(sampleRate / tauEst);
            if (hz < 40f || hz > 2000f) return 0f;
            return hz;
        }
    }
}
