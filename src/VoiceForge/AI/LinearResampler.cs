using System.Collections.Generic;

namespace VoiceForge.AI
{
    /// <summary>
    /// Stateful linear-interpolation resampler for continuous mono streams.
    /// Statefulness (the carried fractional position + previous sample) is what
    /// keeps chunk boundaries click-free, unlike resampling each block cold.
    /// </summary>
    public class LinearResampler
    {
        private readonly double step;      // input samples per output sample
        private double frac;               // 0..1 position between prev and current
        private float prev;
        private bool hasPrev;

        public LinearResampler(int inRate, int outRate)
        {
            if (inRate < 1) inRate = 1;
            if (outRate < 1) outRate = 1;
            step = (double)inRate / outRate;
        }

        public void Reset()
        {
            frac = 0.0;
            prev = 0f;
            hasPrev = false;
        }

        /// <summary>Consumes input samples, appends resampled output to <paramref name="output"/>.</summary>
        public void Process(IList<float> input, int count, List<float> output)
        {
            for (int i = 0; i < count; i++)
            {
                float cur = input[i];
                if (!hasPrev)
                {
                    prev = cur;
                    hasPrev = true;
                    // Emit the very first sample once so the stream starts immediately.
                    output.Add(cur);
                    frac += step;
                    if (frac >= 1.0) frac -= 1.0;
                    continue;
                }

                while (frac < 1.0)
                {
                    output.Add(prev + (cur - prev) * (float)frac);
                    frac += step;
                }
                frac -= 1.0;
                prev = cur;
            }
        }
    }
}
