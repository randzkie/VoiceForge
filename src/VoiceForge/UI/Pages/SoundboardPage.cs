using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI.Pages
{
    /// <summary>
    /// Soundboard page: add WAV/MP3 files, click (or Ctrl+Alt+1..9) to fire them.
    /// Right-click a slot to remove it.
    /// </summary>
    public class SoundboardPage : UserControl
    {
        private Panel topBar;
        private FlowLayoutPanel flow;
        private PillButton btnLoad;
        private PillButton btnClear;
        private Label lblHint;
        private Label lblStatus;

        private List<string> files = new List<string>();

        public event Action<List<string>> FilesChanged;
        public event Action<string> PlayRequested;
        public event Action<string> PlaybackMessage;

        public SoundboardPage()
        {
            DoubleBuffered = true;
            BuildUi();
        }

        private void BuildUi()
        {
            topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 64;

            btnLoad = new PillButton();
            btnLoad.Text = "ADD SOUNDS";
            btnLoad.Size = new Size(140, 40);
            btnLoad.Location = new Point(12, 12);
            btnLoad.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnLoad.Click += delegate { LoadSounds(); };

            btnClear = new PillButton();
            btnClear.Text = "REMOVE ALL";
            btnClear.Filled = false;
            btnClear.Size = new Size(130, 40);
            btnClear.Location = new Point(164, 12);
            btnClear.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnClear.Click += delegate
            {
                files.Clear();
                Rebuild();
                RaiseFilesChanged();
            };

            lblHint = new Label();
            lblHint.Text = "Left click plays  |  Right click removes  |  Ctrl+Alt+1...9 fire the first 9 slots globally";
            lblHint.AutoSize = false;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;
            lblHint.Font = new Font("Segoe UI", 8.25f);
            lblHint.SetBounds(310, 22, 420, 20);

            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.AutoSize = false;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Font = new Font("Segoe UI", 8f);
            lblStatus.SetBounds(310, 42, 420, 16);

            topBar.Controls.Add(btnLoad);
            topBar.Controls.Add(btnClear);
            topBar.Controls.Add(lblHint);
            topBar.Controls.Add(lblStatus);

            flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.Padding = new Padding(4);

            Controls.Add(flow);
            Controls.Add(topBar);
        }

        public void SetFiles(List<string> list)
        {
            files = new List<string>();
            if (list != null) files.AddRange(list);
            Rebuild();
        }

        private void LoadSounds()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Add sounds to the soundboard";
                dlg.Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < dlg.FileNames.Length; i++)
                    {
                        if (!files.Contains(dlg.FileNames[i]))
                        {
                            files.Add(dlg.FileNames[i]);
                        }
                    }
                    Rebuild();
                    RaiseFilesChanged();
                }
            }
        }

        private void Rebuild()
        {
            flow.SuspendLayout();
            flow.Controls.Clear();
            for (int i = 0; i < files.Count; i++)
            {
                SoundButton b = new SoundButton();
                b.Bind(files[i], i + 1, i < 9);
                b.Margin = new Padding(8);
                string path = files[i];
                b.PlayRequested += delegate(string p)
                {
                    Action<string> handler = PlayRequested;
                    if (handler != null) handler(p);
                };
                b.RemoveRequested += delegate(string p)
                {
                    files.Remove(p);
                    Rebuild();
                    RaiseFilesChanged();
                };
                flow.Controls.Add(b);
            }
            if (files.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "No sounds yet - click ADD SOUNDS to load WAV or MP3 files.";
                empty.AutoSize = false;
                empty.Size = new Size(420, 60);
                empty.Font = new Font("Segoe UI", 10f);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                flow.Controls.Add(empty);
            }
            flow.ResumeLayout();
        }

        private void RaiseFilesChanged()
        {
            Action<List<string>> handler = FilesChanged;
            if (handler != null) handler(new List<string>(files));
        }

        public void ShowMessage(string message)
        {
            lblStatus.Text = message;
        }

        public void ApplyTheme(Palette p)
        {
            BackColor = p.WindowBg;
            topBar.BackColor = p.WindowBg;
            flow.BackColor = p.WindowBg;
            lblHint.ForeColor = p.TextMuted;
            lblStatus.ForeColor = p.TextMuted;
            btnLoad.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), Color.FromArgb(12, 14, 32), p.CardBorder);
            btnClear.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);
        }
    }
}
