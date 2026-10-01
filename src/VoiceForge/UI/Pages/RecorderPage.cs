using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI.Pages
{
    /// <summary>
    /// Recorder page: captures the processed voice (post-effects) to WAV via the
    /// engine's recording tap, lists past recordings and plays them back.
    /// </summary>
    public class RecorderPage : UserControl
    {
        private Panel topBar;
        private Panel bottomBar;
        private ListView listView;
        private PillButton btnRecord;
        private PillButton btnPlay;
        private PillButton btnFolder;
        private PillButton btnDelete;
        private Label lblElapsed;
        private Label lblHint;

        private readonly Timer elapsedTimer;
        private DateTime startTime;
        private bool recording;

        public Func<bool> RecordStartRequested;
        public Action RecordStopRequested;
        public event Action<string> PlayRequested;
        public event Action<string> InfoMessage;

        public RecorderPage()
        {
            DoubleBuffered = true;
            elapsedTimer = new Timer();
            elapsedTimer.Interval = 250;
            elapsedTimer.Tick += delegate
            {
                TimeSpan t = DateTime.UtcNow - startTime;
                lblElapsed.Text = string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
            };
            BuildUi();
            RefreshRecordings();
        }

        private void BuildUi()
        {
            topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 76;

            btnRecord = new PillButton();
            btnRecord.Text = "START RECORDING";
            btnRecord.Size = new Size(190, 42);
            btnRecord.Location = new Point(12, 14);
            btnRecord.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnRecord.Enabled = false;
            btnRecord.Click += OnRecordClicked;

            lblElapsed = new Label();
            lblElapsed.Text = "00:00";
            lblElapsed.AutoSize = false;
            lblElapsed.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            lblElapsed.TextAlign = ContentAlignment.MiddleLeft;
            lblElapsed.SetBounds(220, 18, 110, 36);

            lblHint = new Label();
            lblHint.Text = "Records exactly what comes out of the voice engine (with effects).";
            lblHint.AutoSize = false;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;
            lblHint.Font = new Font("Segoe UI", 8.5f);
            lblHint.SetBounds(350, 28, 430, 22);

            topBar.Controls.Add(btnRecord);
            topBar.Controls.Add(lblElapsed);
            topBar.Controls.Add(lblHint);

            bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 64;

            btnPlay = new PillButton();
            btnPlay.Text = "PLAY";
            btnPlay.Filled = false;
            btnPlay.Size = new Size(110, 40);
            btnPlay.Location = new Point(12, 12);
            btnPlay.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnPlay.Click += delegate
            {
                string path = SelectedPath();
                if (path != null)
                {
                    Action<string> handler = PlayRequested;
                    if (handler != null) handler(path);
                }
            };

            btnDelete = new PillButton();
            btnDelete.Text = "DELETE";
            btnDelete.Filled = false;
            btnDelete.Size = new Size(110, 40);
            btnDelete.Location = new Point(134, 12);
            btnDelete.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnDelete.Click += OnDeleteClicked;

            btnFolder = new PillButton();
            btnFolder.Text = "OPEN FOLDER";
            btnFolder.Filled = false;
            btnFolder.Size = new Size(150, 40);
            btnFolder.Location = new Point(256, 12);
            btnFolder.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnFolder.Click += delegate
            {
                try
                {
                    Process.Start(SettingsService.RecordingsFolder);
                }
                catch (Exception ex)
                {
                    Notify("Could not open folder: " + ex.Message);
                }
            };

            bottomBar.Controls.Add(btnPlay);
            bottomBar.Controls.Add(btnDelete);
            bottomBar.Controls.Add(btnFolder);

            listView = new ListView();
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.MultiSelect = false;
            listView.HideSelection = false;
            listView.Dock = DockStyle.Fill;
            listView.Font = new Font("Segoe UI", 9f);
            listView.Columns.Add("Recording", 380);
            listView.Columns.Add("Modified", 150);
            listView.Columns.Add("Size", 100);

            Controls.Add(listView);
            Controls.Add(bottomBar);
            Controls.Add(topBar);
        }

        private void OnRecordClicked(object sender, EventArgs e)
        {
            if (!recording)
            {
                Func<bool> handler = RecordStartRequested;
                bool ok = handler != null && handler();
                if (ok)
                {
                    recording = true;
                    startTime = DateTime.UtcNow;
                    btnRecord.Text = "STOP RECORDING";
                    lblElapsed.Text = "00:00";
                    elapsedTimer.Start();
                }
            }
            else
            {
                recording = false;
                elapsedTimer.Stop();
                Action handler = RecordStopRequested;
                if (handler != null) handler();
                btnRecord.Text = "START RECORDING";
                RefreshRecordings();
            }
        }

        private void OnDeleteClicked(object sender, EventArgs e)
        {
            string path = SelectedPath();
            if (path == null) return;
            DialogResult dr = MessageBox.Show(this, "Delete \"" + Path.GetFileName(path) + "\"?",
                "VoiceForge", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                try { File.Delete(path); } catch (Exception ex) { Notify("Delete failed: " + ex.Message); }
                RefreshRecordings();
            }
        }

        private string SelectedPath()
        {
            if (listView.SelectedItems.Count == 0)
            {
                Notify("Select a recording first.");
                return null;
            }
            return listView.SelectedItems[0].Tag as string;
        }

        private void Notify(string message)
        {
            Action<string> handler = InfoMessage;
            if (handler != null) handler(message);
        }

        public void RefreshRecordings()
        {
            listView.BeginUpdate();
            try
            {
                listView.Items.Clear();
                string folder = SettingsService.RecordingsFolder;
                try
                {
                    DirectoryInfo di = new DirectoryInfo(folder);
                    if (di.Exists)
                    {
                        FileInfo[] files = di.GetFiles("*.wav");
                        Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                        for (int i = 0; i < files.Length; i++)
                        {
                            FileInfo fi = files[i];
                            ListViewItem item = new ListViewItem(fi.Name);
                            item.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                            item.SubItems.Add((fi.Length / 1024.0).ToString("0") + " KB");
                            item.Tag = fi.FullName;
                            listView.Items.Add(item);
                        }
                    }
                }
                catch { }
            }
            finally
            {
                listView.EndUpdate();
            }
        }

        /// <summary>MainForm tells the page whether the engine (and thus recording) is available.</summary>
        public void SetEngineState(bool running)
        {
            btnRecord.Enabled = running;
            if (!running && recording)
            {
                recording = false;
                elapsedTimer.Stop();
                btnRecord.Text = "START RECORDING";
                RefreshRecordings();
            }
        }

        public void ApplyTheme(Palette p)
        {
            BackColor = p.WindowBg;
            topBar.BackColor = p.WindowBg;
            bottomBar.BackColor = p.WindowBg;
            lblElapsed.ForeColor = p.TextPrimary;
            lblHint.ForeColor = p.TextMuted;

            listView.BackColor = p.CardBg;
            listView.ForeColor = p.TextPrimary;
            btnRecord.SetColors(p.Danger, ControlPaint.Light(p.Danger, 0.1f), Color.White, p.CardBorder);
            btnPlay.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);
            btnDelete.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);
            btnFolder.SetColors(p.Accent, ControlPaint.Light(p.Accent, 0.1f), p.TextPrimary, p.CardBorder);
        }
    }
}
