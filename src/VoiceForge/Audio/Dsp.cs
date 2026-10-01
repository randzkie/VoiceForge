using System;

namespace VoiceForge.Dsp
{
    /// <summary>
    /// A real-time mono sample effect. Process() runs on the audio playback thread,
    /// so implementations must be allocation-free and fast.
    /// </summary>
    public interface ISampleEffect
    {
        void Process(float[] buffer, int offset, int count);
        void Reset();
    }

    /// <summary>RBJ biquad filter (low-pass / high-pass / band-pass).</summary>
    public class BiquadFilter : ISampleEffect
    {
        public enum BiquadType { LowPass, HighPass, BandPass }

        private readonly int sampleRate;
        private double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;

        public BiquadFilter(int sampleRate)
        {
            this.sampleRate = sampleRate;
            // Unity by default.
            b0 = 1.0;
        }

        public void Set(BiquadType type, double freq, double q)
        {
            if (freq < 20.0) freq = 20.0;
            if (freq > sampleRate * 0.45) freq = sampleRate * 0.45;
            if (q < 0.05) q = 0.05;

            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cw = Math.Cos(w0);
            double sw = Math.Sin(w0);
            double alpha = sw / (2.0 * q);

            double nb0, nb1, nb2, na0, na1, na2;
            na0 = 1.0 + alpha;
            na1 = -2.0 * cw;
            na2 = 1.0 - alpha;

            switch (type)
            {
                case BiquadType.LowPass:
                    nb0 = (1.0 - cw) / 2.0;
                    nb1 = 1.0 - cw;
                    nb2 = (1.0 - cw) / 2.0;
                    break;
                case BiquadType.HighPass:
                    nb0 = (1.0 + cw) / 2.0;
                    nb1 = -(1.0 + cw);
                    nb2 = (1.0 + cw) / 2.0;
                    break;
                default: // BandPass (unity peak gain)
                    nb0 = alpha;
                    nb1 = 0.0;
                    nb2 = -alpha;
                    break;
            }

            b0 = nb0 / na0;
            b1 = nb1 / na0;
            b2 = nb2 / na0;
            a1 = na1 / na0;
            a2 = na2 / na0;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            double bb0 = b0, bb1 = b1, bb2 = b2, aa1 = a1, aa2 = a2;
            double xx1 = x1, xx2 = x2, yy1 = y1, yy2 = y2;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                double x = buffer[i];
                double y = bb0 * x + bb1 * xx1 + bb2 * xx2 - aa1 * yy1 - aa2 * yy2;
                xx2 = xx1; xx1 = x;
                yy2 = yy1; yy1 = y;
                buffer[i] = (float)y;
            }
            x1 = xx1; x2 = xx2; y1 = yy1; y2 = yy2;
        }

