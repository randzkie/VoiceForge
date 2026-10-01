using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave;
using VoiceForge.AI;
using VoiceForge.Audio;
using VoiceForge.Models;
using VoiceForge.Services;
using VoiceForge.UI.Controls;
using VoiceForge.UI.Pages;

namespace VoiceForge.UI
{
    /// <summary>
    /// Main application window: sidebar navigation, five pages, and all wiring
    /// between the UI, the voice engine, the AI stage, the soundboard, hotkeys
    /// and settings.
    /// </summary>
    public class MainForm : ChromeForm
    {
        private readonly SettingsService settingsService = new SettingsService();
        private readonly VoiceEngine engine = new VoiceEngine();
        private readonly SoundboardPlayer soundboard = new SoundboardPlayer();
        private readonly HotkeyService hotkeys = new HotkeyService();
        private readonly AiVoiceStage aiStage = new AiVoiceStage();

        private Palette pal;
        private Panel sidebar;
        private Label logoLabel;
        private Label statusLabel;
        private SidebarButton navVoice;
        private SidebarButton navAi;
        private SidebarButton navSound;
        private SidebarButton navRec;
        private SidebarButton navSettings;
        private Panel content;

        private VoiceChangerPage voicePage;
        private AiVoicePage aiPage;
        private SoundboardPage soundPage;
        private RecorderPage recPage;
        private SettingsPage setPage;

        private string selectedVoiceId;

        public MainForm()
        {
            Text = "VoiceForge";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 700);
            MinimumSize = new Size(920, 620);

            pal = ThemeService.Get(settingsService.Settings.Theme);
            ApplyChromeTheme(pal);

            BuildContent();
            BuildSidebar();

            selectedVoiceId = settingsService.Settings.SelectedVoiceId;
            voicePage.PopulateCards(VoiceCatalog.GetAll());
            voicePage.SetVoiceSelection(selectedVoiceId);
            voicePage.SetCustomValues(
                settingsService.Settings.CustomPitch,
                settingsService.Settings.CustomReverb,
                settingsService.Settings.CustomDecay,
                settingsService.Settings.CustomEchoDelay,
                settingsService.Settings.CustomEchoMix,
                settingsService.Settings.CustomDrive);
            soundPage.SetFiles(settingsService.Settings.SoundboardFiles);

            aiStage.Enabled = settingsService.Settings.AiEnabled;
            aiStage.Transpose = settingsService.Settings.AiTranspose;
            aiStage.BlockMs = settingsService.Settings.AiBlockMs;
            aiStage.Gain = settingsService.Settings.AiGain;
            aiStage.StatusChanged += OnAiStatus;
            aiPage.RefreshModels(settingsService.Settings.AiModelFile);
            aiPage.SetEnabled(settingsService.Settings.AiEnabled);
            aiPage.SetTranspose(settingsService.Settings.AiTranspose);
            aiPage.SetBlockMs(settingsService.Settings.AiBlockMs);
            aiPage.SetGain(settingsService.Settings.AiGain);
            voicePage.SetAiModels(AiVoiceStage.ScanModels(), settingsService.Settings.AiModelFile);
            voicePage.SetAiMode(settingsService.Settings.AiEnabled);
            voicePage.SetAiState(settingsService.Settings.AiEnabled ? "AI starting..." :
                "AI off - flip the switch to activate the neural voice", false);
            if (settingsService.Settings.AiEnabled && !string.IsNullOrEmpty(settingsService.Settings.AiModelFile))
            {
                LoadAiModel(settingsService.Settings.AiModelFile);
                if (aiStage.IsReady)
                {
                    voicePage.SetAiState("AI on: " + aiStage.LoadedModelName, true);
                    aiPage.SetStatus("AI voice ready: " + aiStage.LoadedModelName);
                }
            }

            WireEvents();

            engine.StateChanged += OnEngineStateChanged;
            engine.ErrorOccurred += OnEngineError;
            soundboard.PlaybackError += OnPlaybackError;
            soundboard.OutputDeviceIndex = ResolveOutputIndex(settingsService.Settings.OutputDeviceName);
            soundboard.Volume = settingsService.Settings.MasterVolume;

            hotkeys.HotkeyPressed += OnHotkeyPressed;

            ApplyAllTheme();
            ShowPage(0);

            Load += OnFormLoaded;
            FormClosing += OnFormClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeys.Attach(Handle);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            hotkeys.UnregisterAll();
            base.OnHandleDestroyed(e);
        }

