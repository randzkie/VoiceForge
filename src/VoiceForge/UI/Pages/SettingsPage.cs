using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI.Pages
{
    /// <summary>
    /// Settings page: theme, latency, global hotkeys, VB-Cable setup guide,
    /// settings reset and about info.
    /// </summary>
    public class SettingsPage : UserControl
    {
        private Label lblSectionAppearance;
        private Label lblTheme;
        private ToggleSwitch toggleTheme;
        private Label lblSectionAudio;
        private Label lblLatency;
        private ComboBox comboLatency;
        private Label lblLatencyHint;
        private Label lblSectionHotkeys;
        private Label lblHotkeys;
        private ToggleSwitch toggleHotkeys;
        private Label lblHotkeysHint;
        private Label lblSectionRouting;
        private LinkLabel linkVbCable;
        private Label lblRoutingInfo;
        private PillButton btnReset;
        private Label lblAbout;

        public event Action<bool> ThemeToggled;
        public event Action<int> LatencyChanged;
        public event Action<bool> HotkeysToggled;
        public event Action ResetRequested;

        private bool suppressEvents;

        public SettingsPage()
        {
            DoubleBuffered = true;
            BuildUi();
        }

        private void BuildUi()
        {
            int x = 24;
            int w = 560;

            lblSectionAppearance = Section(x, 16, "APPEARANCE");

            toggleTheme = new ToggleSwitch();
            toggleTheme.SetBounds(x, 48, 52, 26);
            toggleTheme.Toggled += delegate
            {
                if (suppressEvents) return;
                Action<bool> handler = ThemeToggled;
                if (handler != null) handler(toggleTheme.IsOn);
            };

            lblTheme = Text(x + 66, 52, 300, "Light theme");
            lblTheme.Cursor = Cursors.Hand;
            lblTheme.Click += delegate { toggleTheme.IsOn = !toggleTheme.IsOn; };

            lblSectionAudio = Section(x, 100, "AUDIO ENGINE");

            lblLatency = Text(x, 132, 200, "Latency / responsiveness");
            comboLatency = new ComboBox();
            comboLatency.DropDownStyle = ComboBoxStyle.DropDownList;
            comboLatency.FlatStyle = FlatStyle.Flat;
            comboLatency.Font = new Font("Segoe UI", 9f);
            comboLatency.Items.Add("Low  (40 ms) - fast PC / wired mic");
            comboLatency.Items.Add("Balanced  (70 ms) - recommended");
            comboLatency.Items.Add("Safe  (120 ms) - older hardware");
            comboLatency.SetBounds(x + 210, 130, 250, 26);
            comboLatency.SelectedIndexChanged += delegate
            {
                if (suppressEvents) return;
                int[] map = { 40, 70, 120 };
                int idx = comboLatency.SelectedIndex;
                if (idx >= 0 && idx < map.Length)
                {
                    Action<int> handler = LatencyChanged;
                    if (handler != null) handler(map[idx]);
                }
            };

            lblLatencyHint = Text(x, 162, w, "Lower latency = less delay between your mouth and the output, but needs more CPU headroom.");

            lblSectionHotkeys = Section(x, 200, "GLOBAL HOTKEYS");

            toggleHotkeys = new ToggleSwitch();
            toggleHotkeys.SetBounds(x, 236, 52, 26);
            toggleHotkeys.Toggled += delegate
            {
                if (suppressEvents) return;
                Action<bool> handler = HotkeysToggled;
                if (handler != null) handler(toggleHotkeys.IsOn);
            };

            lblHotkeys = Text(x + 66, 240, 300, "Soundboard hotkeys enabled");
            lblHotkeysHint = Text(x, 268, w, "While VoiceForge runs anywhere, Ctrl+Alt+1 ... Ctrl+Alt+9 fire the first nine soundboard slots.");

            lblSectionRouting = Section(x, 316, "USE AS A MICROPHONE (VB-CABLE)");

            linkVbCable = new LinkLabel();
            linkVbCable.Text = "1) Download and install VB-CABLE (free) - opens vb-audio.com";
            linkVbCable.LinkColor = Color.FromArgb(0, 179, 155);
            linkVbCable.ActiveLinkColor = Color.FromArgb(255, 77, 141);
            linkVbCable.LinkBehavior = LinkBehavior.HoverUnderline;
            linkVbCable.AutoSize = false;
            linkVbCable.SetBounds(x, 352, w, 22);
            linkVbCable.LinkClicked += delegate
            {
                try
                {
                    Process.Start("https://www.vb-audio.com/Cable/");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not open the browser: " + ex.Message, "VoiceForge");
                }
            };

            lblRoutingInfo = Text(x, 380, w, "2) In VoiceForge set Output = \"CABLE Input\".\n" +
                "3) In Discord / game / OBS set the microphone to \"CABLE Output\".\n" +
                "Your processed voice then appears as a normal microphone everywhere.");
            lblRoutingInfo.Height = 70;

            btnReset = new PillButton();
            btnReset.Text = "RESET SETTINGS";
            btnReset.Filled = false;
            btnReset.Size = new Size(160, 40);
            btnReset.SetBounds(x, 470, 160, 40);
            btnReset.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnReset.Click += delegate
            {
                Action handler = ResetRequested;
                if (handler != null) handler();
            };

            lblAbout = Text(x, 530, w, "VoiceForge 1.0 - a Voicemod-style voice changer built with .NET Framework 4.7, WinForms and NAudio.\n" +
                "Settings are stored in %AppData%\\VoiceForge\\settings.json - recordings in %AppData%\\VoiceForge\\Recordings.");
            lblAbout.Height = 44;

            Controls.Add(lblSectionAppearance);
            Controls.Add(toggleTheme);
            Controls.Add(lblTheme);
            Controls.Add(lblSectionAudio);
            Controls.Add(lblLatency);
            Controls.Add(comboLatency);
            Controls.Add(lblLatencyHint);
            Controls.Add(lblSectionHotkeys);
            Controls.Add(toggleHotkeys);
            Controls.Add(lblHotkeys);
            Controls.Add(lblHotkeysHint);
            Controls.Add(lblSectionRouting);
            Controls.Add(linkVbCable);
            Controls.Add(lblRoutingInfo);
            Controls.Add(btnReset);
            Controls.Add(lblAbout);
        }

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

        public void SetValues(bool lightTheme, int latencyMs, bool hotkeysEnabled)
        {
            suppressEvents = true;
            toggleTheme.IsOn = lightTheme;
            toggleHotkeys.IsOn = hotkeysEnabled;
            comboLatency.SelectedIndex = latencyMs <= 40 ? 0 : (latencyMs <= 70 ? 1 : 2);
            suppressEvents = false;
        }

        public void ApplyTheme(Palette p)
        {
            BackColor = p.WindowBg;

            Color section = p.Accent;
            lblSectionAppearance.ForeColor = section;
            lblSectionAudio.ForeColor = section;
            lblSectionHotkeys.ForeColor = section;
            lblSectionRouting.ForeColor = section;

            lblTheme.ForeColor = p.TextPrimary;
            lblLatency.ForeColor = p.TextPrimary;
            lblLatencyHint.ForeColor = p.TextMuted;
            lblHotkeys.ForeColor = p.TextPrimary;
            lblHotkeysHint.ForeColor = p.TextMuted;
            lblRoutingInfo.ForeColor = p.TextMuted;
            lblAbout.ForeColor = p.TextMuted;

            comboLatency.BackColor = p.InputBg;
            comboLatency.ForeColor = p.TextPrimary;

            toggleTheme.SetColors(p.Accent, p.PowerOff);
            toggleHotkeys.SetColors(p.Accent, p.PowerOff);

            btnReset.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);

            Invalidate();
        }
    }
}
