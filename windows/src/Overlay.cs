using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace SnapNote
{
    // Full-screen frozen screenshot. Drag for a region, click for the window under the cursor.
    class Overlay : Form
    {
        readonly Bitmap shot, dim;
        readonly Rectangle vs;
        readonly List<Rectangle> wins;
        readonly List<Rectangle> monitors = new List<Rectangle>();
        Point down;
        bool mouseIsDown, dragging;
        Rectangle sel, hover, drawn, pillDrawn;
        public Rectangle Selection = Rectangle.Empty;   // in overlay (bitmap) coordinates

        public Overlay(Bitmap screenshot, Rectangle virtualScreen, List<Rectangle> windows)
        {
            vs = virtualScreen;
            wins = windows;
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle b = s.Bounds; b.Offset(-vs.X, -vs.Y); monitors.Add(b);
            }
            shot = Copy(screenshot, false);
            dim = Copy(screenshot, true);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            Bounds = vs;
            Ui.Scale = Native.DpiScaleAt(Cursor.Position.X, Cursor.Position.Y);
            hover = Pick(PointToClientSafe(Cursor.Position));
        }

        Point PointToClientSafe(Point screen) { return new Point(screen.X - vs.X, screen.Y - vs.Y); }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }   // tool window: not in Alt+Tab
        }

        protected override void OnShown(EventArgs e)
        {
            Bounds = vs;
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        static Bitmap Copy(Bitmap src, bool darken)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(src, 0, 0);
                if (darken)
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(115, 11, 11, 12))) g.FillRectangle(br, 0, 0, b.Width, b.Height);
                }
            }
            return b;
        }

        // Visible top-level windows, top of z-order first, in overlay coordinates.
        public static List<Rectangle> VisibleWindows(Rectangle vs)
        {
            List<Rectangle> list = new List<Rectangle>();
            uint me = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindowsProc cb = delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == me) return true;
                StringBuilder cls = new StringBuilder(64);
                Native.GetClassName(h, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW") return true;
                int ex = Native.GetWindowLong(h, -20);
                if ((ex & 0x20) != 0) return true;                                           // click-through overlays
                if ((ex & 0x80) != 0 && Native.GetWindowTextLength(h) == 0) return true;      // untitled tool windows
                Native.RECT r;
                if (Native.DwmGetWindowAttribute(h, 9, out r, 16) != 0) Native.GetWindowRect(h, out r);
                Rectangle rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                rc.Intersect(vs);
                if (rc.Width < 24 || rc.Height < 24) return true;
                rc.Offset(-vs.X, -vs.Y);
                list.Add(rc);
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        Rectangle Pick(Point p)
        {
            foreach (Rectangle r in wins) if (r.Contains(p)) return r;
            foreach (Rectangle m in monitors) if (m.Contains(p)) return m;
            return Rectangle.Empty;
        }

        Rectangle MonitorAt(Point p)
        {
            foreach (Rectangle m in monitors) if (m.Contains(p)) return m;
            return monitors.Count > 0 ? monitors[0] : ClientRectangle;
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        }

        Rectangle Current { get { return dragging ? sel : hover; } }

        void Refresh2()
        {
            Rectangle cur = Current;
            Rectangle pill = PillRect();
            Rectangle area = Rectangle.Empty;
            foreach (Rectangle r in new Rectangle[] { drawn, cur, pill, pillDrawn })
            {
                if (r.IsEmpty) continue;
                Rectangle rr = r; rr.Inflate(Ui.S(10), Ui.S(40));
                area = area.IsEmpty ? rr : Rectangle.Union(area, rr);
            }
            if (!area.IsEmpty) Invalidate(area);
            drawn = cur;
            pillDrawn = pill;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (dragging) { dragging = false; mouseIsDown = false; Refresh2(); }
                else Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            mouseIsDown = true;
            down = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mouseIsDown)
            {
                if (!dragging && (Math.Abs(e.X - down.X) > 3 || Math.Abs(e.Y - down.Y) > 3)) dragging = true;
                if (dragging) sel = Norm(down, e.Location);
            }
            else hover = Pick(e.Location);
            Refresh2();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !mouseIsDown) return;
            mouseIsDown = false;
            Selection = dragging ? sel : Pick(e.Location);
            Selection.Intersect(ClientRectangle);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Selection = Rectangle.Empty; Close(); }
            else if (e.KeyCode == Keys.Enter && !dragging)
            {
                Selection = MonitorAt(PointToClientSafe(Cursor.Position));
                Close();
            }
        }

        // Hint pill at the top centre of the monitor under the cursor
        const string Hint = "Drag to select   ·   Click a window   ·   Enter for full screen   ·   Esc to cancel";

        Rectangle PillRect()
        {
            if (dragging) return Rectangle.Empty;
            Rectangle m = MonitorAt(PointToClientSafe(Cursor.Position));
            Size sz = TextRenderer.MeasureText(Hint, Ui.Sans(13), Size.Empty, Ui.TextFlags);
            int w = sz.Width + Ui.S(32), h = Ui.S(36);
            return new Rectangle(m.X + (m.Width - w) / 2, m.Y + Ui.S(24), w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);

            Rectangle cur = Current;
            if (!cur.IsEmpty)
            {
                Rectangle ir = Rectangle.Intersect(cur, clip);
                if (!ir.IsEmpty) g.DrawImage(shot, ir, ir, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                // Linework selection: ink outline with a light inner line (reads on light and dark screens),
                // square handles on corners and edges while dragging.
                float olw = Math.Max(1f, Ui.Scale);
                using (Pen outer = new Pen(Color.FromArgb(200, 11, 11, 12), olw * 3))
                    g.DrawRectangle(outer, cur.X, cur.Y, cur.Width - 1, cur.Height - 1);
                using (Pen p = new Pen(Color.FromArgb(242, 242, 243), olw))
                {
                    if (!dragging) p.DashPattern = new float[] { 4, 3 };
                    g.DrawRectangle(p, cur.X, cur.Y, cur.Width - 1, cur.Height - 1);
                }
                if (dragging)
                {
                    int hs = Ui.S(7);
                    int[] xs = { cur.Left, cur.Left + cur.Width / 2, cur.Right - 1 };
                    int[] ys = { cur.Top, cur.Top + cur.Height / 2, cur.Bottom - 1 };
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(242, 242, 243)))
                    using (Pen hp = new Pen(Color.FromArgb(11, 11, 12), olw))
                        foreach (int hx in xs)
                            foreach (int hy in ys)
                            {
                                if (hx == xs[1] && hy == ys[1]) continue;
                                Rectangle hr = new Rectangle(hx - hs / 2, hy - hs / 2, hs, hs);
                                g.FillRectangle(hb, hr);
                                g.DrawRectangle(hp, hr);
                            }
                }
                // size label, mono, under or above the box
                string label = cur.Width + " × " + cur.Height;
                Font f = Ui.Mono(11);
                Size sz = TextRenderer.MeasureText(label, f, Size.Empty, Ui.TextFlags);
                int lw = sz.Width + Ui.S(12), lh = Ui.S(22);
                int ly = cur.Y - lh - Ui.S(6);
                if (ly < MonitorAt(cur.Location).Y + Ui.S(4)) ly = cur.Y + Ui.S(6);
                Rectangle lr = new Rectangle(cur.X, ly, lw, lh);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Ui.Round(lr, Ui.S(4)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(11, 11, 12))) g.FillPath(b, path);
                TextRenderer.DrawText(g, label, f, new Point(lr.X + Ui.S(6), lr.Y + (lh - sz.Height) / 2), Color.FromArgb(242, 242, 243), Color.FromArgb(11, 11, 12), Ui.TextFlags);
                g.SmoothingMode = SmoothingMode.None;
            }

            Rectangle pill = PillRect();
            if (!pill.IsEmpty && pill.IntersectsWith(clip))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Ui.Round(pill, pill.Height / 2f))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                    using (Pen p = new Pen(Theme.Line)) g.DrawPath(p, path);
                }
                Size sz = TextRenderer.MeasureText(Hint, Ui.Sans(13), Size.Empty, Ui.TextFlags);
                TextRenderer.DrawText(g, Hint, Ui.Sans(13), new Point(pill.X + Ui.S(16), pill.Y + (pill.Height - sz.Height) / 2), Theme.Ink, Theme.Surface, Ui.TextFlags);
            }
        }

        // Self-test: paint the overlay with a given hover box into a bitmap.
        public Bitmap Preview(Rectangle hv)
        {
            hover = hv;
            Bitmap b = new Bitmap(vs.Width, vs.Height);
            using (Graphics g = Graphics.FromImage(b))
                OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, vs.Width, vs.Height)));
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { shot.Dispose(); dim.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
