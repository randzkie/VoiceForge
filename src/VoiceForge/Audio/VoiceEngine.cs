using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceForge.AI;
using VoiceForge.Dsp;
using VoiceForge.Models;

namespace VoiceForge.Audio
{
    /// <summary>
    /// Real-time voice engine: microphone capture -> DSP effect chain -> output device.
    /// All audio is 48 kHz mono so the DSP chain stays simple and predictable.
    /// A recording tap sits after the volume stage so the recorder captures exactly
    /// what the user hears (voice + effects).
    /// </summary>
    public class VoiceEngine : IDisposable, ISampleProvider
    {
        public const int SampleRate = 48000;

        private WaveInEvent waveIn;
        private WaveOutEvent waveOut;
        private BufferedWaveProvider bufferedWave;
        private ISampleProvider sampleSource;
        private VolumeSampleProvider volumeStage;
        private RecordingTapProvider tapStage;
        private SampleToWaveProvider16 outputStage;
        private AiVoiceStage aiStage;

        private readonly object chainLock = new object();
        private List<ISampleEffect> currentEffects = new List<ISampleEffect>();
        private VoicePreset currentPreset;

        public bool IsRunning { get; private set; }

        /// <summary>Raised after the engine starts (true) or stops (false).</summary>
        public event Action<bool> StateChanged;

        /// <summary>Raised when the engine fails (device busy, unplugged, ...).</summary>
        public event Action<string> ErrorOccurred;

        /// <summary>Recording tap; use Start/StopRecording to capture the processed voice.</summary>
        public RecordingTapProvider Tap
        {
            get { return tapStage; }
        }

        public WaveFormat WaveFormat
        {
            get
            {
                if (sampleSource != null) return sampleSource.WaveFormat;
                return new WaveFormat(SampleRate, 16, 1);
            }
        }

        public void Start(int micDeviceIndex, int outputDeviceIndex, int latencyMs, float masterVolume, VoicePreset preset)
        {
            Start(micDeviceIndex, outputDeviceIndex, latencyMs, masterVolume, preset, null);
        }

        public void Start(int micDeviceIndex, int outputDeviceIndex, int latencyMs, float masterVolume,
                          VoicePreset preset, AiVoiceStage ai)
        {
            if (IsRunning) Stop();
            currentPreset = preset;
            aiStage = ai;

            try
            {
                if (latencyMs < 40) latencyMs = 40;
                if (latencyMs > 200) latencyMs = 200;

                bufferedWave = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1));
                bufferedWave.DiscardOnBufferOverflow = true;
                bufferedWave.BufferDuration = TimeSpan.FromSeconds(2);

                sampleSource = new Pcm16BitToSampleProvider(bufferedWave);
                currentEffects = BuildEffects(preset, SampleRate);

                volumeStage = new VolumeSampleProvider(this);
                volumeStage.Volume = masterVolume;
                tapStage = new RecordingTapProvider(volumeStage);
                outputStage = new SampleToWaveProvider16(tapStage);

                waveOut = new WaveOutEvent();
                waveOut.DeviceNumber = outputDeviceIndex;
                waveOut.DesiredLatency = latencyMs + 30;
                waveOut.NumberOfBuffers = 3;
                waveOut.Init(outputStage);
                waveOut.Play();

                waveIn = new WaveInEvent();
                waveIn.DeviceNumber = micDeviceIndex;
                waveIn.WaveFormat = new WaveFormat(SampleRate, 16, 1);
                waveIn.BufferMilliseconds = latencyMs;
                waveIn.NumberOfBuffers = 3;
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.RecordingStopped += OnRecordingStopped;
                waveIn.StartRecording();