        public void Reset()
        {
            x1 = x2 = y1 = y2 = 0.0;
        }
    }

    /// <summary>
    /// Real-time pitch shifter (harmonizer) built on a modulated delay line with two
    /// cross-faded read taps. Shifts pitch without changing duration.
    /// </summary>
    public class PitchShifter : ISampleEffect
    {
        private readonly float[] delay;
        private readonly int delayLen;
        private readonly int grain;
        private int writePos;
        private double phase;
        private double increment;
        private double rate = 1.0;

        public PitchShifter(int sampleRate, double grainSeconds)
        {
            grain = Math.Max(256, (int)(sampleRate * grainSeconds));
            delayLen = grain + 8;
            delay = new float[delayLen];
        }

        public void SetSemitones(double semitones)
        {
            rate = Math.Pow(2.0, semitones / 12.0);
            // The normalized delay fraction drifts at (1 - rate) per sample.
            increment = (1.0 - rate) / grain;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            if (Math.Abs(rate - 1.0) < 1e-6)
            {
                return; // Unity pitch: pass through untouched.
            }

            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                delay[writePos] = buffer[i];

                phase += increment;
                if (phase >= 1.0) phase -= 1.0;
                if (phase < 0.0) phase += 1.0;

                double d0 = phase * grain;
                double p1 = phase >= 0.5 ? phase - 0.5 : phase + 0.5;
                double d1 = p1 * grain;

                double w = Math.Sin(Math.PI * phase);
                w = w * w;                     // Cross-fade window, zero at wrap points.
                double w1 = 1.0 - w;

                double s0 = ReadFractional(d0);
                double s1 = ReadFractional(d1);

                buffer[i] = (float)(s0 * w + s1 * w1);

                writePos++;
                if (writePos >= delayLen) writePos = 0;
            }
        }

        private double ReadFractional(double delaySamples)
        {
            double pos = writePos - delaySamples;
            while (pos < 0.0) pos += delayLen;
            int i0 = (int)pos;
            if (i0 >= delayLen) i0 = 0;
            int i1 = i0 + 1;
            if (i1 >= delayLen) i1 = 0;
            double frac = pos - Math.Floor(pos);
            return delay[i0] * (1.0 - frac) + delay[i1] * frac;
        }

        public void Reset()
        {
            Array.Clear(delay, 0, delayLen);
            phase = 0.0;
            writePos = 0;
        }
    }

    /// <summary>
    /// Schroeder/Freeverb-style reverb: 8 parallel comb filters followed by 4 all-pass
    /// filters. Tunings are scaled to the device sample rate.
    /// </summary>
    public class ReverbEffect : ISampleEffect
    {
        private static readonly int[] CombTunings = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTunings = { 556, 441, 341, 225 };

        private readonly float[][] combBuffers;
        private readonly int[] combPositions;
        private readonly float[][] allpassBuffers;
        private readonly int[] allpassPositions;

        private double feedback = 0.72;
        private double damp1 = 0.2;
        private double filterStore;
        private double mix = 0.3;
        private double wetScale = 0.22;

        public ReverbEffect(int sampleRate, double mix01, double decaySeconds)
        {
            double scale = sampleRate / 44100.0;
            combBuffers = new float[CombTunings.Length][];
            combPositions = new int[CombTunings.Length];
            for (int i = 0; i < CombTunings.Length; i++)
            {
                int len = Math.Max(8, (int)(CombTunings[i] * scale));
                combBuffers[i] = new float[len];
                combPositions[i] = 0;
            }
            allpassBuffers = new float[AllpassTunings.Length][];
            allpassPositions = new int[AllpassTunings.Length];
            for (int i = 0; i < AllpassTunings.Length; i++)
            {
                int len = Math.Max(4, (int)(AllpassTunings[i] * scale));
                allpassBuffers[i] = new float[len];
                allpassPositions[i] = 0;
            }
            SetMix(mix01);
            SetDecay(decaySeconds);
        }

        public void SetMix(double mix01)
        {
            if (mix01 < 0.0) mix01 = 0.0;
            if (mix01 > 1.0) mix01 = 1.0;
            mix = mix01;
        }

        public void SetDecay(double decaySeconds)
        {
            if (decaySeconds < 0.2) decaySeconds = 0.2;
            if (decaySeconds > 6.0) decaySeconds = 6.0;
            // Map decay time (0.2s .. 6s) onto comb feedback (0.55 .. 0.92).
            feedback = 0.55 + (decaySeconds / 6.0) * 0.37;
            if (feedback > 0.92) feedback = 0.92;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            if (mix <= 0.001) return;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                double input = buffer[i];

                // Sum the parallel combs.
                double combSum = 0.0;
                for (int c = 0; c < combBuffers.Length; c++)
                {
                    float[] buf = combBuffers[c];
                    int pos = combPositions[c];
                    double output = buf[pos];
                    filterStore = output * (1.0 - damp1) + filterStore * damp1;
                    buf[pos] = (float)(input + filterStore * feedback);
                    pos++;
                    if (pos >= buf.Length) pos = 0;
                    combPositions[c] = pos;
                    combSum += output;
                }
                combSum /= combBuffers.Length;

                // Cascade the all-pass filters on the wet signal only.
                // Canonical Freeverb all-pass: y = bufout - in; store in + 0.5*bufout.
                double wet = combSum;
                for (int a = 0; a < allpassBuffers.Length; a++)
                {
                    float[] buf = allpassBuffers[a];
                    int pos = allpassPositions[a];
                    double bufout = buf[pos];
                    double output = bufout - wet;
                    buf[pos] = (float)(wet + bufout * 0.5);
                    wet = output;
                    pos++;
                    if (pos >= buf.Length) pos = 0;
                    allpassPositions[a] = pos;
                }

                double result = input + wet * mix * wetScale;
                if (result > 1.0) result = 1.0;
                if (result < -1.0) result = -1.0;
                buffer[i] = (float)result;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < combBuffers.Length; i++) Array.Clear(combBuffers[i], 0, combBuffers[i].Length);
            for (int i = 0; i < allpassBuffers.Length; i++) Array.Clear(allpassBuffers[i], 0, allpassBuffers[i].Length);
            filterStore = 0.0;
        }
    }

    /// <summary>Feedback delay / echo with configurable time, feedback and mix.</summary>
    public class EchoEffect : ISampleEffect
    {
        private const double MaxDelaySeconds = 2.0;
        private readonly float[] buffer;
        private int writePos;
        private int delaySamples;
        private double feedback;
        private double mix;

        public EchoEffect(int sampleRate, double delayMs, double feedback01, double mix01)
        {
            int maxSamples = (int)(sampleRate * MaxDelaySeconds);
            buffer = new float[maxSamples];
            SetParameters(delayMs, feedback01, mix01);
        }

        public void SetParameters(double delayMs, double feedback01, double mix01)
        {
            int maxSamples = buffer.Length;
            int samples = (int)(delayMs / 1000.0 * 48000.0);
            if (samples < 16) samples = 16;
            if (samples > maxSamples - 16) samples = maxSamples - 16;
            delaySamples = samples;

            if (feedback01 < 0.0) feedback01 = 0.0;
            if (feedback01 > 0.9) feedback01 = 0.9;
            feedback = feedback01;

            if (mix01 < 0.0) mix01 = 0.0;
            if (mix01 > 1.0) mix01 = 1.0;
            mix = mix01;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            if (mix <= 0.001) return;
            float[] buf = this.buffer;
            int len = buf.Length;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                int readPos = writePos - delaySamples;
                while (readPos < 0) readPos += len;
                double delayed = buf[readPos];
                buf[writePos] = (float)(buffer[i] + delayed * feedback);
                double result = buffer[i] + delayed * mix;
                if (result > 1.0) result = 1.0;
                if (result < -1.0) result = -1.0;
                buffer[i] = (float)result;
                writePos++;
                if (writePos >= len) writePos = 0;
            }
        }

        public void Reset()
        {
            Array.Clear(buffer, 0, buffer.Length);
            writePos = 0;
        }
    }

    /// <summary>Ring modulator: multiplies input by an oscillator. Classic robot / alien timbre.</summary>
    public class RingModulator : ISampleEffect
    {
        private readonly double sampleRate;
        private double freq;
        private double phase;

        public RingModulator(int sampleRate, double freqHz)
        {
            this.sampleRate = sampleRate;
            freq = freqHz;
        }

        public void SetFrequency(double freqHz)
        {
            if (freqHz < 1.0) freqHz = 1.0;
            if (freqHz > sampleRate * 0.4) freqHz = sampleRate * 0.4;
            freq = freqHz;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            double step = 2.0 * Math.PI * freq / sampleRate;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                buffer[i] = (float)(buffer[i] * Math.Sin(phase));
                phase += step;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
            }
        }

        public void Reset()
        {
            phase = 0.0;
        }
    }

    /// <summary>Flanger: short LFO-modulated delay with feedback. Ghostly / underwater sweeps.</summary>
    public class FlangerEffect : ISampleEffect
    {
        private readonly float[] buffer;
        private readonly int sampleRate;
        private int writePos;
        private double phase;
        private double rate;     // Hz of the LFO
        private double depth;    // 0..1 sweep depth
        private double feedback = 0.55;
        private double wet = 0.65;

        public FlangerEffect(int sampleRate, double rateHz, double depth01)
        {
            this.sampleRate = sampleRate;
            int len = Math.Max(64, (int)(sampleRate * 0.02)); // 20 ms max delay
            buffer = new float[len];
            SetParameters(rateHz, depth01);
        }

        public void SetParameters(double rateHz, double depth01)
        {
            if (rateHz < 0.05) rateHz = 0.05;
            if (rateHz > 5.0) rateHz = 5.0;
            rate = rateHz;
            if (depth01 < 0.0) depth01 = 0.0;
            if (depth01 > 1.0) depth01 = 1.0;
            depth = depth01;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            float[] buf = this.buffer;
            int len = buf.Length;
            double step = 2.0 * Math.PI * rate / sampleRate;
            double minDelay = 0.0012 * sampleRate;                    // ~1.2 ms
            double span = 0.006 * sampleRate * depth;                 // up to ~7 ms
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                double lfo = Math.Sin(phase);
                double delaySamples = minDelay + span * (0.5 + 0.5 * lfo);
                phase += step;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;

                double pos = writePos - delaySamples;
                while (pos < 0.0) pos += len;
                int i0 = (int)pos;
                if (i0 >= len) i0 = 0;
                int i1 = i0 + 1;
                if (i1 >= len) i1 = 0;
                double frac = pos - Math.Floor(pos);
                double delayed = buf[i0] * (1.0 - frac) + buf[i1] * frac;

                buf[writePos] = (float)(buffer[i] + delayed * feedback);
                double result = buffer[i] + delayed * wet;
                if (result > 1.0) result = 1.0;
                if (result < -1.0) result = -1.0;
                buffer[i] = (float)result;

                writePos++;
                if (writePos >= len) writePos = 0;
            }
        }

        public void Reset()
        {
            Array.Clear(buffer, 0, buffer.Length);
            writePos = 0;
            phase = 0.0;
        }
    }

    /// <summary>Soft-clipping waveshaper. Drive > 1 adds grit; output is normalized.</summary>
    public class DistortionEffect : ISampleEffect
    {
        private double drive = 1.0;
        private double norm = 1.0 / Math.Tanh(1.0);

        public void SetDrive(double drive01to7)
        {
            if (drive01to7 < 1.0) drive01to7 = 1.0;
            if (drive01to7 > 8.0) drive01to7 = 8.0;
            drive = drive01to7;
            norm = 1.0 / Math.Tanh(drive);
        }

        public void Process(float[] buffer, int offset, int count)
        {
            if (drive <= 1.01) return;
            double d = drive, n = norm;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                buffer[i] = (float)(Math.Tanh(buffer[i] * d) * n);
            }
        }

        public void Reset()
        {
        }
    }

    /// <summary>Simple pre/post gain stage.</summary>
    public class GainEffect : ISampleEffect
    {
        private double gain = 1.0;

        public GainEffect(double gain01)
        {
            SetGain(gain01);
        }

        public void SetGain(double g)
        {
            if (g < 0.1) g = 0.1;
            if (g > 4.0) g = 4.0;
            gain = g;
        }

        public void Process(float[] buffer, int offset, int count)
        {
            double g = gain;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                buffer[i] = (float)(buffer[i] * g);
            }
        }

        public void Reset()
        {
        }
    }
}
