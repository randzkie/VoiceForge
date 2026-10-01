using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NAudio.Wave;

namespace VoiceForge.Audio
{
    /// <summary>
    /// Plays soundboard clips and recordings. Supports several simultaneous playbacks;
    /// a UI-timer prunes finished streams and frees their devices.
    /// </summary>
    public class SoundboardPlayer : IDisposable
    {
        private class ActivePlayback
        {
            public WaveOutEvent Output;
            public AudioFileReader Reader;
        }

        private readonly List<ActivePlayback> active = new List<ActivePlayback>();
        private readonly Timer cleanupTimer;
        private int outputDeviceIndex = -1;
        private float volume = 1.0f;

        public event Action<string> PlaybackError;

        /// <summary>WMME device index used for all playback (-1 = system default).</summary>
        public int OutputDeviceIndex
        {
            get { return outputDeviceIndex; }
            set { outputDeviceIndex = value; }
        }

        public float Volume
        {
            get { return volume; }
            set
            {
                volume = value;
                lock (active)
                {
                    for (int i = 0; i < active.Count; i++)
                    {
                        active[i].Reader.Volume = value;
                    }
                }
            }
        }

        public SoundboardPlayer()
        {
            cleanupTimer = new Timer();
            cleanupTimer.Interval = 400;
            cleanupTimer.Tick += CleanupTick;
            cleanupTimer.Start();
        }

        /// <summary>Plays a WAV/MP3/AIFF file. Returns false (and fires PlaybackError) on failure.</summary>
        public bool Play(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                AudioFileReader reader = new AudioFileReader(filePath);
                reader.Volume = volume;

                WaveOutEvent output = new WaveOutEvent();
                output.DeviceNumber = outputDeviceIndex;
                output.DesiredLatency = 150;
                output.NumberOfBuffers = 3;
                output.Init(reader);
                output.Play();

                ActivePlayback pb = new ActivePlayback();
                pb.Output = output;
                pb.Reader = reader;
                lock (active)
                {
                    active.Add(pb);
                }
                return true;
            }
            catch (Exception ex)
            {
                Action<string> handler = PlaybackError;
                if (handler != null) handler("Could not play \"" + System.IO.Path.GetFileName(filePath) + "\": " + ex.Message);
                return false;
            }
        }

        public void StopAll()
        {
            List<ActivePlayback> snapshot;
            lock (active)
            {
                snapshot = new List<ActivePlayback>(active);
                active.Clear();
            }
            for (int i = 0; i < snapshot.Count; i++)
            {
                DisposePlayback(snapshot[i]);
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (active)
                {
                    return active.Count > 0;
                }
            }
        }

        private void CleanupTick(object sender, EventArgs e)
        {
            List<ActivePlayback> finished = new List<ActivePlayback>();
            lock (active)
            {
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    if (active[i].Output.PlaybackState != PlaybackState.Playing)
                    {
                        finished.Add(active[i]);
                        active.RemoveAt(i);
                    }
                }
            }
            for (int i = 0; i < finished.Count; i++)
            {
                DisposePlayback(finished[i]);
            }
        }

        private static void DisposePlayback(ActivePlayback pb)
        {
            try { pb.Output.Stop(); } catch { }
            try { pb.Output.Dispose(); } catch { }
            try { pb.Reader.Dispose(); } catch { }
        }

        public void Dispose()
        {
            cleanupTimer.Stop();
            cleanupTimer.Dispose();
            StopAll();
        }
    }
}
