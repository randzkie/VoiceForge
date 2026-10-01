using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VoiceForge.AI;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI.Pages
{
    /// <summary>
    /// AI Voices page: engine toggle, RVC model picker (scanned from the
    /// models folders), transpose / chunk-size / gain tuning, and a live
    /// status line. Pure presentation - all decisions are made by MainForm.
    /// </summary>
    public class AiVoicePage : UserControl
    {
        private Label lblSectionEngine;
        private ToggleSwitch toggleAi;
        private Label lblAi;
        private Label lblAiHint;

        private Label lblSectionModel;
        private ComboBox comboModels;
        private PillButton btnRefresh;
        private PillButton btnFolder;
        private Label lblModelHint;

        private Label lblSectionTuning;
        private Label lblTranspose;
        private Label vTranspose;
        private ModernSlider sTranspose;
        private Label lblBlock;
        private ComboBox comboBlock;
        private Label lblGain;
        private Label vGain;
        private ModernSlider sGain;

        private Label lblSectionStatus;
        private Label lblStatus;
        private Label lblGuide;

        public event Action<bool> AiEnabledChanged;
        public event Action<string> AiModelSelected;     // full path
        public event Action<int> AiTransposeChanged;
        public event Action<int> AiBlockChanged;
        public event Action<double> AiGainChanged;

        private bool suppressEvents;
        private List<string> modelPaths = new List<string>();

        public AiVoicePage()
        {
            DoubleBuffered = true;
            BuildUi();
            RefreshModels(null);
        }

        private void BuildUi()
        {
            int x = 24;
            int w = 560;

            // ----- engine -----
            lblSectionEngine = Section(x, 16, "AI VOICE ENGINE");

            toggleAi = new ToggleSwitch();
            toggleAi.SetBounds(x, 48, 52, 26);
            toggleAi.Toggled += delegate
            {
                if (suppressEvents) return;
                Action<bool> handler = AiEnabledChanged;
                if (handler != null) handler(toggleAi.IsOn);
            };

            lblAi = Text(x + 66, 52, 300, "AI voice conversion enabled");
            lblAi.Cursor = Cursors.Hand;
            lblAi.Click += delegate { toggleAi.IsOn = !toggleAi.IsOn; };

            lblAiHint = Text(x, 82, w,
                "Converts your voice with a neural RVC model running locally on the CPU. " +
                "Adds about one chunk of delay while active - DSP presets stay instant.");

            // ----- model -----
            lblSectionModel = Section(x, 134, "VOICE MODEL");

            comboModels = new ComboBox();
            comboModels.DropDownStyle = ComboBoxStyle.DropDownList;
            comboModels.FlatStyle = FlatStyle.Flat;
            comboModels.Font = new Font("Segoe UI", 9f);
            comboModels.SetBounds(x, 166, 300, 26);
            comboModels.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                int idx = comboModels.SelectedIndex;
                if (idx >= 0 && idx < modelPaths.Count)
                {
                    Action<string> handler = AiModelSelected;
                    if (handler != null) handler(modelPaths[idx]);
                }
            };

            btnRefresh = new PillButton();
            btnRefresh.Text = "RESCAN";
            btnRefresh.Filled = false;
            btnRefresh.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnRefresh.SetBounds(x + 312, 166, 110, 26);
            btnRefresh.Click += delegate { RefreshModels(SelectedModelPath); };

            btnFolder = new PillButton();
            btnFolder.Text = "OPEN MODELS FOLDER";
            btnFolder.Filled = false;
            btnFolder.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnFolder.SetBounds(x + 430, 166, 154, 26);
            btnFolder.Click += delegate
            {
                Action handler = ModelsFolderRequested;
                if (handler != null) handler();
            };

            lblModelHint = Text(x, 202, w,
                "Drop RVC v2 .onnx voice models (and hubert.onnx) into %AppData%\\VoiceForge\\models. " +
                "A sidecar .json with tensor names is created automatically next to each voice.");
            lblModelHint.Height = 34;

            // ----- tuning -----
            lblSectionTuning = Section(x, 252, "TUNING");

            lblTranspose = Text(x, 286, 130, "Pitch transpose");
            vTranspose = Text(x + 130, 286, 76, "0 st");
            vTranspose.TextAlign = ContentAlignment.MiddleRight;
            sTranspose = new ModernSlider();
            sTranspose.Minimum = -12;
            sTranspose.Maximum = 12;
            sTranspose.Step = 1;
            sTranspose.Value = 0;
            sTranspose.SetBounds(x, 304, 210, 26);
            sTranspose.ValueChanged += delegate
            {
                int st = (int)Math.Round(sTranspose.Value);
                vTranspose.Text = (st > 0 ? "+" : "") + st + " st";
                if (suppressEvents) return;
                Action<int> handler = AiTransposeChanged;
                if (handler != null) handler(st);
            };

            lblBlock = Text(x + 260, 286, 220, "Chunk size / AI latency");
            comboBlock = new ComboBox();
            comboBlock.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBlock.FlatStyle = FlatStyle.Flat;
            comboBlock.Font = new Font("Segoe UI", 9f);
            comboBlock.Items.Add("250 ms - snappy, needs fast CPU");
            comboBlock.Items.Add("500 ms - balanced (recommended)");
            comboBlock.Items.Add("1000 ms - most stable on slow CPUs");
            comboBlock.SetBounds(x + 260, 304, 220, 26);
            comboBlock.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                int[] map = { 250, 500, 1000 };
                int idx = comboBlock.SelectedIndex;
                if (idx >= 0 && idx < map.Length)
                {
                    Action<int> handler = AiBlockChanged;
                    if (handler != null) handler(map[idx]);
                }
            };

            lblGain = Text(x, 346, 130, "AI output gain");
            vGain = Text(x + 130, 346, 76, "100%");
            vGain.TextAlign = ContentAlignment.MiddleRight;
            sGain = new ModernSlider();
            sGain.Minimum = 0;
            sGain.Maximum = 200;
            sGain.Step = 5;
            sGain.Value = 100;
            sGain.SetBounds(x, 364, 210, 26);
            sGain.ValueChanged += delegate
            {
                int pct = (int)Math.Round(sGain.Value);
                vGain.Text = pct + "%";
                if (suppressEvents) return;
                Action<double> handler = AiGainChanged;
                if (handler != null) handler(pct / 100.0);
            };

            // ----- status -----
            lblSectionStatus = Section(x, 414, "STATUS");
            lblStatus = Text(x, 446, w, "No AI voice loaded.");
            lblStatus.Height = 34;

            lblGuide = Text(x, 492, w,
                "Setup: 1) put voice models in the models folder  2) pick one above  " +
                "3) switch AI on - the engine starts by itself. The dashboard has the same " +
                "AI switch and model picker; fine-tune pitch / latency / gain here anytime.");
            lblGuide.Height = 34;

            Controls.Add(lblSectionEngine);
            Controls.Add(toggleAi);
            Controls.Add(lblAi);
            Controls.Add(lblAiHint);
            Controls.Add(lblSectionModel);
            Controls.Add(comboModels);
            Controls.Add(btnRefresh);
            Controls.Add(btnFolder);
            Controls.Add(lblModelHint);
            Controls.Add(lblSectionTuning);
            Controls.Add(lblTranspose);
            Controls.Add(vTranspose);
            Controls.Add(sTranspose);
            Controls.Add(lblBlock);
            Controls.Add(comboBlock);
            Controls.Add(lblGain);
            Controls.Add(vGain);
            Controls.Add(sGain);
            Controls.Add(lblSectionStatus);
            Controls.Add(lblStatus);
            Controls.Add(lblGuide);
        }

        public event Action ModelsFolderRequested;

        // ---------- helpers ----------

        private static Label Section(int x, int y, string title)
        {
            Label l = new Label();
            l.Text = title;
            l.AutoSize = false;
            l.SetBounds(x, y, 560, 20);
            l.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
            return l;
        }

        private static Label Text(int x, int y, int width, string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.SetBounds(x, y, width, 20);
            l.Font = new Font("Segoe UI", 8.75f);
            return l;
        }

        /// <summary>Rescans the models folders and (re)selects <paramref name="selectedPath"/>.</summary>
        public void RefreshModels(string selectedPath)
        {
            suppressEvents = true;
            modelPaths = AiVoiceStage.ScanModels();
            comboModels.Items.Clear();

            if (modelPaths.Count == 0)
            {
                comboModels.Items.Add("(no models found - open the models folder)");
                comboModels.SelectedIndex = 0;
            }
            else
            {
                foreach (string p in modelPaths)
                {
                    comboModels.Items.Add(Path.GetFileNameWithoutExtension(p));
                }
                int sel = -1;
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    for (int i = 0; i < modelPaths.Count; i++)
                    {
                        if (string.Equals(modelPaths[i], selectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            sel = i;
                            break;
                        }
                    }
                }
                comboModels.SelectedIndex = sel >= 0 ? sel : 0;
            }
            suppressEvents = false;
        }

        public string SelectedModelPath
        {
            get
            {
                int idx = comboModels.SelectedIndex;
                if (idx >= 0 && idx < modelPaths.Count) return modelPaths[idx];
                return null;
            }
        }

        public void SetEnabled(bool on)
        {
            suppressEvents = true;
            toggleAi.IsOn = on;
            suppressEvents = false;
        }

        public void SetTranspose(int semitones)
        {
            suppressEvents = true;
            sTranspose.Value = semitones;
            vTranspose.Text = (semitones > 0 ? "+" : "") + semitones + " st";
            suppressEvents = false;
        }

        public void SetBlockMs(int ms)
        {
            suppressEvents = true;
            comboBlock.SelectedIndex = ms <= 250 ? 0 : (ms <= 500 ? 1 : 2);
            suppressEvents = false;
        }

        public void SetGain(double gain)
        {
            suppressEvents = true;
            int pct = (int)Math.Round(gain * 100.0);
            sGain.Value = pct;
            vGain.Text = pct + "%";
            suppressEvents = false;
        }

        public void SetStatus(string message)
        {
            if (lblStatus.InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetStatus), message); } catch { }
                return;
            }
            lblStatus.Text = message;
        }

        public void ApplyTheme(Palette p)
        {
            BackColor = p.WindowBg;

            Color section = p.Accent;
            lblSectionEngine.ForeColor = section;
            lblSectionModel.ForeColor = section;
            lblSectionTuning.ForeColor = section;
            lblSectionStatus.ForeColor = section;

            lblAi.ForeColor = p.TextPrimary;
            lblAiHint.ForeColor = p.TextMuted;
            lblModelHint.ForeColor = p.TextMuted;
            lblTranspose.ForeColor = p.TextPrimary;
            vTranspose.ForeColor = p.TextMuted;
            lblBlock.ForeColor = p.TextPrimary;
            lblGain.ForeColor = p.TextPrimary;
            vGain.ForeColor = p.TextMuted;
            lblStatus.ForeColor = p.TextPrimary;
            lblGuide.ForeColor = p.TextMuted;

            comboModels.BackColor = p.InputBg;
            comboModels.ForeColor = p.TextPrimary;
            comboBlock.BackColor = p.InputBg;
            comboBlock.ForeColor = p.TextPrimary;

            toggleAi.SetColors(p.Accent, p.PowerOff);
            sTranspose.ApplyPalette(p);
            sGain.ApplyPalette(p);

            btnRefresh.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);
            btnFolder.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);

            Invalidate();
        }
    }
}
