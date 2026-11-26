using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace SnapNote
{
    // Linework tokens (design-system/linework/tokens.css), light and dark.
    static class Theme
    {
        public static bool Dark;
        public static Color Bg, Surface, Sunken, Hover, Ink, InkMuted, InkFaint, OnPrimary, Line, LineControl, Focus, Danger, Info, Warning, Success;

        public static void Load(string pref)
        {
            pref = (pref ?? "auto").Trim().ToLowerInvariant();
            bool dark = false;
            if (pref == "dark") dark = true;
            else if (pref != "light") dark = !ReadLight("AppsUseLightTheme");
            Dark = dark;
            if (!dark)
            {
                Bg = H("#ffffff"); Surface = H("#ffffff"); Sunken = H("#f5f5f6"); Hover = H("#efeff1");
                Ink = H("#0b0b0c"); InkMuted = H("#6b6b72"); InkFaint = H("#a3a3ab"); OnPrimary = H("#ffffff");
                Line = H("#e8e8eb"); LineControl = H("#8a8a92"); Focus = H("#2b59c3");
                Danger = H("#c2362b"); Info = H("#2b59c3"); Warning = H("#a15c00"); Success = H("#1f7a4a");
            }
            else
            {
                Bg = H("#0b0b0c"); Surface = H("#131315"); Sunken = H("#1a1a1d"); Hover = H("#222226");
                Ink = H("#f2f2f3"); InkMuted = H("#9d9da6"); InkFaint = H("#5c5c64"); OnPrimary = H("#0b0b0c");
                Line = H("#232327"); LineControl = H("#66666f"); Focus = H("#7aa2ff");
                Danger = H("#ff7b70"); Info = H("#7aa2ff"); Warning = H("#e0a43a"); Success = H("#4cc38a");
            }
        }

        public static bool TaskbarDark() { return !ReadLight("SystemUsesLightTheme"); }

        static bool ReadLight(string name)
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", name, 1);
                if (v is int) return (int)v != 0;
            }
            catch { }
            return true;
        }

        public static Color H(string s) { return ColorTranslator.FromHtml(s); }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // Sizing, fonts, shapes and icons shared by the UI.
    static class Ui
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        static string sans, sansMedium, mono;
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        static void Pick()
        {
            if (sans != null) return;
            HashSet<string> fams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (InstalledFontCollection ifc = new InstalledFontCollection())
                foreach (FontFamily f in ifc.Families) fams.Add(f.Name);
            sans = First(fams, "Hanken Grotesk", "Segoe UI Variable Text", "Segoe UI");
            sansMedium = First(fams, "Hanken Grotesk Medium", sans == "Hanken Grotesk" ? "Hanken Grotesk" : null,
                "Segoe UI Variable Text Semibold", "Segoe UI Semibold", sans);
            mono = First(fams, "JetBrains Mono", "Cascadia Mono", "Consolas");
        }

        static string First(HashSet<string> fams, params string[] names)
        {
            foreach (string n in names) if (n != null && fams.Contains(n)) return n;
            return "Segoe UI";
        }

        public static string SansName { get { Pick(); return sans; } }
        public static Font Sans(float px) { return Get(SansName, px); }
        public static Font Medium(float px) { Pick(); return Get(sansMedium, px); }
        public static Font Mono(float px) { Pick(); return Get(mono, px); }

        static Font Get(string fam, float px)
        {
            float size = px * Scale;
            string k = fam + "|" + size.ToString("0.00");
            Font f;
            if (!cache.TryGetValue(k, out f)) { f = new Font(fam, size, FontStyle.Regular, GraphicsUnit.Pixel); cache[k] = f; }
            return f;
        }

        public const System.Windows.Forms.TextFormatFlags TextFlags =
            System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix;

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Icons on a 16px grid, 1.25 stroke, like Linework's .lw-ico
        public delegate void IconFn(Graphics g, RectangleF r, Color c);

        static Pen IconPen(Color c)
        {
            Pen p = new Pen(c, 1.25f * Scale);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }
        static PointF P(RectangleF r, float x, float y) { float k = r.Width / 16f; return new PointF(r.X + x * k, r.Y + y * k); }

        public static void IconRect(Graphics g, RectangleF r, Color c)
        {
            float k = r.Width / 16f;
            using (Pen p = IconPen(c))
            using (GraphicsPath path = Round(new RectangleF(r.X + 2.5f * k, r.Y + 3.5f * k, 11 * k, 9 * k), 1.5f * k))
                g.DrawPath(p, path);
        }

        public static void IconArrow(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = IconPen(c))
            {
                g.DrawLine(p, P(r, 3.5f, 12.5f), P(r, 12.5f, 3.5f));
                g.DrawLines(p, new PointF[] { P(r, 6.5f, 3.5f), P(r, 12.5f, 3.5f), P(r, 12.5f, 9.5f) });
            }
        }

        public static void IconUndo(Graphics g, RectangleF r, Color c)
        {
            float k = r.Width / 16f;
            using (Pen p = IconPen(c))
            {
                g.DrawLines(p, new PointF[] { P(r, 6, 4), P(r, 3.5f, 6.5f), P(r, 6, 9) });
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLine(P(r, 3.5f, 6.5f), P(r, 9.5f, 6.5f));
                    path.AddArc(r.X + 6.5f * k, r.Y + 6.5f * k, 6 * k, 6 * k, 270, 180);
                    path.AddLine(P(r, 9.5f, 12.5f), P(r, 6.5f, 12.5f));
                    g.DrawPath(p, path);
                }
            }
        }

        public static void IconClose(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = IconPen(c))
            {
                g.DrawLine(p, P(r, 4.5f, 4.5f), P(r, 11.5f, 11.5f));
                g.DrawLine(p, P(r, 11.5f, 4.5f), P(r, 4.5f, 11.5f));
            }
        }
    }
}
