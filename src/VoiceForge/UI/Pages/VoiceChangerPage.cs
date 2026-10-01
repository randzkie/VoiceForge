using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VoiceForge.Models;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI.Pages
{
    /// <summary>
    /// Voice changer page: POWER toggle, device pickers, the 13 voice cards
    /// (13 presets + Custom), the Custom Voice Lab slider panel and the
    /// AI VOICE bar (switch + model picker + live state).
    /// </summary>
    public class VoiceChangerPage : UserControl
    {
        private Panel topBar;
        private Panel aiBar;
        private Panel customPanel;
        private FlowLayoutPanel flow;
        private PillButton btnPower;
        private Label lblStatus;
        private Label lblMic;
        private Label lblOut;
        private Label lblVol;
        private ComboBox comboMic;
        private ComboBox comboOut;
        private ModernSlider sliderVol;
        private Label lblCustomTitle;
        private Label lblCustomHint;

        private Label lblAiTitle;
        private ToggleSwitch toggleAi;
        private ComboBox comboAi;
        private Label lblAiState;
        private readonly List<string> aiModelPaths = new List<string>();
        private string aiStateText = "AI off - flip the switch to activate the neural voice";
        private bool aiStateActive;

        private readonly Dictionary<string, VoiceCard> cards = new Dictionary<string, VoiceCard>();
        private string selectedVoiceId = "helium";

        private ModernSlider sPitch;
        private ModernSlider sReverb;
        private ModernSlider sDecay;
        private ModernSlider sEchoDelay;
        private ModernSlider sEchoMix;
        private ModernSlider sDrive;
        private Label vPitch;
        private Label vReverb;
        private Label vDecay;
        private Label vEchoDelay;
        private Label vEchoMix;
        private Label vDrive;

        private bool suppressEvents;

        public event Action<bool> PowerToggled;
        public event Action<VoicePreset> VoiceSelected;
        public event Action CustomParamsChanged;
        public event Action<string> MicDeviceChanged;
        public event Action<string> OutputDeviceChanged;
        public event Action<float> VolumeChanged;
        public event Action<bool> AiModeToggled;          // dashboard AI switch
        public event Action<string> AiModelPicked;        // dashboard model combo (full path)

        public VoiceChangerPage()
        {
            DoubleBuffered = true;
            BuildUi();
        }

        private void BuildUi()
        {
            topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 90;

            btnPower = new PillButton();
            btnPower.Size = new Size(140, 40);
            btnPower.Location = new Point(12, 10);
            btnPower.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnPower.Text = "VOICE OFF";
            btnPower.Click += delegate
            {
                Action<bool> handler = PowerToggled;
                if (handler != null) handler(true);
            };

            lblStatus = MakeLabel("Engine stopped - press VOICE OFF to start monitoring", 8.25f, true);
            lblStatus.SetBounds(166, 22, 420, 18);

            lblMic = MakeLabel("Microphone", 8f, true);
            lblMic.SetBounds(12, 58, 86, 16);
            comboMic = MakeCombo();
            comboMic.SetBounds(104, 54, 190, 26);
            comboMic.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                Action<string> handler = MicDeviceChanged;
                if (handler != null) handler(SelectedMicName());
            };

            lblOut = MakeLabel("Output", 8f, true);
            lblOut.SetBounds(318, 58, 64, 16);
            comboOut = MakeCombo();
            comboOut.SetBounds(386, 54, 190, 26);
            comboOut.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                Action<string> handler = OutputDeviceChanged;
                if (handler != null) handler(SelectedOutName());
            };

            lblVol = MakeLabel("Vol 100%", 8f, true);
            lblVol.SetBounds(600, 58, 70, 16);
            sliderVol = new ModernSlider();
            sliderVol.Minimum = 0;
            sliderVol.Maximum = 200;
            sliderVol.Step = 5;
            sliderVol.Value = 100;
            sliderVol.SetBounds(668, 54, 130, 26);
            sliderVol.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblVol.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sliderVol.ValueChanged += delegate
            {
                lblVol.Text = "Vol " + Math.Round(sliderVol.Value) + "%";
                if (suppressEvents) return;
                Action<float> handler = VolumeChanged;
                if (handler != null) handler((float)(sliderVol.Value / 100.0));
            };

            topBar.Controls.Add(btnPower);
            topBar.Controls.Add(lblStatus);
            topBar.Controls.Add(lblMic);
            topBar.Controls.Add(comboMic);
            topBar.Controls.Add(lblOut);
            topBar.Controls.Add(comboOut);
            topBar.Controls.Add(lblVol);
            topBar.Controls.Add(sliderVol);

            BuildCustomPanel();
            BuildAiBar();

            flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.Padding = new Padding(4);

            // Docking is applied from the last control in the collection to the
            // first, so this order yields: topBar on top, aiBar right below it,
            // customPanel at the bottom, flow fills the middle.
            Controls.Add(flow);
            Controls.Add(customPanel);
            Controls.Add(aiBar);
            Controls.Add(topBar);
        }

        private void BuildAiBar()
        {
            aiBar = new Panel();
            aiBar.Dock = DockStyle.Top;
            aiBar.Height = 46;

            lblAiTitle = MakeLabel("AI VOICE", 9f, true);
            lblAiTitle.SetBounds(14, 14, 76, 18);

            toggleAi = new ToggleSwitch();
            toggleAi.SetBounds(92, 12, 52, 26);
            toggleAi.Toggled += delegate
            {
                if (suppressEvents) return;
                Action<bool> handler = AiModeToggled;
                if (handler != null) handler(toggleAi.IsOn);
            };

            comboAi = MakeCombo();
            comboAi.SetBounds(158, 12, 214, 26);
            comboAi.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                int idx = comboAi.SelectedIndex;
                if (idx >= 0 && idx < aiModelPaths.Count)
                {
                    Action<string> handler = AiModelPicked;
                    if (handler != null) handler(aiModelPaths[idx]);
                }
            };

            lblAiState = MakeLabel(aiStateText, 8.25f, true);
            lblAiState.SetBounds(384, 16, 320, 18);
            // Keep the state line inside the bar at any window width
            // (content is 712 px wide at the 920 px minimum window size).
            aiBar.Resize += delegate
            {
                lblAiState.Width = Math.Max(120, aiBar.ClientSize.Width - 392);
            };

            aiBar.Controls.Add(lblAiTitle);
            aiBar.Controls.Add(toggleAi);
            aiBar.Controls.Add(comboAi);
            aiBar.Controls.Add(lblAiState);
        }

        private void BuildCustomPanel()
        {
            customPanel = new Panel();
            customPanel.Dock = DockStyle.Bottom;
            customPanel.Height = 158;

            lblCustomTitle = MakeLabel("CUSTOM VOICE LAB", 9.75f, true);
            lblCustomTitle.SetBounds(12, 8, 220, 20);

            lblCustomHint = MakeLabel("Active while the Custom card is selected", 8f, true);
            lblCustomHint.SetBounds(460, 12, 220, 16);
            lblCustomHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            sPitch = new ModernSlider(); sPitch.Minimum = -12; sPitch.Maximum = 12; sPitch.Step = 1; sPitch.Value = 0;
            sReverb = new ModernSlider(); sReverb.Minimum = 0; sReverb.Maximum = 100; sReverb.Step = 1; sReverb.Value = 25;
            sDecay = new ModernSlider(); sDecay.Minimum = 0; sDecay.Maximum = 100; sDecay.Step = 1; sDecay.Value = 30;
            sEchoDelay = new ModernSlider(); sEchoDelay.Minimum = 0; sEchoDelay.Maximum = 500; sEchoDelay.Step = 10; sEchoDelay.Value = 200;
            sEchoMix = new ModernSlider(); sEchoMix.Minimum = 0; sEchoMix.Maximum = 100; sEchoMix.Step = 1; sEchoMix.Value = 30;
            sDrive = new ModernSlider(); sDrive.Minimum = 0; sDrive.Maximum = 100; sDrive.Step = 1; sDrive.Value = 0;

            vPitch = MakeLabel("", 8f, true);
            vReverb = MakeLabel("", 8f, true);
            vDecay = MakeLabel("", 8f, true);
            vEchoDelay = MakeLabel("", 8f, true);
            vEchoMix = MakeLabel("", 8f, true);
            vDrive = MakeLabel("", 8f, true);

            customPanel.Controls.Add(lblCustomTitle);
            customPanel.Controls.Add(lblCustomHint);
            AddLab("Pitch", sPitch, vPitch, 12, 36);
            AddLab("Reverb mix", sReverb, vReverb, 238, 36);
            AddLab("Reverb decay", sDecay, vDecay, 464, 36);
            AddLab("Echo delay", sEchoDelay, vEchoDelay, 12, 96);
            AddLab("Echo mix", sEchoMix, vEchoMix, 238, 96);
            AddLab("Distortion", sDrive, vDrive, 464, 96);

            foreach (Control c in customPanel.Controls)
            {
                ModernSlider ms = c as ModernSlider;
                if (ms != null)
                {
                    ms.ValueChanged += delegate
                    {
                        UpdateValueLabels();
                        if (suppressEvents) return;
                        Action handler = CustomParamsChanged;
                        if (handler != null) handler();
                    };
                }
            }
            UpdateValueLabels();
        }

        private void AddLab(string title, ModernSlider slider, Label valueLabel, int x, int y)
        {
            Label l = MakeLabel(title, 8f, true);
            l.SetBounds(x, y, 130, 16);
            valueLabel.SetBounds(x + 130, y, 76, 16);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            slider.SetBounds(x, y + 18, 210, 26);
            customPanel.Controls.Add(l);
            customPanel.Controls.Add(valueLabel);
            customPanel.Controls.Add(slider);
        }

        private static Label MakeLabel(string text, float size, bool mutedStyle)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = new Font("Segoe UI", size);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private static ComboBox MakeCombo()
        {
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Font = new Font("Segoe UI", 8.75f);
            return cb;
        }

        public void PopulateCards(List<VoicePreset> presets)
        {
            flow.SuspendLayout();
            flow.Controls.Clear();
            cards.Clear();
            for (int i = 0; i < presets.Count; i++)
            {
                VoicePreset p = presets[i];
                VoiceCard card = new VoiceCard();
                card.Bind(p);
                card.Margin = new Padding(8);
                card.VoiceSelected += OnCardSelected;
                cards[p.Id] = card;
                flow.Controls.Add(card);
            }
            flow.ResumeLayout();
            SetVoiceSelection(selectedVoiceId);
        }

        private void OnCardSelected(object sender, VoicePreset preset)
        {
            SetVoiceSelection(preset.Id);
            Action<VoicePreset> handler = VoiceSelected;
            if (handler != null) handler(preset);
        }

        public void SetVoiceSelection(string id)
        {
            selectedVoiceId = id;
            foreach (KeyValuePair<string, VoiceCard> kv in cards)
            {
                kv.Value.Selected = string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string SelectedVoiceId
        {
            get { return selectedVoiceId; }
        }

        // ---------- AI bar API (state owned by MainForm) ----------

        /// <summary>Mirrors the AI switch without raising AiModeToggled.</summary>
        public void SetAiMode(bool on)
        {
            suppressEvents = true;
            toggleAi.IsOn = on;
            suppressEvents = false;
        }

        /// <summary>Fills the dashboard model picker (display names + selection).</summary>
        public void SetAiModels(List<string> paths, string selectedPath)
        {
            suppressEvents = true;
            aiModelPaths.Clear();
            if (paths != null) aiModelPaths.AddRange(paths);

            comboAi.Items.Clear();
            if (aiModelPaths.Count == 0)
            {
                comboAi.Items.Add("(no AI models found - see AI Voices page)");
                comboAi.SelectedIndex = 0;
            }
            else
            {
                for (int i = 0; i < aiModelPaths.Count; i++)
                {
                    comboAi.Items.Add(System.IO.Path.GetFileNameWithoutExtension(aiModelPaths[i]));
                }
                int sel = -1;
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    for (int i = 0; i < aiModelPaths.Count; i++)
                    {
                        if (string.Equals(aiModelPaths[i], selectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            sel = i;
                            break;
                        }
                    }
                }
                comboAi.SelectedIndex = sel >= 0 ? sel : 0;
            }
            suppressEvents = false;
        }

        public string SelectedAiModelPath()
        {
            int idx = comboAi.SelectedIndex;
            if (idx >= 0 && idx < aiModelPaths.Count) return aiModelPaths[idx];
            return null;
        }

        /// <summary>Live AI state line (thread-safe; called from the AI worker too).</summary>
        public void SetAiState(string text, bool active)
        {
            if (lblAiState != null && lblAiState.InvokeRequired)
            {
                try { BeginInvoke(new Action<string, bool>(SetAiState), text, active); } catch { }
                return;
            }
            aiStateText = text;
            aiStateActive = active;
            if (lblAiState != null)
            {
                lblAiState.Text = text;
                lblAiState.ForeColor = active ? Color.FromArgb(149, 117, 205) : SystemColors.GrayText;
            }
        }

        private bool vbDetected;

        public void SetPowerState(bool running)
        {
            btnPower.Text = running ? "VOICE ON" : "VOICE OFF";
            UpdateStatusText();
            btnPower.Invalidate();
            if (ApplyPowerPalette != null) ApplyPowerPalette(running);
        }

        public Action<bool> ApplyPowerPalette; // set by ApplyTheme / SetPowerState

        private void UpdateStatusText()
        {
            bool running = btnPower.Text == "VOICE ON";
            if (running)
            {
                lblStatus.Text = "Engine running - your processed voice goes to the selected output";
            }
            else if (vbDetected)
            {
                lblStatus.Text = "Engine stopped - VB-Cable detected: pick it as Output to get a virtual mic";
            }
            else
            {
                lblStatus.Text = "Engine stopped - press the button to start monitoring";
            }
            lblStatus.ForeColor = running ? Color.FromArgb(70, 214, 140) : SystemColors.GrayText;
        }

        public void SetVbCableDetected(bool detected)
        {
            vbDetected = detected;
            UpdateStatusText();
        }

        public void RefreshDevices(List<string> mics, List<string> outs, string selMic, string selOut)
        {
            suppressEvents = true;
            comboMic.Items.Clear();
            for (int i = 0; i < mics.Count; i++) comboMic.Items.Add(mics[i]);
            comboOut.Items.Clear();
            for (int i = 0; i < outs.Count; i++) comboOut.Items.Add(outs[i]);

            int mi = SelectByName(comboMic, selMic);
            int oi = SelectByName(comboOut, selOut);
            if (mi < 0 && comboMic.Items.Count > 0) comboMic.SelectedIndex = 0;
            if (oi < 0 && comboOut.Items.Count > 0) comboOut.SelectedIndex = 0;
            suppressEvents = false;
        }

        private static int SelectByName(ComboBox cb, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals((string)cb.Items[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return i;
                }
            }
            return -1;
        }

        public string SelectedMicName()
        {
            return comboMic.SelectedItem as string ?? "";
        }

        public string SelectedOutName()
        {
            return comboOut.SelectedItem as string ?? "";
        }

        public void SetCustomValues(double pitch, double reverb, double decay, double echoDelay, double echoMix, double drive)
        {
            suppressEvents = true;
            sPitch.Value = pitch;
            sReverb.Value = reverb;
            sDecay.Value = decay;
            sEchoDelay.Value = echoDelay;
            sEchoMix.Value = echoMix;
            sDrive.Value = drive;
            suppressEvents = false;
            UpdateValueLabels();
        }

        public VoicePreset GetCustomPreset()
        {
            VoicePreset template = VoiceCatalog.Find("custom");
            VoicePreset p = template.Clone();
            p.PitchSemitones = sPitch.Value;
            p.ReverbMix = sReverb.Value / 100.0;
            p.ReverbDecay = 0.3 + (sDecay.Value / 100.0) * 4.7;
            p.EchoDelayMs = sEchoDelay.Value;
            p.EchoMix = sEchoMix.Value / 100.0;
            p.EchoFeedback = 0.35;
            p.DistortionDrive = 1.0 + (sDrive.Value / 100.0) * 6.0;
            return p;
        }

        private void UpdateValueLabels()
        {
            vPitch.Text = (sPitch.Value > 0 ? "+" : "") + Math.Round(sPitch.Value) + " st";
            vReverb.Text = Math.Round(sReverb.Value) + "%";
            vDecay.Text = (0.3 + (sDecay.Value / 100.0) * 4.7).ToString("0.0") + " s";
            vEchoDelay.Text = Math.Round(sEchoDelay.Value) + " ms";
            vEchoMix.Text = Math.Round(sEchoMix.Value) + "%";
            double d = 1.0 + (sDrive.Value / 100.0) * 6.0;
            vDrive.Text = sDrive.Value <= 0 ? "off" : "x" + d.ToString("0.0");
        }

        public void ApplyTheme(Palette p)
        {
            BackColor = p.WindowBg;
            topBar.BackColor = p.WindowBg;
            aiBar.BackColor = p.WindowBg;
            customPanel.BackColor = p.CardBg;
            flow.BackColor = p.WindowBg;

            lblStatus.ForeColor = SystemColors.GrayText;
            lblMic.ForeColor = p.TextMuted;
            lblOut.ForeColor = p.TextMuted;
            lblVol.ForeColor = p.TextMuted;
            lblCustomTitle.ForeColor = p.TextPrimary;
            lblCustomHint.ForeColor = p.TextMuted;
            vPitch.ForeColor = p.TextMuted;
            vReverb.ForeColor = p.TextMuted;
            vDecay.ForeColor = p.TextMuted;
            vEchoDelay.ForeColor = p.TextMuted;
            vEchoMix.ForeColor = p.TextMuted;
            vDrive.ForeColor = p.TextMuted;

            comboMic.BackColor = p.InputBg;
            comboMic.ForeColor = p.TextPrimary;
            comboOut.BackColor = p.InputBg;
            comboOut.ForeColor = p.TextPrimary;
            comboAi.BackColor = p.InputBg;
            comboAi.ForeColor = p.TextPrimary;

            lblAiTitle.ForeColor = p.Accent;
            toggleAi.SetColors(Color.FromArgb(149, 117, 205), p.PowerOff);

            sliderVol.ApplyPalette(p);
            sPitch.ApplyPalette(p);
            sReverb.ApplyPalette(p);
            sDecay.ApplyPalette(p);
            sEchoDelay.ApplyPalette(p);
            sEchoMix.ApplyPalette(p);
            sDrive.ApplyPalette(p);

            foreach (KeyValuePair<string, VoiceCard> kv in cards)
            {
                kv.Value.ApplyPalette(p);
            }

            bool running = btnPower.Text == "VOICE ON";
            ApplyPowerPalette = delegate(bool on)
            {
                btnPower.SetColors(on ? p.PowerOn : p.PowerOff, ControlPaint.Light(on ? p.PowerOn : p.PowerOff, 0.1f),
                    p.TextPrimary, p.CardBorder);
            };
            ApplyPowerPalette(running);

            SetAiState(aiStateText, aiStateActive);   // restore the AI state color for this palette
        }
    }
}
