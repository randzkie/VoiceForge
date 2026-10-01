using System.Drawing;

namespace VoiceForge.Services
{
    /// <summary>Complete color palette used by every themed control.</summary>
    public class Palette
    {
        public Color WindowBg;       // Form / content background
        public Color SidebarBg;      // Left navigation background
        public Color TitleBarBg;     // Custom title bar
        public Color CardBg;         // Voice cards, panels
        public Color CardHover;
        public Color CardBorder;
        public Color InputBg;        // Combo boxes, list views
        public Color InputBorder;
        public Color TextPrimary;
        public Color TextMuted;
        public Color Accent;         // Main turquoise
        public Color AccentAlt;      // Pink highlight
        public Color PowerOn;
        public Color PowerOff;
        public Color Danger;
        public Color Success;
        public Color NavHover;
        public Color NavSelected;
    }

    public static class ThemeService
    {
        public static Palette Get(string theme)
        {
            if (theme == "light") return Light();
            return Dark();
        }

        public static Palette Dark()
        {
            Palette p = new Palette();
            p.WindowBg = Color.FromArgb(20, 21, 46);        // #14152E deep navy
            p.SidebarBg = Color.FromArgb(16, 17, 39);       // #101127
            p.TitleBarBg = Color.FromArgb(16, 17, 39);
            p.CardBg = Color.FromArgb(30, 32, 68);          // #1E2044
            p.CardHover = Color.FromArgb(39, 42, 85);       // #272A55
            p.CardBorder = Color.FromArgb(46, 49, 88);      // #2E3158
            p.InputBg = Color.FromArgb(27, 29, 62);         // #1B1D3E
            p.InputBorder = Color.FromArgb(49, 52, 89);     // #313459
            p.TextPrimary = Color.FromArgb(236, 238, 248);  // #ECEEF8
            p.TextMuted = Color.FromArgb(139, 144, 176);    // #8B90B0
            p.Accent = Color.FromArgb(0, 229, 192);         // #00E5C0
            p.AccentAlt = Color.FromArgb(255, 77, 141);     // #FF4D8D
            p.PowerOn = Color.FromArgb(0, 229, 192);
            p.PowerOff = Color.FromArgb(58, 61, 99);        // #3A3D63
            p.Danger = Color.FromArgb(255, 90, 110);        // #FF5A6E
            p.Success = Color.FromArgb(70, 214, 140);       // #46D68C
            p.NavHover = Color.FromArgb(30, 32, 68);
            p.NavSelected = Color.FromArgb(36, 39, 80);
            return p;
        }

        public static Palette Light()
        {
            Palette p = new Palette();
            p.WindowBg = Color.FromArgb(244, 245, 250);     // #F4F5FA
            p.SidebarBg = Color.FromArgb(233, 235, 244);    // #E9EBF4
            p.TitleBarBg = Color.FromArgb(233, 235, 244);
            p.CardBg = Color.FromArgb(255, 255, 255);
            p.CardHover = Color.FromArgb(238, 240, 248);    // #EEF0F8
            p.CardBorder = Color.FromArgb(217, 220, 236);   // #D9DCEC
            p.InputBg = Color.FromArgb(255, 255, 255);
            p.InputBorder = Color.FromArgb(201, 205, 228);  // #C9CDE4
            p.TextPrimary = Color.FromArgb(28, 29, 51);     // #1C1D33
            p.TextMuted = Color.FromArgb(108, 113, 146);    // #6C7192
            p.Accent = Color.FromArgb(0, 179, 155);         // #00B39B
            p.AccentAlt = Color.FromArgb(226, 58, 119);     // #E23A77
            p.PowerOn = Color.FromArgb(0, 179, 155);
            p.PowerOff = Color.FromArgb(169, 174, 201);     // #A9AEC9
            p.Danger = Color.FromArgb(229, 72, 77);         // #E5484D
            p.Success = Color.FromArgb(43, 165, 93);        // #2BA55D
            p.NavHover = Color.FromArgb(224, 227, 240);
            p.NavSelected = Color.FromArgb(216, 220, 238);
            return p;
        }
    }
}
