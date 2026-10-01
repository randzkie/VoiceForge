using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using VoiceForge.Services;

namespace VoiceForge.UI.Controls
{
    /// <summary>Shared GDI+ helpers for the custom controls.</summary>
    internal static class Ui
    {
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (radius <= 0 || r.Width < d || r.Height < d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Enables flicker-free painting on any control.
        /// Control.SetStyle / Control.UpdateStyles are protected members, so a static
        /// helper must invoke them via reflection. This is the standard workaround.
        /// </summary>
        public static void EnableDoubleBuffering(Control c)
        {
            if (c == null || c.IsDisposed) return;

            const ControlStyles flags =
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw;

            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;

            typeof(Control)
                .GetMethod("SetStyle", hidden, null, new[] { typeof(ControlStyles), typeof(bool) }, null)
                ?.Invoke(c, new object[] { flags, true });

            typeof(Control)
                .GetMethod("UpdateStyles", hidden)
                ?.Invoke(c, null);
        }

        public static string Ellipsize(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (TextRenderer.MeasureText(g ?? Graphics.FromHwnd(IntPtr.Zero), text, font).Width <= maxWidth) return text;
            while (text.Length > 1 && TextRenderer.MeasureText(text + "...", font).Width > maxWidth)
            {
                text = text.Substring(0, text.Length - 1);
            }
            return text + "...";
        }
    }

    /// <summary>Horizontal slider with a modern pill track and round thumb.</summary>
    public class ModernSlider : Control
    {
        private double minimum = 0.0;
        private double maximum = 100.0;
        private double value;
        private double step = 1.0;
        private bool dragging;
        private bool hovered;

        private Color trackColor = Color.FromArgb(60, 63, 100);
        private Color fillColor = Color.FromArgb(0, 229, 192);
        private Color thumbColor = Color.White;

        public event EventHandler ValueChanged;

        public ModernSlider()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Height = 26;
        }

        public double Minimum
        {
            get { return minimum; }
            set
            {
                minimum = value;
                if (Value < minimum) Value = minimum;
                Invalidate();
            }
        }

