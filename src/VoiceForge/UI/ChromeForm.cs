using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceForge.Services;
using VoiceForge.UI.Controls;

namespace VoiceForge.UI
{
    /// <summary>
    /// Borderless form with a Voicemod-style custom title bar: native drag via
    /// HTCAPTION, edge resizing via invisible strips, custom min/max/close glyphs,
    /// and a drop shadow. All UI work happens inside ContentRoot.
    /// </summary>
    public class ChromeForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int WM_NCHITTEST = 0x84;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int Grip = 8;

        protected Panel TitleBar;
        protected Panel ContentRoot;

        private readonly ChromeCaptionButton btnMin;
        private readonly ChromeCaptionButton btnMax;
        private readonly ChromeCaptionButton btnClose;
        private readonly ResizeStrip stripTop;
        private readonly ResizeStrip stripBottom;
        private readonly ResizeStrip stripLeft;
        private readonly ResizeStrip stripRight;

        private Palette palette = ThemeService.Dark();

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public ChromeForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = palette.WindowBg;
            Font = new Font("Segoe UI", 9.5f);

            ContentRoot = new Panel();
            ContentRoot.Dock = DockStyle.Fill;
            ContentRoot.BackColor = palette.WindowBg;

            TitleBar = new TitleBarPanel(this);
            TitleBar.Dock = DockStyle.Top;
            TitleBar.Height = 46;
            TitleBar.BackColor = palette.TitleBarBg;

            btnClose = new ChromeCaptionButton(ChromeCaptionButton.GlyphKind.Close);
            btnMax = new ChromeCaptionButton(ChromeCaptionButton.GlyphKind.Maximize);
            btnMin = new ChromeCaptionButton(ChromeCaptionButton.GlyphKind.Minimize);
            btnClose.Click += delegate { Close(); };
            btnMax.Click += delegate { ToggleMaximize(); };
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            TitleBar.Controls.Add(btnClose);
            TitleBar.Controls.Add(btnMax);
            TitleBar.Controls.Add(btnMin);

            stripBottom = new ResizeStrip(HTBOTTOM, Cursors.SizeNS);
            stripLeft = new ResizeStrip(HTLEFT, Cursors.SizeWE);
            stripRight = new ResizeStrip(HTRIGHT, Cursors.SizeWE);
            stripTop = new ResizeStrip(HTTOP, Cursors.SizeNS);
            stripBottom.Dock = DockStyle.Bottom;
            stripLeft.Dock = DockStyle.Left;
            stripRight.Dock = DockStyle.Right;
            stripTop.Dock = DockStyle.Top;

            Controls.Add(ContentRoot);
            Controls.Add(TitleBar);
            Controls.Add(stripBottom);
            Controls.Add(stripLeft);
            Controls.Add(stripRight);
            Controls.Add(stripTop);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        /// <summary>Applies the palette to the window chrome (children theme themselves).</summary>
        public void ApplyChromeTheme(Palette p)
        {
            palette = p;
            BackColor = p.WindowBg;
            ContentRoot.BackColor = p.WindowBg;
            TitleBar.BackColor = p.TitleBarBg;
            TitleBar.Invalidate();
            stripTop.BackColor = p.WindowBg;
            stripBottom.BackColor = p.WindowBg;
            stripLeft.BackColor = p.WindowBg;
            stripRight.BackColor = p.WindowBg;
            btnMin.SetColors(p.TextMuted, p.NavHover);
            btnMax.SetColors(p.TextMuted, p.NavHover);
            btnClose.SetColors(p.TextMuted, p.Danger);
        }

        protected void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
            }
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            btnMax.Invalidate();
        }

        /// <summary>Returns a WM_NCHITTEST edge code for screen point encoded in lParam, or 0.</summary>
        internal int EdgeHitTest(IntPtr lParam)
        {
            if (WindowState == FormWindowState.Maximized) return 0;
            int l = lParam.ToInt32();
            Point screen = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
            Point client = PointToClient(screen);

            bool left = client.X <= Grip;
            bool right = client.X >= Width - Grip;
            bool top = client.Y <= Grip;
            bool bottom = client.Y >= Height - Grip;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                int edge = EdgeHitTest(m.LParam);
                if (edge != 0)
                {
                    m.Result = (IntPtr)edge;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>Title bar: native drag (HTCAPTION) + app title painted directly on it.</summary>
        private sealed class TitleBarPanel : Panel
        {
            private readonly ChromeForm owner;

            public TitleBarPanel(ChromeForm ownerForm)
            {
                owner = ownerForm;
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Accent dot + product name.
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(0, 229, 192)))
                {
                    g.FillEllipse(dot, 16, 17, 12, 12);
                }
                TextRenderer.DrawText(g, "VOICEFORGE", new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    new Rectangle(36, 0, 220, Height), owner.palette.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "Voice Changer & Soundboard", new Font("Segoe UI", 8f),
                    new Rectangle(178, 0, 260, Height), owner.palette.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST)
                {
                    int edge = owner.EdgeHitTest(m.LParam);
                    if (edge != 0)
                    {
                        m.Result = (IntPtr)edge;
                        return;
                    }
                    m.Result = (IntPtr)2; // HTCAPTION - native drag + double-click maximize
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private sealed class ChromeCaptionButton : Control
        {
            public enum GlyphKind { Minimize, Maximize, Close }

            private readonly GlyphKind kind;
            private bool hovered;
            private Color glyphColor = Color.FromArgb(139, 144, 176);
            private Color hoverBg = Color.FromArgb(30, 32, 68);

            public ChromeCaptionButton(GlyphKind glyphKind)
            {
                kind = glyphKind;
                Dock = DockStyle.Right;
                Size = new Size(42, 46);
                Cursor = Cursors.Hand;
                Ui.EnableDoubleBuffering(this);
            }

            public void SetColors(Color glyph, Color hover)
            {
                glyphColor = glyph;
                hoverBg = hover;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (hovered)
                {
                    using (SolidBrush b = new SolidBrush(hoverBg))
                    {
                        g.FillRectangle(b, ClientRectangle);
                    }
                }

                using (Pen pen = new Pen(hovered ? Color.White : glyphColor, 1.5f))
                {
                    switch (kind)
                    {
                        case GlyphKind.Minimize:
                            g.DrawLine(pen, 14, Height / 2 + 4, Width - 14, Height / 2 + 4);
                            break;
                        case GlyphKind.Maximize:
                            g.DrawRectangle(pen, 14, 14, Width - 29, Height - 29);
                            break;
                        default: // Close
                            g.DrawLine(pen, 15, 16, Width - 15, Height - 16);
                            g.DrawLine(pen, Width - 15, 16, 15, Height - 16);
                            break;
                    }
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

        private sealed class ResizeStrip : Control
        {
            private readonly int hitCode;

            public ResizeStrip(int hitTestCode, Cursor cursor)
            {
                hitCode = hitTestCode;
                Cursor = cursor;
                BackColor = Color.White;
                if (hitTestCode == HTLEFT || hitTestCode == HTRIGHT)
                {
                    Width = Grip;
                }
                else
                {
                    Height = Grip;
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left)
                {
                    Form form = FindForm();
                    if (form != null)
                    {
                        ReleaseCapture();
                        SendMessage(form.Handle, WM_NCLBUTTONDOWN, hitCode, 0);
                    }
                }
            }
        }
    }
}
