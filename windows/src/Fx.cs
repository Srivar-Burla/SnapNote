using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SnapNote
{
    // Short hairline animations that play on top of everything after Copy (paper plane) and Save (tray).
    // A click-through, per-pixel-alpha window; it never takes focus and removes itself when done.
    class Fx : Form
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Native.POINT pos, ref SIZE size, IntPtr src, ref Native.POINT srcPos, int key, ref BLEND blend, int flags);
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, out bool value, int winIni);

        // Windows "Animation effects" switch (Settings > Accessibility > Visual effects) and settings.ini
        public static bool On
        {
            get
            {
                if (!Program.Cfg.Animations) return false;
                bool on;
                try { if (SystemParametersInfo(0x1042, 0, out on, 0)) return on; } catch { }
                return true;
            }
        }

        public enum Kind { Plane, Tray }

        readonly Kind kind;
        readonly Bitmap thumb;
        readonly Rectangle area;          // screen pixels covered by this window
        readonly PointF start, target;    // in window coordinates
        readonly float k;                 // design-pixel scale
        readonly Action atLanding;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer();
        bool landed;
        const int Dur = 1000;

        // from = the editor's screen rectangle; the animation heads for the toast corner.
        public static void Play(Kind kind, Bitmap final, Rectangle from, Action atLanding)
        {
            if (!On) { atLanding(); return; }
            try { new Fx(kind, final, from, atLanding).Show(); }
            catch { atLanding(); }
        }

        Fx(Kind kind, Bitmap final, Rectangle from, Action atLanding)
        {
            this.kind = kind;
            this.atLanding = atLanding;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            k = Native.DpiScaleAt(wa.X + wa.Width / 2, wa.Y + wa.Height / 2);

            // Thumbnail of the marked-up image, about 180 x 120 design pixels
            float tz = Math.Min(180 * k / final.Width, 120 * k / final.Height);
            tz = Math.Min(tz, 1f);
            int tw = Math.Max(8, (int)(final.Width * tz)), th = Math.Max(8, (int)(final.Height * tz));
            thumb = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(final, 0, 0, tw, th);
            }

            Point s = new Point(from.X + from.Width / 2, from.Y + from.Height / 2);
            // The toast sits in the bottom-right corner of the primary screen
            Point t = new Point(wa.Right - (int)(16 * k) - (int)(170 * k), wa.Bottom - (int)(16 * k) - (int)(40 * k));
            if (kind == Kind.Tray) t.Y -= (int)(70 * k);
            Rectangle r = Rectangle.FromLTRB(Math.Min(s.X, t.X), Math.Min(s.Y, t.Y), Math.Max(s.X, t.X), Math.Max(s.Y, t.Y));
            r.Inflate((int)(140 * k), (int)(140 * k));
            r = Rectangle.Intersect(r, SystemInformation.VirtualScreen);
            area = r;
            start = new PointF(s.X - r.X, s.Y - r.Y);
            target = new PointF(t.X - r.X, t.Y - r.Y);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
            timer.Interval = 15;
            timer.Tick += delegate { Frame(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000;   // layered, click-through, tool, topmost, no-activate
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Frame();
            timer.Start();
        }

        static float Seg(float t, float a, float b) { return Math.Max(0, Math.Min(1, (t - a) / (b - a))); }
        static float EaseOut(float t) { return 1 - (float)Math.Pow(1 - t, 3); }
        static float EaseIn(float t) { return t * t * t; }
        static float EaseInOut(float t) { return t < .5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2; }
        static float Spring(float t) { return t <= 0 ? 0 : (float)(Math.Exp(-6 * t) * Math.Cos(14 * t)); }

        void Frame()
        {
            float ms = clock.ElapsedMilliseconds;
            int landAt = kind == Kind.Plane ? 700 : 760;
            if (!landed && ms >= landAt) { landed = true; try { atLanding(); } catch { } }
            if (ms >= Dur) { timer.Stop(); Close(); return; }

            using (Bitmap b = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    if (kind == Kind.Plane) DrawPlane(g, ms); else DrawTray(g, ms);
                }
                Push(b);
            }
        }

        void Push(Bitmap b)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = b.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hb);
                SIZE size = new SIZE { cx = b.Width, cy = b.Height };
                Native.POINT pos = new Native.POINT { X = area.X, Y = area.Y };
                Native.POINT zero = new Native.POINT();
                BLEND bl = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref zero, 0, ref bl, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hb != IntPtr.Zero) DeleteObject(hb);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // ---- pieces ----

        static readonly Color Ink = Color.FromArgb(11, 11, 12);

        void DrawCard(Graphics g, PointF c, float sx, float sy, float alpha)
        {
            if (alpha <= 0.01f || sx <= 0.01f || sy <= 0.01f) return;
            float w = thumb.Width * sx, h = thumb.Height * sy;
            RectangleF r = new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
            int a = (int)(255 * alpha);
            for (int i = 3; i >= 1; i--)
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(10 * alpha), 0, 0, 0)))
                    g.FillRectangle(sb, r.X - i * k, r.Y - i * k + 2 * k, r.Width + 2 * i * k, r.Height + 2 * i * k);
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = alpha;
                ia.SetColorMatrix(cm);
                g.DrawImage(thumb, Rectangle.Round(r), 0, 0, thumb.Width, thumb.Height, GraphicsUnit.Pixel, ia);
            }
            using (Pen p = new Pen(Color.FromArgb(a, Ink), Math.Max(1f, k))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }

        // Paper plane as two hairline wings, with a white edge so it reads on any background.
        void DrawPlaneShape(Graphics g, PointF c, float angle, float scale, float alpha)
        {
            if (alpha <= 0.01f) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            g.ScaleTransform(scale * k, scale * k);
            PointF[] lower = { new PointF(-14, -1), new PointF(14, 0), new PointF(-7, 8) };
            PointF[] upper = { new PointF(-14, -1), new PointF(14, 0), new PointF(-5, -9) };
            int a = (int)(255 * alpha);
            using (Pen halo = new Pen(Color.FromArgb(a, 255, 255, 255), 3.2f)) { halo.LineJoin = LineJoin.Round; g.DrawPolygon(halo, lower); g.DrawPolygon(halo, upper); }
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(a, 241, 241, 243))) g.FillPolygon(soft, lower);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(a, 255, 255, 255))) g.FillPolygon(white, upper);
            using (Pen p = new Pen(Color.FromArgb(a, Ink), 1.2f))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawPolygon(p, lower);
                g.DrawPolygon(p, upper);
                g.DrawLine(p, 14, 0, -6, 3);
            }
            g.Restore(st);
        }

        void DrawPlane(Graphics g, float ms)
        {
            // fold the card (0-240 ms), fly (200-820 ms), fade (820-1000 ms)
            float fold = EaseIn(Seg(ms, 0, 240));
            DrawCard(g, start, 1 - 0.85f * fold, 1 - 0.95f * fold, 1 - Seg(ms, 180, 250));

            float fly = EaseInOut(Seg(ms, 200, 820));
            float dist = (float)Math.Sqrt((target.X - start.X) * (target.X - start.X) + (target.Y - start.Y) * (target.Y - start.Y));
            PointF ctrl = new PointF((start.X + target.X) / 2, Math.Min(start.Y, target.Y) - Math.Max(60 * k, dist * 0.3f));
            ctrl.Y = Math.Max(10 * k, ctrl.Y);
            Func<float, PointF> at = delegate (float u)
            {
                float v = 1 - u;
                return new PointF(v * v * start.X + 2 * u * v * ctrl.X + u * u * target.X, v * v * start.Y + 2 * u * v * ctrl.Y + u * u * target.Y);
            };
            PointF p0 = at(fly);
            PointF p1 = at(Math.Min(1, fly + 0.01f));
            float ang = (float)(Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180 / Math.PI);
            if (fly >= 1) ang = (float)(Math.Atan2(target.Y - ctrl.Y, target.X - ctrl.X) * 180 / Math.PI);
            float appear = EaseOut(Seg(ms, 170, 300));
            float fade = 1 - Seg(ms, 800, 980);

            // dotted hairline trail behind the plane
            if (ms > 220)
            {
                int n = 40;
                PointF[] pts = new PointF[n + 1];
                for (int i = 0; i <= n; i++) pts[i] = at(fly * i / n);
                using (Pen tr = new Pen(Color.FromArgb((int)(150 * fade), Ink), Math.Max(1f, 0.9f * k)))
                {
                    tr.DashPattern = new float[] { 2.5f, 2.5f };
                    using (Pen trHalo = new Pen(Color.FromArgb((int)(160 * fade), 255, 255, 255), Math.Max(2f, 2.6f * k)))
                    {
                        trHalo.DashPattern = new float[] { 1f, 1f };
                        g.DrawLines(trHalo, pts);
                    }
                    g.DrawLines(tr, pts);
                }
            }
            DrawPlaneShape(g, p0, ang, (0.4f + 0.75f * appear) * (1 - 0.35f * fly), Math.Min(appear, fade));
        }

        void DrawTrayShape(Graphics g, PointF c, float alpha)
        {
            if (alpha <= 0.01f) return;
            int a = (int)(255 * alpha);
            float s = k;
            PointF[] rim = { P(c, -46, 6, s), P(c, 0, -9, s), P(c, 46, 6, s), P(c, 0, 21, s) };
            PointF[] inner = { P(c, -36, 6, s), P(c, 0, -5, s), P(c, 36, 6, s), P(c, 0, 17, s) };
            PointF[] front = { P(c, -46, 6, s), P(c, 0, 21, s), P(c, 46, 6, s), P(c, 46, 13, s), P(c, 0, 28, s), P(c, -46, 13, s) };
            using (Pen halo = new Pen(Color.FromArgb(a, 255, 255, 255), 3.5f * s)) { halo.LineJoin = LineJoin.Round; g.DrawPolygon(halo, front); g.DrawPolygon(halo, rim); }
            using (SolidBrush w = new SolidBrush(Color.FromArgb(a, 255, 255, 255))) g.FillPolygon(w, rim);
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(a, 241, 241, 243))) g.FillPolygon(soft, front);
            using (Pen p = new Pen(Color.FromArgb(a, Ink), 1.1f * s))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawPolygon(p, front);
                g.DrawPolygon(p, rim);
            }
            using (Pen p = new Pen(Color.FromArgb(a, 107, 107, 114), 0.9f * s)) g.DrawPolygon(p, inner);
        }

        static PointF P(PointF c, float x, float y, float s) { return new PointF(c.X + x * s, c.Y + y * s); }

        void DrawTray(Graphics g, float ms)
        {
            // tray appears (0-220), card shrinks (0-260) and drops in (260-640), tray dips and settles (640-1000)
            float trayIn = EaseOut(Seg(ms, 0, 220));
            float trayOut = 1 - Seg(ms, 860, 1000);
            float dip = ms > 640 ? Spring(Seg(ms, 640, 1000)) * 6 * k : 0;
            PointF tc = new PointF(target.X, target.Y + (1 - trayIn) * 10 * k + dip);

            float shrink = EaseOut(Seg(ms, 0, 260));
            float drop = EaseIn(Seg(ms, 260, 640));
            float sc = 1 - 0.6f * shrink;
            PointF c = new PointF(start.X + (tc.X - start.X) * drop, start.Y + (tc.Y - 2 * k - start.Y) * drop - (float)Math.Sin(drop * Math.PI) * 40 * k);
            float cardAlpha = 1 - Seg(ms, 610, 660);

            DrawTrayShape(g, tc, trayIn * trayOut);
            // the card goes behind the tray's front edge as it lands
            DrawCard(g, c, sc * (1 - 0.1f * drop), sc * (1 - 0.45f * drop), cardAlpha);
            if (drop > 0.85f)
            {
                PointF[] front = { P(tc, -46, 6, k), P(tc, 0, 21, k), P(tc, 46, 6, k), P(tc, 46, 13, k), P(tc, 0, 28, k), P(tc, -46, 13, k) };
                using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(255 * trayIn * trayOut), 241, 241, 243))) g.FillPolygon(soft, front);
                using (Pen p = new Pen(Color.FromArgb((int)(255 * trayIn * trayOut), Ink), 1.1f * k)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, front); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); thumb.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