        // ---------- UI construction ----------

        private void BuildSidebar()
        {
            sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 208;

            logoLabel = new Label();
            logoLabel.Text = "VOICEFORGE";
            logoLabel.AutoSize = false;
            logoLabel.SetBounds(24, 22, 170, 30);
            logoLabel.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            logoLabel.TextAlign = ContentAlignment.MiddleLeft;

            Label versionLabel = new Label();
            versionLabel.Text = "v1.0 - voice changer";
            versionLabel.AutoSize = false;
            versionLabel.SetBounds(24, 52, 170, 18);
            versionLabel.Font = new Font("Segoe UI", 8f);

            navVoice = MakeNav("Voice Changer", "V", Color.FromArgb(0, 229, 192), 92);
            navAi = MakeNav("AI Voices", "AI", Color.FromArgb(149, 117, 205), 146);
            navSound = MakeNav("Soundboard", "S", Color.FromArgb(255, 77, 141), 200);
            navRec = MakeNav("Recorder", "R", Color.FromArgb(255, 179, 71), 254);
            navSettings = MakeNav("Settings", "G", Color.FromArgb(79, 195, 247), 308);

            navVoice.Click += delegate { ShowPage(0); };
            navAi.Click += delegate { ShowPage(1); };
            navSound.Click += delegate { ShowPage(2); };
            navRec.Click += delegate { ShowPage(3); };
            navSettings.Click += delegate { ShowPage(4); };

            statusLabel = new Label();
            statusLabel.Text = "";
            statusLabel.AutoSize = false;
            statusLabel.SetBounds(20, 560, 176, 80);
            statusLabel.Font = new Font("Segoe UI", 7.75f);
            statusLabel.TextAlign = ContentAlignment.TopLeft;
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            sidebar.Controls.Add(logoLabel);
            sidebar.Controls.Add(versionLabel);
            sidebar.Controls.Add(navVoice);
            sidebar.Controls.Add(navAi);
            sidebar.Controls.Add(navSound);
            sidebar.Controls.Add(navRec);
            sidebar.Controls.Add(navSettings);
            sidebar.Controls.Add(statusLabel);

            ContentRoot.Controls.Add(sidebar);
        }

        private SidebarButton MakeNav(string text, string badge, Color badgeColor, int y)
        {
            SidebarButton b = new SidebarButton();
            b.Text = text;
            b.BadgeText = badge;
            b.BadgeColor = badgeColor;
            b.SetBounds(12, y, 184, 48);
            return b;
        }

        private void BuildContent()
        {
            content = new Panel();
            content.Dock = DockStyle.Fill;

            voicePage = new VoiceChangerPage();
            aiPage = new AiVoicePage();
            soundPage = new SoundboardPage();
            recPage = new RecorderPage();
            setPage = new SettingsPage();
            voicePage.Dock = DockStyle.Fill;
            aiPage.Dock = DockStyle.Fill;
            soundPage.Dock = DockStyle.Fill;
            recPage.Dock = DockStyle.Fill;
            setPage.Dock = DockStyle.Fill;

            content.Controls.Add(voicePage);
            content.Controls.Add(aiPage);
            content.Controls.Add(soundPage);
            content.Controls.Add(recPage);
            content.Controls.Add(setPage);

            ContentRoot.Controls.Add(content);
        }

        private void ShowPage(int index)
        {
            voicePage.Visible = index == 0;
            aiPage.Visible = index == 1;
            soundPage.Visible = index == 2;
            recPage.Visible = index == 3;
            setPage.Visible = index == 4;

            UserControl shown = index == 0 ? (UserControl)voicePage :
                                index == 1 ? (UserControl)aiPage :
                                index == 2 ? (UserControl)soundPage :
                                index == 3 ? (UserControl)recPage : (UserControl)setPage;
            shown.BringToFront();

            navVoice.Selected = index == 0;
            navAi.Selected = index == 1;
            navSound.Selected = index == 2;
            navRec.Selected = index == 3;
            navSettings.Selected = index == 4;
        }

        // ---------- wiring ----------