                IsRunning = true;
                RaiseStateChanged(true);
            }
            catch (Exception ex)
            {
                SafeTeardown();
                IsRunning = false;
                RaiseStateChanged(false);
                string msg = "Failed to start the voice engine: " + ex.Message;
                Action<string> handler = ErrorOccurred;
                if (handler != null) handler(msg);
            }
        }

        public void Stop()
        {
            if (!IsRunning && waveIn == null && waveOut == null) return;
            SafeTeardown();
            if (IsRunning)
            {
                IsRunning = false;
                RaiseStateChanged(false);
            }
        }

        /// <summary>Swap the active effect chain while running (click-free rebuild).</summary>
        public void UpdatePreset(VoicePreset preset)
        {
            if (preset == null) return;
            currentPreset = preset;
            int sr = SampleRate;
            List<ISampleEffect> fresh = BuildEffects(preset, sr);
            lock (chainLock)
            {
                currentEffects = fresh;
            }
        }

        public void UpdateVolume(float masterVolume)
        {
            if (volumeStage != null) volumeStage.Volume = masterVolume;
        }

        /// <summary>ISampleProvider: pull from the capture buffer and run the DSP chain.</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            ISampleProvider source = sampleSource;
            if (source == null)
            {
                // Engine torn down while the output thread is still pulling: emit silence.
                if (buffer != null && offset >= 0 && offset + count <= buffer.Length)
                {
                    Array.Clear(buffer, offset, count);
                }
                return count;
            }

            int read = source.Read(buffer, offset, count);
            if (read <= 0) return read;

            // AI voice conversion sits between capture and the DSP chain, so
            // DSP presets can still be stacked on top of the neural voice.
            AiVoiceStage ai = aiStage;
            if (ai != null)
            {
                try
                {
                    ai.Process(buffer, offset, read);
                }
                catch
                {
                    // The AI stage must never kill the audio thread.
                }
            }

            List<ISampleEffect> snapshot;
            lock (chainLock)
            {
                snapshot = currentEffects;
            }
            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i].Process(buffer, offset, read);
                }
                catch
                {
                    // A faulty effect must never kill the audio thread.
                }
            }
            return read;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            BufferedWaveProvider target = bufferedWave;
            if (target != null)
            {
                target.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            // Capture died (mic unplugged, privacy setting, device busy...).
            SafeTeardown();
            if (IsRunning)
            {
                IsRunning = false;
                RaiseStateChanged(false);
                string msg = e.Exception != null
                    ? "Microphone capture stopped unexpectedly: " + e.Exception.Message
                    : "Microphone capture stopped.";
                Action<string> handler = ErrorOccurred;
                if (handler != null) handler(msg);
            }
        }

        private void SafeTeardown()
        {
            if (tapStage != null) tapStage.AbandonRecording();

            WaveInEvent input = waveIn;
            waveIn = null;
            if (input != null)
            {
                try
                {
                    input.DataAvailable -= OnDataAvailable;
                    input.RecordingStopped -= OnRecordingStopped;
                    input.StopRecording();
                }
                catch { }
                try { input.Dispose(); } catch { }
            }

            WaveOutEvent output = waveOut;
            waveOut = null;
            if (output != null)
            {
                try { output.Stop(); } catch { }
                try { output.Dispose(); } catch { }
            }

            bufferedWave = null;
            sampleSource = null;
            volumeStage = null;
            tapStage = null;
            outputStage = null;
            aiStage = null;   // stage itself is owned by MainForm, not disposed here
        }

        private void RaiseStateChanged(bool running)
        {
            Action<bool> handler = StateChanged;
            if (handler != null) handler(running);
        }

        private static List<ISampleEffect> BuildEffects(VoicePreset p, int sampleRate)
        {
            List<ISampleEffect> list = new List<ISampleEffect>();

            if (Math.Abs(p.PreGain - 1.0) > 0.01)
            {
                list.Add(new GainEffect(p.PreGain));
            }

            if (p.RingModFreq > 0.5)
            {
                list.Add(new RingModulator(sampleRate, p.RingModFreq));
            }

            if (Math.Abs(p.PitchSemitones) > 0.01)
            {
                PitchShifter shifter = new PitchShifter(sampleRate, 0.08);
                shifter.SetSemitones(p.PitchSemitones);
                list.Add(shifter);
            }

            if (!string.IsNullOrEmpty(p.Filter) && p.FilterFreq > 20.0)
            {
                BiquadFilter bq = new BiquadFilter(sampleRate);
                BiquadFilter.BiquadType type;
                switch (p.Filter)
                {
                    case "lowpass": type = BiquadFilter.BiquadType.LowPass; break;
                    case "highpass": type = BiquadFilter.BiquadType.HighPass; break;
                    default: type = BiquadFilter.BiquadType.BandPass; break;
                }
                bq.Set(type, p.FilterFreq, p.FilterQ);
                list.Add(bq);
            }

            if (p.FlangerOn)
            {
                list.Add(new FlangerEffect(sampleRate, p.FlangerRate, p.FlangerDepth));
            }

            if (p.DistortionDrive > 1.05)
            {
                DistortionEffect dist = new DistortionEffect();
                dist.SetDrive(p.DistortionDrive);
                list.Add(dist);
            }

            if (p.ReverbMix > 0.01)
            {
                ReverbEffect reverb = new ReverbEffect(sampleRate, p.ReverbMix, p.ReverbDecay);
                list.Add(reverb);
            }

            if (p.EchoDelayMs > 5.0 && p.EchoMix > 0.01)
            {
                list.Add(new EchoEffect(sampleRate, p.EchoDelayMs, p.EchoFeedback, p.EchoMix));
            }

            return list;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Sits at the end of the pipeline. While a WaveFileWriter is attached it writes
    /// every passing sample, so recordings contain the fully processed voice.
    /// </summary>
    public class RecordingTapProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly object recordLock = new object();
        private WaveFileWriter writer;
        private string writerPath;

        public RecordingTapProvider(ISampleProvider source)
        {
            this.source = source;
        }

        public WaveFormat WaveFormat
        {
            get { return source.WaveFormat; }
        }

        public bool IsRecording
        {
            get
            {
                lock (recordLock)
                {
                    return writer != null;
                }
            }
        }

        public void StartRecording(string path)
        {
            lock (recordLock)
            {
                CloseWriter();
                WaveFormat fmt = source.WaveFormat;
                writer = new WaveFileWriter(path, new WaveFormat(fmt.SampleRate, 16, fmt.Channels));
                writerPath = path;
            }
        }

        /// <summary>Stops recording and returns the file path (null if not recording).</summary>
        public string StopRecording()
        {
            lock (recordLock)
            {
                string path = writerPath;
                CloseWriter();
                return path;
            }
        }

        /// <summary>Force-closes the writer without expecting a return value (teardown path).</summary>
        public void AbandonRecording()
        {
            lock (recordLock)
            {
                CloseWriter();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            if (read > 0)
            {
                lock (recordLock)
                {
                    if (writer != null)
                    {
                        try
                        {
                            writer.WriteSamples(buffer, offset, read);
                        }
                        catch
                        {
                            // Never let disk problems kill the audio stream.
                        }
                    }
                }
            }
            return read;
        }

        private void CloseWriter()
        {
            WaveFileWriter w = writer;
            writer = null;
            writerPath = null;
            if (w != null)
            {
                try { w.Flush(); } catch { }
                try { w.Dispose(); } catch { }
            }
        }
    }
}