        public double Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value;
                if (Value > maximum) Value = maximum;
                Invalidate();
            }
        }

        public double Step
        {
            get { return step; }
            set { step = value > 0 ? value : 1.0; }
        }

        public double Value
        {
            get { return this.value; }
            set
            {
                double v = value;
                if (step > 0)
                {
                    v = Math.Round((v - minimum) / step) * step + minimum;
                }
                if (v < minimum) v = minimum;
                if (v > maximum) v = maximum;
                if (Math.Abs(v - this.value) > 1e-9)
                {
                    this.value = v;
                    Invalidate();
                    EventHandler handler = ValueChanged;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        public void SetColors(Color track, Color fill)
        {
            trackColor = track;
            fillColor = fill;
            Invalidate();
        }

        public void ApplyPalette(Palette p)
        {
            trackColor = p.InputBorder;
            fillColor = p.Accent;
            Invalidate();
        }

        private int ThumbRadius
        {
            get { return 9; }
        }

        private double Fraction
        {
            get
            {
                double span = maximum - minimum;
                if (span <= 0) return 0;
                return (this.value - minimum) / span;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int tr = ThumbRadius;
            int trackY = Height / 2 - 2;
            Rectangle track = new Rectangle(tr, trackY, Width - tr * 2, 4);

            using (GraphicsPath tp = Ui.RoundedRect(track, 2))
            {
                using (SolidBrush tb = new SolidBrush(trackColor))
                {
                    g.FillPath(tb, tp);
                }
            }

            int fillW = (int)Math.Round((Width - tr * 2) * Fraction);
            if (fillW > 0)
            {
                Rectangle fillRect = new Rectangle(tr, trackY, fillW, 4);
                using (GraphicsPath fp = Ui.RoundedRect(fillRect, 2))
                {
                    using (SolidBrush fb = new SolidBrush(fillColor))
                    {
                        g.FillPath(fb, fp);
                    }
                }
            }

            int cx = tr + (int)Math.Round((Width - tr * 2) * Fraction);
            int cy = Height / 2;
            Rectangle thumb = new Rectangle(cx - tr, cy - tr, tr * 2, tr * 2);
            using (SolidBrush tb2 = new SolidBrush(hovered || dragging ? fillColor : thumbColor))
            {
                g.FillEllipse(tb2, thumb);
            }
            using (Pen outline = new Pen(Color.FromArgb(60, 0, 0, 0)))
            {
                g.DrawEllipse(outline, thumb);
            }
        }

        private void UpdateFromMouse(int x)
        {
            int tr = ThumbRadius;
            int span = Width - tr * 2;
            if (span <= 0) return;
            int rel = x - tr;
            if (rel < 0) rel = 0;
            if (rel > span) rel = span;
            Value = minimum + (maximum - minimum) * ((double)rel / span);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                Capture = true;
                UpdateFromMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) UpdateFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
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

    /// <summary>iOS-style toggle switch.</summary>
    public class ToggleSwitch : Control
    {
        private bool isOn;
        private Color onColor = Color.FromArgb(0, 229, 192);
        private Color offColor = Color.FromArgb(58, 61, 99);

        public event EventHandler Toggled;

        public ToggleSwitch()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Size = new Size(52, 26);
        }

        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (isOn != value)
                {
                    isOn = value;
                    Invalidate();
                    EventHandler handler = Toggled;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        public void SetColors(Color on, Color off)
        {
            onColor = on;
            offColor = off;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle pill = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Ui.RoundedRect(pill, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(isOn ? onColor : offColor))
                {
                    g.FillPath(b, gp);
                }
            }

            int knob = Height - 6;
            int x = isOn ? Width - knob - 3 : 3;
            Rectangle kr = new Rectangle(x, 3, knob, knob);
            using (SolidBrush kb = new SolidBrush(Color.White))
            {
                g.FillEllipse(kb, kr);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            IsOn = !IsOn;
        }
    }

    /// <summary>Left navigation button with an accent bar and letter badge.</summary>
    public class SidebarButton : Control
    {
        private bool hovered;
        private bool selected;
        private string badgeText = "V";
        private Color badgeColor = Color.FromArgb(0, 229, 192);

        private Color bg = Color.Transparent;
        private Color hoverBg = Color.FromArgb(30, 32, 68);
        private Color selectedBg = Color.FromArgb(36, 39, 80);
        private Color textColor = Color.FromArgb(236, 238, 248);
        private Color accent = Color.FromArgb(0, 229, 192);

        public SidebarButton()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Size = new Size(184, 48);
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

        public string BadgeText
        {
            get { return badgeText; }
            set
            {
                badgeText = value;
                Invalidate();
            }
        }

        public Color BadgeColor
        {
            get { return badgeColor; }
            set
            {
                badgeColor = value;
                Invalidate();
            }
        }

        public void ApplyPalette(Palette p)
        {
            bg = p.SidebarBg;
            hoverBg = p.NavHover;
            selectedBg = p.NavSelected;
            textColor = p.TextPrimary;
            accent = p.Accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color back = selected ? selectedBg : (hovered ? hoverBg : bg);
            using (SolidBrush b = new SolidBrush(back))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            if (selected)
            {
                using (SolidBrush ab = new SolidBrush(accent))
                {
                    g.FillRectangle(ab, 0, 8, 4, Height - 16);
                }
            }

            Rectangle badgeRect = new Rectangle(14, Height / 2 - 12, 24, 24);
            using (GraphicsPath bp = Ui.RoundedRect(badgeRect, 7))
            {
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(46, badgeColor)))
                {
                    g.FillPath(bb, bp);
                }
            }
            TextRenderer.DrawText(g, badgeText, new Font("Segoe UI", 9f, FontStyle.Bold),
                badgeRect, badgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle textRect = new Rectangle(50, 0, Width - 60, Height);
            TextRenderer.DrawText(g, Text, new Font("Segoe UI", 10f, selected ? FontStyle.Bold : FontStyle.Regular),
                textRect, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

    /// <summary>Rounded filled button used for POWER / Record / Load sounds etc.</summary>
    public class PillButton : Control
    {
        private bool hovered;
        private bool pressed;
        private bool filled = true;

        private Color fillColorValue = Color.FromArgb(0, 229, 192);
        private Color hoverColorValue = Color.FromArgb(0, 204, 173);
        private Color textColorValue = Color.FromArgb(12, 14, 32);
        private Color outlineColorValue = Color.FromArgb(58, 61, 99);

        public PillButton()
        {
            Ui.EnableDoubleBuffering(this);
            Cursor = Cursors.Hand;
            Size = new Size(140, 40);
        }

        public bool Filled
        {
            get { return filled; }
            set
            {
                filled = value;
                Invalidate();
            }
        }

        public void SetColors(Color fill, Color hover, Color text, Color outline)
        {
            fillColorValue = fill;
            hoverColorValue = hover;
            textColorValue = text;
            outlineColorValue = outline;
            Invalidate();
        }

        public void ApplyPalette(Palette p, Color? fillOverride)
        {
            fillColorValue = fillOverride.HasValue ? fillOverride.Value : p.Accent;
            hoverColorValue = ControlPaint.Light(fillColorValue, 0.15f);
            textColorValue = fillOverride == p.PowerOff ? p.TextPrimary : Color.FromArgb(12, 14, 32);
            outlineColorValue = p.CardBorder;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (!Enabled)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, outlineColorValue)))
                {
                    using (GraphicsPath gp = Ui.RoundedRect(r, r.Height / 2))
                    {
                        g.FillPath(b, gp);
                    }
                }
                TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                    Color.FromArgb(140, textColorValue),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            if (filled)
            {
                Color fill = pressed ? ControlPaint.Dark(fillColorValue, 0.05f) : (hovered ? hoverColorValue : fillColorValue);
                using (GraphicsPath gp = Ui.RoundedRect(r, r.Height / 2))
                {
                    using (SolidBrush b = new SolidBrush(fill))
                    {
                        g.FillPath(b, gp);
                    }
                }
            }
            else
            {
                using (GraphicsPath gp = Ui.RoundedRect(r, r.Height / 2))
                {
                    using (Pen p2 = new Pen(hovered ? fillColorValue : outlineColorValue, 1.6f))
                    {
                        g.DrawPath(p2, gp);
                    }
                }
            }

            Color text = filled ? textColorValue : (hovered ? fillColorValue : textColorValue);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }
}
