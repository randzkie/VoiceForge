using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VoiceForge.Models;
using VoiceForge.Services;

namespace VoiceForge.UI.Controls
{
    /// <summary>
    /// A selectable voice preset card in the Voicemod style: rounded panel,
    /// colored badge, name + description, accent glow when selected.
    /// </summary>
    public class VoiceCard : Control
    {
        private bool hovered;
        private bool selected;
        private VoicePreset preset;
        private Color badgeColor = Color.FromArgb(0, 229, 192);

        private Color cardBg = Color.FromArgb(30, 32, 68);
        private Color cardHover = Color.FromArgb(39, 42, 85);
        private Color cardBorder = Color.FromArgb(46, 49, 88);
        private Color textPrimary = Color.FromArgb(236, 238, 248);
        private Color textMuted = Color.FromArgb(139, 144, 176);
        private Color accent = Color.FromArgb(0, 229, 192);

        public event EventHandler<VoicePreset> VoiceSelected;

        public VoiceCard()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Size = new Size(216, 108);
        }

        public VoicePreset Preset
        {
            get { return preset; }
        }

        public void Bind(VoicePreset voicePreset)
        {
            preset = voicePreset;
            Text = voicePreset.Name;
            badgeColor = VoiceCatalog.BadgeColor(voicePreset.Id);
            Invalidate();
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                selected = value;
                Invalidate();
            }
        }

        public void ApplyPalette(Palette p)
        {
            cardBg = p.CardBg;
            cardHover = p.CardHover;
            cardBorder = p.CardBorder;
            textPrimary = p.TextPrimary;
            textMuted = p.TextMuted;
            accent = p.Accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (preset == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);

            // Soft glow when selected.
            if (selected)
            {
                Rectangle glow = new Rectangle(-4, -4, Width + 8, Height + 8);
                using (GraphicsPath gp = Ui.RoundedRect(glow, 18))
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(36, accent)))
                {
                    g.FillPath(gb, gp);
                }
            }

            using (GraphicsPath path = Ui.RoundedRect(r, 14))
            {
                using (SolidBrush bg = new SolidBrush(hovered && !selected ? cardHover : cardBg))
                {
                    g.FillPath(bg, path);
                }
                using (Pen pen = new Pen(selected ? accent : cardBorder, selected ? 2f : 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Badge circle with the preset glyph.
            Rectangle badge = new Rectangle(18, Height / 2 - 19, 38, 38);
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(44, badgeColor)))
            {
                g.FillEllipse(bb, badge);
            }
            if (selected)
            {
                using (Pen bp = new Pen(badgeColor, 1.6f))
                {
                    g.DrawEllipse(bp, badge);
                }
            }
            TextRenderer.DrawText(g, preset.Badge, new Font("Segoe UI", 11f, FontStyle.Bold),
                badge, badgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Texts.
            Rectangle nameRect = new Rectangle(68, 24, Width - 80, 26);
            TextRenderer.DrawText(g, preset.Name, new Font("Segoe UI", 10.5f, FontStyle.Bold),
                nameRect, textPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle descRect = new Rectangle(68, 52, Width - 80, 34);
            TextRenderer.DrawText(g, preset.Description, new Font("Segoe UI", 8.25f),
                descRect, textMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                EventHandler<VoicePreset> handler = VoiceSelected;
                if (handler != null) handler(this, preset);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }
    }

    /// <summary>
    /// One soundboard slot: shows the file name and its global hotkey.
    /// Left click plays, right click removes.
    /// </summary>
    public class SoundButton : Control
    {
        private bool hovered;
        private string filePath = "";
        private string hotkeyText = "";
        private int slotIndex;

        private Color cardBg = Color.FromArgb(30, 32, 68);
        private Color cardHover = Color.FromArgb(39, 42, 85);
        private Color cardBorder = Color.FromArgb(46, 49, 88);
        private Color textPrimary = Color.FromArgb(236, 238, 248);
        private Color textMuted = Color.FromArgb(139, 144, 176);
        private Color accent = Color.FromArgb(0, 229, 192);

        public event Action<string> PlayRequested;
        public event Action<string> RemoveRequested;

        public SoundButton()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Size = new Size(158, 84);
        }

        public string FilePath
        {
            get { return filePath; }
        }

        public int SlotIndex
        {
            get { return slotIndex; }
        }

        public void Bind(string path, int slot, bool hasHotkey)
        {
            filePath = path;
            slotIndex = slot;
            hotkeyText = hasHotkey ? "Ctrl+Alt+" + slot : "";
            Invalidate();
        }

        public void ApplyPalette(Palette p)
        {
            cardBg = p.CardBg;
            cardHover = p.CardHover;
            cardBorder = p.CardBorder;
            textPrimary = p.TextPrimary;
            textMuted = p.TextMuted;
            accent = p.Accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Ui.RoundedRect(r, 12))
            {
                using (SolidBrush bg = new SolidBrush(hovered ? cardHover : cardBg))
                {
                    g.FillPath(bg, path);
                }
                using (Pen pen = new Pen(hovered ? accent : cardBorder, 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Speaker glyph (drawn, safe on every font).
            using (Pen gp = new Pen(accent, 1.8f))
            {
                int cx = 26, cy = Height / 2 - 8;
                g.DrawLine(gp, cx, cy + 6, cx + 5, cy + 6);
                g.DrawLine(gp, cx, cy + 6, cx, cy + 14);
                g.DrawLine(gp, cx, cy + 14, cx + 5, cy + 14);
                g.DrawLine(gp, cx + 5, cy + 6, cx + 11, cy);
                g.DrawLine(gp, cx + 11, cy, cx + 11, cy + 20);
                g.DrawLine(gp, cx + 11, cy + 20, cx + 5, cy + 14);
                g.DrawArc(gp, cx + 13, cy + 5, 8, 10, -60, 120);
            }

            string name = System.IO.Path.GetFileNameWithoutExtension(filePath);
            Rectangle nameRect = new Rectangle(46, 14, Width - 56, 36);
            TextRenderer.DrawText(g, name, new Font("Segoe UI", 8.75f, FontStyle.Bold),
                nameRect, textPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            if (hotkeyText.Length > 0)
            {
                Rectangle hkRect = new Rectangle(14, Height - 24, Width - 24, 18);
                TextRenderer.DrawText(g, hotkeyText, new Font("Segoe UI", 7.5f),
                    hkRect, textMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                Action<string> play = PlayRequested;
                if (play != null) play(filePath);
            }
            else if (e.Button == MouseButtons.Right)
            {
                Action<string> remove = RemoveRequested;
                if (remove != null) remove(filePath);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }
    }
}