        private void WireEvents()
        {
            voicePage.PowerToggled += delegate { ToggleEngine(); };
            voicePage.VoiceSelected += OnVoiceSelected;
            voicePage.CustomParamsChanged += OnCustomParamsChanged;
            voicePage.MicDeviceChanged += OnMicChanged;
            voicePage.OutputDeviceChanged += OnOutputChanged;
            voicePage.VolumeChanged += OnVolumeChanged;

            aiPage.AiEnabledChanged += OnAiEnabledChanged;
            aiPage.AiModelSelected += OnAiModelSelected;
            aiPage.AiTransposeChanged += OnAiTransposeChanged;
            aiPage.AiBlockChanged += OnAiBlockChanged;
            aiPage.AiGainChanged += OnAiGainChanged;
            aiPage.ModelsFolderRequested += OnOpenModelsFolder;

            // Dashboard AI bar drives exactly the same handlers as the AI page,
            // so both switches and both pickers can never disagree.
            voicePage.AiModeToggled += OnAiEnabledChanged;
            voicePage.AiModelPicked += OnAiModelSelected;

            soundPage.FilesChanged += OnSoundboardFilesChanged;
            soundPage.PlayRequested += OnPlayRequested;

            recPage.RecordStartRequested = OnRecordStart;
            recPage.RecordStopRequested = OnRecordStop;
            recPage.PlayRequested += OnPlayRequested;
            recPage.InfoMessage += OnPlaybackError;

            setPage.ThemeToggled += OnThemeToggled;
            setPage.LatencyChanged += OnLatencyChanged;
            setPage.HotkeysToggled += OnHotkeysToggled;
            setPage.ResetRequested += OnResetRequested;
        }

        private void OnFormLoaded(object sender, EventArgs e)
        {
            PopulateDevices();
            setPage.SetValues(
                settingsService.Settings.Theme == "light",
                settingsService.Settings.LatencyMs,
                settingsService.Settings.HotkeysEnabled);

            if (settingsService.Settings.WindowMaximized)
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }

            if (settingsService.Settings.PowerOnStart)
            {
                StartEngine();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            settingsService.Settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            SaveSettings();
            engine.Dispose();
            soundboard.Dispose();
            hotkeys.Dispose();
            aiStage.Dispose();
        }

        // ---------- engine control ----------

        private void ToggleEngine()
        {
            if (engine.IsRunning)
            {
                engine.Stop();
            }
            else
            {
                StartEngine();
            }
        }

        private void StartEngine()
        {
            string micName = voicePage.SelectedMicName();
            string outName = voicePage.SelectedOutName();

            settingsService.Settings.MicDeviceName = micName;
            settingsService.Settings.OutputDeviceName = outName;
            SaveSettings();

            soundboard.OutputDeviceIndex = ResolveOutputIndex(outName);
            engine.Start(ResolveMicIndex(micName), ResolveOutputIndex(outName),
                settingsService.Settings.LatencyMs, settingsService.Settings.MasterVolume,
                CurrentPreset(), aiStage);
        }

        private void RestartEngineIfRunning()
        {
            if (engine.IsRunning)
            {
                engine.Stop();
                StartEngine();
            }
        }

        private VoicePreset CurrentPreset()
        {
            if (string.Equals(selectedVoiceId, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return voicePage.GetCustomPreset();
            }
            return VoiceCatalog.Find(selectedVoiceId);
        }

        private void OnEngineStateChanged(bool running)
        {
            voicePage.SetPowerState(running);
            recPage.SetEngineState(running);
            statusLabel.Text = running ? "Engine running" : "";

            // Keep the AI bar truthful: the engine can stop on its own (mic
            // error) or via the power button while the AI voice stays loaded.
            if (aiStage.IsReady && aiStage.Enabled)
            {
                voicePage.SetAiState(running
                    ? "AI on: " + aiStage.LoadedModelName + " (about one chunk of delay)"
                    : "AI ready: " + aiStage.LoadedModelName + " - press VOICE ON to hear it", running);
            }
        }

        private void OnEngineError(string message)
        {
            statusLabel.Text = message;
            MessageBox.Show(this, message, "VoiceForge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnVoiceSelected(VoicePreset preset)
        {
            selectedVoiceId = preset.Id;
            settingsService.Settings.SelectedVoiceId = preset.Id;
            SaveSettings();
            if (engine.IsRunning)
            {
                engine.UpdatePreset(CurrentPreset());
            }
        }

        private void OnCustomParamsChanged()
        {
            VoicePreset cp = voicePage.GetCustomPreset();
            settingsService.Settings.CustomPitch = cp.PitchSemitones;
            settingsService.Settings.CustomReverb = cp.ReverbMix * 100.0;
            settingsService.Settings.CustomDecay = (cp.ReverbDecay - 0.3) / 4.7 * 100.0;
            settingsService.Settings.CustomEchoDelay = cp.EchoDelayMs;
            settingsService.Settings.CustomEchoMix = cp.EchoMix * 100.0;
            settingsService.Settings.CustomDrive = (cp.DistortionDrive - 1.0) / 6.0 * 100.0;
            SaveSettings();
            if (engine.IsRunning && string.Equals(selectedVoiceId, "custom", StringComparison.OrdinalIgnoreCase))
            {
                engine.UpdatePreset(cp);
            }
        }

        private void OnMicChanged(string name)
        {
            settingsService.Settings.MicDeviceName = name ?? "";
            SaveSettings();
            RestartEngineIfRunning();
        }

        private void OnOutputChanged(string name)
        {
            settingsService.Settings.OutputDeviceName = name ?? "";
            SaveSettings();
            soundboard.OutputDeviceIndex = ResolveOutputIndex(name);
            RestartEngineIfRunning();
        }

        private void OnVolumeChanged(float volume)
        {
            settingsService.Settings.MasterVolume = volume;
            engine.UpdateVolume(volume);
            soundboard.Volume = volume;
            SaveSettings();
        }

        // ---------- soundboard & hotkeys ----------

        private void OnSoundboardFilesChanged(List<string> files)
        {
            settingsService.Settings.SoundboardFiles = files;
            SaveSettings();
            RegisterHotkeys();
        }

        private void OnPlayRequested(string path)
        {
            soundboard.Play(path);
        }

        private void RegisterHotkeys()
        {
            hotkeys.UnregisterAll();
            if (!settingsService.Settings.HotkeysEnabled) return;
            List<string> files = settingsService.Settings.SoundboardFiles;
            for (int n = 1; n <= 9; n++)
            {
                if (files.Count >= n)
                {
                    hotkeys.Register(200 + n, (Keys)(0x30 + n), true, true, false);
                }
            }
        }

        private void OnHotkeyPressed(int id)
        {
            if (id < 201 || id > 209) return;
            int idx = id - 201;
            List<string> files = settingsService.Settings.SoundboardFiles;
            if (idx < files.Count)
            {
                soundboard.Play(files[idx]);
            }
        }

        private void OnHotkeysToggled(bool enabled)
        {
            settingsService.Settings.HotkeysEnabled = enabled;
            SaveSettings();
            RegisterHotkeys();
        }

        private void OnPlaybackError(string message)
        {
            statusLabel.Text = message;
        }

        // ---------- AI voice ----------

        private void LoadAiModel(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Cursor prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                aiPage.SetStatus("Loading AI voice (a few seconds on first load)...");
                aiStage.LoadModel(path);
                if (aiStage.IsReady)
                {
                    settingsService.Settings.AiModelFile = path;
                    SaveSettings();
                    aiPage.RefreshModels(path);
                    voicePage.SetAiModels(AiVoiceStage.ScanModels(), path);
                }
            }
            finally
            {
                Cursor = prev;
            }
        }

        /// <summary>
        /// Single source of truth for the AI switch (dashboard + AI page both
        /// land here). Loads the model if needed, activates the stage and -
        /// importantly - starts the audio engine when it is not running, so
        /// flipping AI on really does turn the converted voice on.
        /// </summary>
        private void OnAiEnabledChanged(bool on)
        {
            settingsService.Settings.AiEnabled = on;
            SaveSettings();

            if (!on)
            {
                aiStage.Enabled = false;
                aiPage.SetEnabled(false);
                voicePage.SetAiMode(false);
                aiPage.SetStatus("AI voice off.");
                voicePage.SetAiState("AI off - flip the switch to activate the neural voice", false);
                return;
            }

            if (!aiStage.IsReady)
            {
                string model = aiPage.SelectedModelPath ?? voicePage.SelectedAiModelPath();
                if (model == null)
                {
                    RevertAiSwitch();
                    aiPage.SetStatus("No AI voice model found - put .onnx voices in the models folder and press RESCAN.");
                    voicePage.SetAiState("No AI models found - see the AI Voices page", false);
                    return;
                }
                aiPage.SetStatus("Loading AI voice (a few seconds on first load)...");
                voicePage.SetAiState("Loading AI voice...", false);
                LoadAiModel(model);
            }

            if (aiStage.IsReady)
            {
                aiStage.Enabled = true;
                aiPage.SetEnabled(true);
                voicePage.SetAiMode(true);
                aiPage.SetStatus("AI voice on: " + aiStage.LoadedModelName);
                voicePage.SetAiState("AI on: " + aiStage.LoadedModelName + " (about one chunk of delay)", true);

                if (!engine.IsRunning)
                {
                    StartEngine();   // the AI switch really turns the voice on
                }
                if (!engine.IsRunning)
                {
                    voicePage.SetAiState("AI loaded, but the engine could not start - check mic/output devices", false);
                }
            }
            else
            {
                RevertAiSwitch();
                string err = aiStage.LastError ?? "unknown error";
                aiPage.SetStatus("AI voice failed: " + err);
                voicePage.SetAiState("AI failed - details on the AI Voices page", false);
            }
        }

        private void RevertAiSwitch()
        {
            aiStage.Enabled = false;
            settingsService.Settings.AiEnabled = false;
            SaveSettings();
            aiPage.SetEnabled(false);
            voicePage.SetAiMode(false);
        }

        private void OnAiModelSelected(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            bool wantActive = settingsService.Settings.AiEnabled;
            aiStage.Enabled = wantActive;
            LoadAiModel(path);
            if (aiStage.IsReady)
            {
                aiStage.Enabled = wantActive;
                aiPage.SetStatus(wantActive
                    ? "AI voice on: " + aiStage.LoadedModelName
                    : "AI voice ready: " + aiStage.LoadedModelName + " - flip AI on to activate.");
                voicePage.SetAiState(wantActive
                    ? "AI on: " + aiStage.LoadedModelName
                    : "Ready: " + aiStage.LoadedModelName + " - flip AI on", wantActive);
            }
            else
            {
                aiPage.SetStatus("AI voice failed: " + (aiStage.LastError ?? "unknown error"));
                voicePage.SetAiState("AI failed - details on the AI Voices page", false);
            }
        }

        private void OnAiTransposeChanged(int semitones)
        {
            settingsService.Settings.AiTranspose = semitones;
            aiStage.Transpose = semitones;
            SaveSettings();
        }

        private void OnAiBlockChanged(int ms)
        {
            settingsService.Settings.AiBlockMs = ms;
            aiStage.BlockMs = ms;
            SaveSettings();
        }

        private void OnAiGainChanged(double gain)
        {
            settingsService.Settings.AiGain = gain;
            aiStage.Gain = gain;
            SaveSettings();
        }

        private void OnOpenModelsFolder()
        {
            try
            {
                Process.Start("explorer.exe", AiVoiceStage.PrimaryModelsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the folder: " + ex.Message, "VoiceForge");
            }
        }

        private void OnAiStatus(string message)
        {
            aiPage.SetStatus(message);
            // Mirror a short state onto the dashboard (may come from the worker thread).
            if (aiStage.IsReady)
            {
                voicePage.SetAiState(aiStage.Enabled
                    ? "AI on: " + aiStage.LoadedModelName
                    : "Ready: " + aiStage.LoadedModelName + " - flip AI on", aiStage.Enabled);
            }
            else
            {
                voicePage.SetAiState("AI stopped - " + ShortAiReason(message), false);
            }
        }

        /// <summary>Trims a full AI status message for the narrow dashboard line.</summary>
        private static string ShortAiReason(string message)
        {
            if (string.IsNullOrEmpty(message)) return "see the AI Voices page";
            message = message.Replace("AI voice stopped: ", "").Replace("AI voice failed: ", "");
            if (message.Length > 72) message = message.Substring(0, 72) + "...";
            return message;
        }

        // ---------- recorder ----------

        private bool OnRecordStart()
        {
            if (!engine.IsRunning)
            {
                MessageBox.Show(this,
                    "Turn the voice engine on first (Voice Changer page), then recording captures the processed voice.",
                    "VoiceForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (engine.Tap == null) return false;
            string path = System.IO.Path.Combine(
                SettingsService.RecordingsFolder,
                "VoiceForge_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav");
            engine.Tap.StartRecording(path);
            return true;
        }

        private void OnRecordStop()
        {
            if (engine.Tap != null)
            {
                engine.Tap.StopRecording();
            }
        }

        // ---------- settings / theme ----------

        private void OnThemeToggled(bool light)
        {
            settingsService.Settings.Theme = light ? "light" : "dark";
            pal = ThemeService.Get(settingsService.Settings.Theme);
            ApplyAllTheme();
            SaveSettings();
        }

        private void OnLatencyChanged(int latencyMs)
        {
            settingsService.Settings.LatencyMs = latencyMs;
            SaveSettings();
            RestartEngineIfRunning();
        }

        private void OnResetRequested()
        {
            DialogResult dr = MessageBox.Show(this,
                "Reset all settings to defaults? (Soundboard file list and recordings are kept.)",
                "VoiceForge", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            settingsService.ResetToDefaults();
            AppSettings s = settingsService.Settings;

            pal = ThemeService.Get(s.Theme);
            ApplyAllTheme();
            setPage.SetValues(s.Theme == "light", s.LatencyMs, s.HotkeysEnabled);

            selectedVoiceId = s.SelectedVoiceId;
            voicePage.SetVoiceSelection(selectedVoiceId);
            voicePage.SetCustomValues(s.CustomPitch, s.CustomReverb, s.CustomDecay, s.CustomEchoDelay, s.CustomEchoMix, s.CustomDrive);
            soundPage.SetFiles(s.SoundboardFiles);
            soundboard.Volume = s.MasterVolume;
            voicePage.SetPowerState(engine.IsRunning);

            aiStage.Enabled = s.AiEnabled;
            aiStage.Transpose = s.AiTranspose;
            aiStage.BlockMs = s.AiBlockMs;
            aiStage.Gain = s.AiGain;
            aiStage.Unload();
            aiPage.RefreshModels(null);
            aiPage.SetEnabled(s.AiEnabled);
            aiPage.SetTranspose(s.AiTranspose);
            aiPage.SetBlockMs(s.AiBlockMs);
            aiPage.SetGain(s.AiGain);
            aiPage.SetStatus("No AI voice loaded.");
            voicePage.SetAiModels(AiVoiceStage.ScanModels(), null);
            voicePage.SetAiMode(s.AiEnabled);
            voicePage.SetAiState("AI voice unloaded - pick a model to try again", false);

            RegisterHotkeys();
            PopulateDevices();
            SaveSettings();
        }

        private void ApplyAllTheme()
        {
            ApplyChromeTheme(pal);
            sidebar.BackColor = pal.SidebarBg;
            logoLabel.ForeColor = pal.Accent;
            statusLabel.ForeColor = pal.TextMuted;
            navVoice.ApplyPalette(pal);
            navAi.ApplyPalette(pal);
            navSound.ApplyPalette(pal);
            navRec.ApplyPalette(pal);
            navSettings.ApplyPalette(pal);
            content.BackColor = pal.WindowBg;
            voicePage.ApplyTheme(pal);
            aiPage.ApplyTheme(pal);
            soundPage.ApplyTheme(pal);
            recPage.ApplyTheme(pal);
            setPage.ApplyTheme(pal);
        }

        private void SaveSettings()
        {
            settingsService.Save();
        }

        // ---------- devices ----------

        private void PopulateDevices()
        {
            List<string> mics = new List<string>();
            List<string> outs = new List<string>();
            try
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    mics.Add(WaveIn.GetCapabilities(i).ProductName);
                }
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    outs.Add(WaveOut.GetCapabilities(i).ProductName);
                }
            }
            catch
            {
                statusLabel.Text = "Could not enumerate audio devices.";
            }
            voicePage.RefreshDevices(mics, outs,
                settingsService.Settings.MicDeviceName, settingsService.Settings.OutputDeviceName);

            bool vbDetected = false;
            for (int i = 0; i < outs.Count; i++)
            {
                if (outs[i].IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    vbDetected = true;
                    break;
                }
            }
            voicePage.SetVbCableDetected(vbDetected);
        }

        private int ResolveMicIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            try
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    string pn = WaveIn.GetCapabilities(i).ProductName;
                    if (string.Equals(pn, name, StringComparison.OrdinalIgnoreCase)) return i;
                }
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    string pn = WaveIn.GetCapabilities(i).ProductName;
                    if (pn.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(pn, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            catch { }
            return -1;
        }

        private int ResolveOutputIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            try
            {
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    string pn = WaveOut.GetCapabilities(i).ProductName;
                    if (string.Equals(pn, name, StringComparison.OrdinalIgnoreCase)) return i;
                }
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    string pn = WaveOut.GetCapabilities(i).ProductName;
                    if (pn.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(pn, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            catch { }
            return -1;
        }
    }
}
