using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SnapNote
{
    // A highlight box (with its comment bubble) or an arrow, in image pixels.
    class Shape
    {
        public const int Rect = 0, Arrow = 1;
        public int Kind;
        public Color Color;
        public PointF A, B;
        public float W;
        public string Comment = "";
        public PointF BubblePos;
        public bool Placed, Outside;
        public long Born;   // when it was drawn (Ann.Clock ms), for the drawn-in animation; 0 = no animation

        public RectangleF Box
        {
            get { return RectangleF.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y)); }
        }

        // Arrows have three nodes the user can pull. Until a node is pulled it follows the others:
        // the middle node sits halfway, and nodes 1 and 3 sit on the single curve through A, middle, B.
        // So pulling the middle bends the whole arrow one way; pulling node 1 or 3 adds a second bend.
        public readonly PointF[] N = new PointF[3];
        public readonly bool[] Pinned = new bool[3];

        public PointF Node(int i)
        {
            PointF mid = Pinned[1] ? N[1] : new PointF((A.X + B.X) / 2, (A.Y + B.Y) / 2);
            if (i == 1) return mid;
            if (Pinned[i]) return N[i];
            float t = i == 0 ? 0.25f : 0.75f;
            // quadratic Bezier that passes through mid at t = 0.5
            float cx = 2 * mid.X - (A.X + B.X) / 2, cy = 2 * mid.Y - (A.Y + B.Y) / 2;
            float u = 1 - t;
            return new PointF(u * u * A.X + 2 * t * u * cx + t * t * B.X, u * u * A.Y + 2 * t * u * cy + t * t * B.Y);
        }

        public PointF[] Points() { return new PointF[] { A, Node(0), Node(1), Node(2), B }; }

        // What the comment bubble points at: the highlight, or the tail of the arrow.
        public RectangleF Anchor
        {
            get { return Kind == Rect ? Box : new RectangleF(A.X - 2, A.Y - 2, 4, 4); }
        }

        public RectangleF Bounds
        {
            get
            {
                if (Kind == Rect) return Box;
                float l = A.X, t = A.Y, r = A.X, b = A.Y;
                foreach (PointF p in Points()) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y); }
                return RectangleF.FromLTRB(l, t, r, b);
            }
        }
    }

    // Draws highlights, arrows and frosted comment bubbles. Used by the editor view and the final copied image,
    // so what you see is what gets pasted. All sizes are in image pixels; K = image pixels per design pixel.
    static class Ann
    {
        public static float K = 1f;

        // Drawn-in animation: outline draws itself (0-368 ms), badge drops in (253-552), bubble unfolds (437-736).
        public static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public const int AnimMs = 810;
        static bool animate;
        static long now;
        static float Prog(Shape s, float a, float b)
        {
            if (!animate || s.Born == 0) return 1;
            return Math.Max(0, Math.Min(1, (now - s.Born - a) / (b - a)));
        }
        static float EaseInOut(float t) { return t < .5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2; }
        static float EaseOut(float t) { return 1 - (float)Math.Pow(1 - t, 3); }
        static float Back(float t) { float c = 1.7f, c3 = c + 1; return 1 + c3 * (float)Math.Pow(t - 1, 3) + c * (float)Math.Pow(t - 1, 2); }

        static PointF[] Partial(PointF[] f, float frac)
        {
            float total = 0;
            for (int i = 1; i < f.Length; i++) total += Dist(f[i - 1], f[i]);
            float want = total * frac, acc = 0;
            List<PointF> o = new List<PointF>();
            o.Add(f[0]);
            for (int i = 1; i < f.Length; i++)
            {
                float l = Dist(f[i - 1], f[i]);
                if (acc + l >= want)
                {
                    float t = l == 0 ? 0 : (want - acc) / l;
                    o.Add(new PointF(f[i - 1].X + (f[i].X - f[i - 1].X) * t, f[i - 1].Y + (f[i].Y - f[i - 1].Y) * t));
                    break;
                }
                o.Add(f[i]);
                acc += l;
            }
            if (o.Count < 2) o.Add(f[0]);
            return o.ToArray();
        }
        static float Dist(PointF a, PointF b) { return (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); }

        static void DrawPartial(Graphics g, Shape s, PointF[] pts)
        {
            using (Pen halo = new Pen(Color.FromArgb(235, 255, 255, 255), s.W + 3f * K)) { halo.LineJoin = LineJoin.Round; halo.StartCap = LineCap.Round; halo.EndCap = LineCap.Round; g.DrawLines(halo, pts); }
            using (Pen pen = new Pen(s.Color, s.W)) { pen.LineJoin = LineJoin.Round; pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawLines(pen, pts); }
        }
        public const string Placeholder = "Type a comment";
        public const string EmptyHint = "Click to comment";

        static readonly Color Ink = Color.FromArgb(11, 11, 12);
        static readonly Color InkMuted = Color.FromArgb(107, 107, 114);

        static Font font, mono;
        static float fontK = -1;
        static Graphics mg;
        static StringFormat sf;

        static void Fonts()
        {
            if (fontK == K && font != null) return;
            if (font != null) { font.Dispose(); mono.Dispose(); }
            font = new Font(Ui.SansName, 13f * K, FontStyle.Regular, GraphicsUnit.Pixel);
            string monoName = Ui.Mono(11).FontFamily.Name;
            mono = new Font(monoName, 11f * K, FontStyle.Bold, GraphicsUnit.Pixel);
            fontK = K;
            if (mg == null)
            {
                mg = Graphics.FromImage(new Bitmap(1, 1));
                mg.TextRenderingHint = TextRenderingHint.AntiAlias;
                sf = (StringFormat)StringFormat.GenericTypographic.Clone();
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            }
        }

        static float Wd(string s) { Fonts(); return s.Length == 0 ? 0 : mg.MeasureString(s, font, PointF.Empty, sf).Width; }
        static float LineH { get { Fonts(); return (float)Math.Ceiling(font.GetHeight() * 1.08f); } }

        // Bubble metrics
        static float Pad { get { return 10 * K; } }
        static float Chip { get { return 18 * K; } }
        static float Gap { get { return 8 * K; } }
        static float MaxW { get { return 264 * K; } }
        static float MinW { get { return 148 * K; } }
        static float Radius { get { return 10 * K; } }
        static float TextMax { get { return MaxW - Pad * 2 - Chip - Gap; } }

        // Word wrap; returns {start, length} per line (indices into t).
        public static List<int[]> Wrap(string t, float maxW)
        {
            List<int[]> lines = new List<int[]>();
            int n = t.Length, ps = 0;
            while (true)
            {
                int nl = t.IndexOf('\n', ps);
                int pe = nl < 0 ? n : nl;
                int ls = ps;
                if (ls == pe) lines.Add(new int[] { ls, 0 });
                while (ls < pe)
                {
                    int fit = ls, j = ls;
                    while (j < pe)
                    {
                        int k2 = j;
                        while (k2 < pe && t[k2] != ' ') k2++;
                        while (k2 < pe && t[k2] == ' ') k2++;
                        if (Wd(t.Substring(ls, k2 - ls).TrimEnd(' ')) <= maxW) { fit = k2; j = k2; } else break;
                    }
                    if (fit == ls)
                    {
                        int c = ls + 1;
                        while (c < pe && Wd(t.Substring(ls, c + 1 - ls)) <= maxW) c++;
                        fit = c;
                    }
                    lines.Add(new int[] { ls, fit - ls });
                    ls = fit;
                }
                if (nl < 0) break;
                ps = nl + 1;
            }
            return lines;
        }

        public static SizeF BubbleSize(string shown, out List<int[]> lines)
        {
            lines = Wrap(shown, TextMax);
            float widest = 0;
            foreach (int[] l in lines) widest = Math.Max(widest, Wd(shown.Substring(l[0], l[1]).TrimEnd(' ')));
            float w = Math.Max(MinW, Math.Min(MaxW, Pad * 2 + Chip + Gap + widest + 3 * K));
            float h = Pad * 2 + Math.Max(Chip, lines.Count * LineH);
            return new SizeF((float)Math.Ceiling(w), (float)Math.Ceiling(h));
        }

        static string Shown(Shape s, bool editing, bool final)
        {
            if (s.Comment.Length > 0) return s.Comment;
            return final ? "" : (editing ? Placeholder : EmptyHint);
        }

        // Bubble rectangle; places it on first use and keeps it inside the image whenever it fits.
        public static RectangleF BubbleRect(Shape s, Size img, bool editing, bool final)
        {
            List<int[]> lines;
            SizeF sz = BubbleSize(Shown(s, editing, final).Length > 0 ? Shown(s, editing, final) : Placeholder, out lines);
            if (!s.Placed) { Place(s, sz, img); s.Placed = true; }
            PointF p = s.BubblePos;
            if (!s.Outside && sz.Width <= img.Width && sz.Height <= img.Height)
            {
                p.X = Math.Max(0, Math.Min(img.Width - sz.Width, p.X));
                p.Y = Math.Max(0, Math.Min(img.Height - sz.Height, p.Y));
            }
            return new RectangleF(p, sz);
        }

        // The shapes of the open editor, so a new bubble can avoid the others.
        public static List<Shape> Scene;

        static void Place(Shape s, SizeF sz, Size img)
        {
            RectangleF box = s.Anchor;
            float gap = 14 * K, w = sz.Width, h = sz.Height;
            List<RectangleF> taken = new List<RectangleF>();
            if (s.Kind == Shape.Arrow) taken.Add(s.Bounds);
            float outsideBottom = float.MinValue;
            if (Scene != null)
                foreach (Shape o in Scene)
                {
                    if (o == s) continue;
                    taken.Add(o.Bounds);
                    if (!o.Placed) continue;
                    RectangleF ob = BubbleRect(o, img, false, false);
                    taken.Add(ob);
                    if (o.Outside) outsideBottom = Math.Max(outsideBottom, ob.Bottom);
                }

            // Inside the image only when it has room for a bubble without smothering the capture
            bool roomy = img.Width >= w * 1.8f && img.Height >= h * 2.5f;
            if (roomy)
            {
                float yAt = Math.Max(0, Math.Min(img.Height - h, box.Top));
                float xAt = Math.Max(0, Math.Min(img.Width - w, box.Left));
                if (s.Kind == Shape.Arrow)
                {
                    yAt = Math.Max(0, Math.Min(img.Height - h, box.Top - h / 2));
                    xAt = Math.Max(0, Math.Min(img.Width - w, box.Left - w / 2));
                }
                PointF[] cands = {
                    new PointF(box.Right + gap, yAt), new PointF(box.Left - gap - w, yAt),
                    new PointF(xAt, box.Bottom + gap), new PointF(xAt, box.Top - gap - h) };
                if (s.Kind == Shape.Arrow)
                {
                    // Prefer the side of the tail facing away from where the arrow goes
                    float dx = s.Node(0).X - s.A.X, dy = s.Node(0).Y - s.A.Y;
                    Array.Sort(cands, delegate (PointF p1, PointF p2)
                    {
                        float d1 = (p1.X + w / 2 - s.A.X) * dx + (p1.Y + h / 2 - s.A.Y) * dy;
                        float d2 = (p2.X + w / 2 - s.A.X) * dx + (p2.Y + h / 2 - s.A.Y) * dy;
                        return d1.CompareTo(d2);
                    });
                }
                foreach (bool strict in new bool[] { true, false })
                    foreach (PointF c in cands)
                    {
                        RectangleF r = new RectangleF(c, sz);
                        if (r.Left < 0 || r.Top < 0 || r.Right > img.Width || r.Bottom > img.Height) continue;
                        bool hit = false;
                        if (strict) foreach (RectangleF t in taken) if (t.IntersectsWith(r)) { hit = true; break; }
                        if (!hit) { s.BubblePos = c; s.Outside = false; return; }
                    }
                s.BubblePos = new PointF(img.Width - w, img.Height - h);   // overlaps; can be dragged
                s.Outside = false;
                return;
            }
            // Small capture: stack bubbles in a column to the right of the image
            float y = Math.Max(0, box.Top);
            if (outsideBottom != float.MinValue) y = Math.Max(y, outsideBottom + 8 * K);
            s.BubblePos = new PointF(img.Width + gap, y);
            s.Outside = true;
        }

        public static void Dropped(Shape s, Size img)
        {
            RectangleF r = BubbleRect(s, img, false, false);
            s.Outside = r.Left < 0 || r.Top < 0 || r.Right > img.Width || r.Bottom > img.Height;
        }

        public static RectangleF CloseRect(RectangleF bubble)
        {
            float d = 18 * K;
            return new RectangleF(bubble.Right - d * 0.6f, bubble.Top - d * 0.4f, d, d);
        }

        public static RectangleF BadgeRect(RectangleF box, Size img)
        {
            float d = 20 * K;
            float x = box.Left - d / 2, y = box.Top - d / 2;
            if (img.Width >= d && img.Height >= d)
            {
                x = Math.Max(0, Math.Min(img.Width - d, x));
                y = Math.Max(0, Math.Min(img.Height - d, y));
            }
            return new RectangleF(x, y, d, d);
        }

        // Everything that should be visible: the image plus bubbles that stick out of it.
        public static RectangleF Content(List<Shape> shapes, Size img, bool final)
        {
            RectangleF r = new RectangleF(0, 0, img.Width, img.Height);
            foreach (Shape s in shapes)
            {
                if (final && s.Comment.Length == 0) continue;
                r = RectangleF.Union(r, BubbleRect(s, img, false, final));
            }
            return r;
        }

        public static void DrawAll(Graphics g, Bitmap img, Bitmap blur, List<Shape> shapes, Shape live, bool final,
                                   Shape editing, int caret, bool caretOn, Shape hover, Shape selected)
        {
            Size isz = img.Size;
            animate = !final && Fx.On;
            now = Clock.ElapsedMilliseconds;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            List<Shape> all = new List<Shape>(shapes);
            if (live != null) all.Add(live);

            // Spotlight: gently dim everything outside the highlights
            bool anyRect = false;
            using (Region outside = new Region(new RectangleF(0, 0, isz.Width, isz.Height)))
            {
                foreach (Shape s in all)
                    if (s.Kind == Shape.Rect) { anyRect = true; outside.Exclude(s.Box); }
                int rects = 0; Shape only = null;
                foreach (Shape s in all) if (s.Kind == Shape.Rect) { rects++; only = s; }
                float fade = rects == 1 ? EaseOut(Prog(only, 0, 368)) : 1;
                if (anyRect) using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(46 * fade), 11, 11, 12))) g.FillRegion(b, outside);
            }

            foreach (Shape s in all) if (s.Kind == Shape.Rect) DrawHighlight(g, s); else DrawArrow(g, s);

            if (selected != null && selected.Kind == Shape.Arrow && !final) DrawNodes(g, selected);

            int num = 0;
            foreach (Shape s in shapes)
            {
                num++;
                float d = Prog(s, 253, 552);
                if (d <= 0) continue;
                RectangleF br = BadgeFor(s, isz);
                if (d >= 1) { DrawBadge(g, br, s.Color, num); continue; }
                // drops in from just above its corner and settles
                GraphicsState bs = g.Save();
                float cx = br.X + br.Width / 2, cy = br.Y + br.Height / 2, sc = 0.5f + 0.5f * Back(d);
                g.TranslateTransform(cx, cy - (1 - EaseOut(d)) * 12 * K);
                g.ScaleTransform(sc, sc);
                g.TranslateTransform(-cx, -cy);
                DrawBadge(g, br, s.Color, num);
                g.Restore(bs);
            }
            if (live != null) DrawBadge(g, BadgeFor(live, isz), live.Color, num + 1);

            num = 0;
            foreach (Shape s in shapes)
            {
                num++;
                if (final && s.Comment.Length == 0) continue;
                bool ed = s == editing;
                float u = Prog(s, 437, 736);
                if (u <= 0) continue;
                if (u >= 1) { DrawBubble(g, blur, s, isz, num, final, ed, ed && caretOn ? caret : -1, !final && (ed || s == hover)); continue; }
                // unfolds out of its pointer
                RectangleF bub = BubbleRect(s, isz, ed, final);
                RectangleF an = s.Anchor;
                float px = Math.Max(bub.Left, Math.Min(bub.Right, an.X + an.Width / 2));
                float py = Math.Max(bub.Top, Math.Min(bub.Bottom, an.Y + an.Height / 2));
                float bsc = Math.Max(0.05f, Back(u));
                GraphicsState us = g.Save();
                g.TranslateTransform(px, py);
                g.ScaleTransform(bsc, bsc);
                g.TranslateTransform(-px, -py);
                DrawBubble(g, blur, s, isz, num, final, ed, -1, false);
                g.Restore(us);
            }
        }

        public static RectangleF BadgeFor(Shape s, Size img)
        {
            if (s.Kind == Shape.Rect) return BadgeRect(s.Box, img);
            float d = 20 * K;
            return BadgeRect(new RectangleF(s.A.X, s.A.Y, 0, 0), img);
        }

        public static float NodeR { get { return 6 * K; } }

        static void DrawNodes(Graphics g, Shape s)
        {
            PointF[] pts = s.Points();
            using (Pen guide = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, K)))
            {
                guide.DashPattern = new float[] { 3, 3 };
                g.DrawLines(guide, pts);
            }
            for (int i = 0; i < 3; i++)
            {
                PointF c = s.Node(i);
                float r = NodeR * (i == 1 ? 1.15f : 1f);
                RectangleF rr = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
                using (SolidBrush b = new SolidBrush(s.Pinned[i] ? s.Color : Color.White)) g.FillEllipse(b, rr);
                using (Pen p = new Pen(s.Pinned[i] ? Color.White : Ink, Math.Max(1f, 1.5f * K))) g.DrawEllipse(p, rr);
            }
        }

        // The arrow's centre line, flattened into short straight pieces.
        public static PointF[] Flat(Shape s)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddCurve(s.Points(), 0.5f);
                gp.Flatten(new Matrix(), 0.2f * K);
                return gp.PathPoints;
            }
        }

        public static float DistToArrow(Shape s, PointF p)
        {
            PointF[] f = Flat(s);
            float best = float.MaxValue;
            for (int i = 1; i < f.Length; i++)
            {
                PointF a = f[i - 1], b = f[i];
                float vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
                float t = l2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2));
                float dx = a.X + t * vx - p.X, dy = a.Y + t * vy - p.Y;
                best = Math.Min(best, (float)Math.Sqrt(dx * dx + dy * dy));
            }
            return best;
        }

        // Point at a given distance back from the end of a polyline, and everything before it.
        static PointF Back(PointF[] f, float dist, out List<PointF> before)
        {
            before = new List<PointF>();
            float acc = 0;
            for (int i = f.Length - 1; i > 0; i--)
            {
                float vx = f[i].X - f[i - 1].X, vy = f[i].Y - f[i - 1].Y;
                float l = (float)Math.Sqrt(vx * vx + vy * vy);
                if (acc + l >= dist)
                {
                    float t = l == 0 ? 0 : (dist - acc) / l;
                    PointF q = new PointF(f[i].X - vx * t, f[i].Y - vy * t);
                    for (int j = 0; j < i; j++) before.Add(f[j]);
                    before.Add(q);
                    return q;
                }
                acc += l;
            }
            before.Add(f[0]);
            return f[0];
        }

        static void DrawHighlight(Graphics g, Shape s)
        {
            RectangleF r = s.Box;
            float hp = EaseInOut(Prog(s, 0, 368));
            if (hp < 1)
            {
                if (hp <= 0) return;
                PointF[] per = { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top) };
                DrawPartial(g, s, Partial(per, hp));
                return;
            }
            using (GraphicsPath p = Ui.Round(r, 2.5f * K))
            {
                using (Pen halo = new Pen(Color.FromArgb(235, 255, 255, 255), s.W + 3f * K)) { halo.LineJoin = LineJoin.Round; g.DrawPath(halo, p); }
                using (Pen pen = new Pen(s.Color, s.W)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
            }
        }

        static void DrawBadge(Graphics g, RectangleF r, Color c, int n)
        {
            Fonts();
            using (GraphicsPath p = Ui.Round(r, 5 * K))
            {
                using (Pen ring = new Pen(Color.White, 2f * K)) g.DrawPath(ring, p);
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
            }
            DrawCentered(g, n.ToString(), r, OnColor(c));
        }

        static Color OnColor(Color c)
        {
            double lum = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            return lum > 160 ? Ink : Color.White;
        }

        static void DrawCentered(Graphics g, string t, RectangleF r, Color c)
        {
            using (StringFormat f = new StringFormat(StringFormat.GenericTypographic))
            using (SolidBrush b = new SolidBrush(c))
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                RectangleF rr = r; rr.Offset(0, 0.5f * K);
                g.DrawString(t, mono, b, rr, f);
            }
        }

        static void DrawArrow(Graphics g, Shape s)
        {
            PointF[] f = Flat(s);
            float len = 0;
            for (int i = 1; i < f.Length; i++) { float vx = f[i].X - f[i - 1].X, vy = f[i].Y - f[i - 1].Y; len += (float)Math.Sqrt(vx * vx + vy * vy); }
            if (len < 1 || f.Length < 2) return;
            float dp = EaseInOut(Prog(s, 0, 368));
            if (dp < 1) { if (dp > 0) DrawPartial(g, s, Partial(f, dp)); return; }
            float head = Math.Min(len * 0.6f, s.W * 4.5f + 6 * K);
            float half = head * 0.55f;
            List<PointF> dummy, shaft;
            PointF bp = Back(f, head, out dummy);
            Back(f, head * 0.7f, out shaft);
            float dx = s.B.X - bp.X, dy = s.B.Y - bp.Y;
            float hl = (float)Math.Sqrt(dx * dx + dy * dy);
            if (hl < 0.01f) return;
            float ux = dx / hl, uy = dy / hl;
            PointF[] tri = { s.B, new PointF(bp.X - uy * half, bp.Y + ux * half), new PointF(bp.X + uy * half, bp.Y - ux * half) };
            PointF[] sh = shaft.ToArray();
            float hw = 3f * K;
            using (Pen p = new Pen(Color.FromArgb(235, 255, 255, 255), s.W + hw))
            {
                p.StartCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                if (sh.Length >= 2) g.DrawLines(p, sh);
                g.DrawPolygon(p, tri);
            }
            using (Pen p = new Pen(s.Color, s.W))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Flat; p.LineJoin = LineJoin.Round;
                if (sh.Length >= 2) g.DrawLines(p, sh);
            }
            using (SolidBrush b = new SolidBrush(s.Color)) g.FillPolygon(b, tri);
            using (Pen p = new Pen(s.Color, s.W * 0.5f)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, tri); }
        }

        // Rounded rectangle with a speech-bubble pointer on one side, aimed at the highlight.
        static GraphicsPath BubblePath(RectangleF r, RectangleF box)
        {
            float rad = Radius, half = 7 * K, inset = 3 * K;
            int side = 0; float bpos = 0; PointF tip = PointF.Empty;
            if (r.Left >= box.Right + 2 * K) side = 1;
            else if (r.Right <= box.Left - 2 * K) side = 2;
            else if (r.Top >= box.Bottom + 2 * K) side = 3;
            else if (r.Bottom <= box.Top - 2 * K) side = 4;

            if (side == 1 || side == 2)
            {
                float lo = r.Top + rad + half, hi = r.Bottom - rad - half;
                if (hi < lo) side = 0;
                else
                {
                    float cy = (Math.Max(r.Top, box.Top) + Math.Min(r.Bottom, box.Bottom)) / 2;
                    if (Math.Max(r.Top, box.Top) > Math.Min(r.Bottom, box.Bottom)) cy = box.Top + box.Height / 2;
                    bpos = Math.Max(lo, Math.Min(hi, cy));
                    tip = new PointF(side == 1 ? box.Right + inset : box.Left - inset, Math.Max(box.Top + inset, Math.Min(box.Bottom - inset, bpos)));
                }
            }
            else if (side == 3 || side == 4)
            {
                float lo = r.Left + rad + half, hi = r.Right - rad - half;
                if (hi < lo) side = 0;
                else
                {
                    float cx = (Math.Max(r.Left, box.Left) + Math.Min(r.Right, box.Right)) / 2;
                    if (Math.Max(r.Left, box.Left) > Math.Min(r.Right, box.Right)) cx = box.Left + box.Width / 2;
                    bpos = Math.Max(lo, Math.Min(hi, cx));
                    tip = new PointF(Math.Max(box.Left + inset, Math.Min(box.Right - inset, bpos)), side == 3 ? box.Bottom + inset : box.Top - inset);
                }
            }

            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            if (side == 3) p.AddLines(new PointF[] { new PointF(bpos - half, r.Top), tip, new PointF(bpos + half, r.Top) });
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            if (side == 2) p.AddLines(new PointF[] { new PointF(r.Right, bpos - half), tip, new PointF(r.Right, bpos + half) });
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            if (side == 4) p.AddLines(new PointF[] { new PointF(bpos + half, r.Bottom), tip, new PointF(bpos - half, r.Bottom) });
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            if (side == 1) p.AddLines(new PointF[] { new PointF(r.Left, bpos + half), tip, new PointF(r.Left, bpos - half) });
            p.CloseFigure();
            return p;
        }

        static void DrawBubble(Graphics g, Bitmap blur, Shape s, Size isz, int num, bool final, bool editing, int caret, bool showClose)
        {
            Fonts();
            string shown = Shown(s, editing, final);
            bool ph = s.Comment.Length == 0;
            RectangleF r = BubbleRect(s, isz, editing, final);
            List<int[]> lines;
            BubbleSize(shown, out lines);

            using (GraphicsPath path = BubblePath(r, s.Anchor))
            {
                // Soft shadow, outside the bubble only
                GraphicsState st = g.Save();
                using (Region ex = new Region(path)) g.ExcludeClip(ex);
                g.TranslateTransform(0, 2 * K);
                float[] widths = { 10, 6, 3 };
                int[] alphas = { 8, 12, 18 };
                for (int i = 0; i < 3; i++)
                    using (Pen sp = new Pen(Color.FromArgb(alphas[i], 0, 0, 0), widths[i] * K)) { sp.LineJoin = LineJoin.Round; g.DrawPath(sp, path); }
                g.Restore(st);

                // Frosted glass: blurred image behind, white tint on top
                st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                if (blur != null) g.DrawImage(blur, 0, 0, isz.Width, isz.Height);
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(196, 255, 255, 255))) g.FillPath(tint, path);
                g.Restore(st);

                using (Pen bp = new Pen(Color.FromArgb(editing ? 90 : 38, 11, 11, 12), Math.Max(1f, 1f * K))) g.DrawPath(bp, path);
            }

            // Number chip, matching the badge on the highlight
            RectangleF chip = new RectangleF(r.X + Pad, r.Y + Pad, Chip, Chip);
            using (GraphicsPath cp = Ui.Round(chip, 4.5f * K))
            using (SolidBrush cb = new SolidBrush(s.Color)) g.FillPath(cb, cp);
            DrawCentered(g, num.ToString(), chip, OnColor(s.Color));

            // Text
            float tx = chip.Right + Gap;
            float ty = r.Y + Pad + Math.Max(0, (Chip - LineH) / 2);
            using (SolidBrush tb = new SolidBrush(ph ? InkMuted : Ink))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string ln = shown.Substring(lines[i][0], lines[i][1]);
                    g.DrawString(ln, font, tb, new PointF(tx, ty + i * LineH), sf);
                }
            }

            // Caret
            if (caret >= 0)
            {
                float cx = tx, cy = ty;
                if (!ph)
                {
                    int li = 0;
                    for (int i = 0; i < lines.Count; i++) if (lines[i][0] <= caret) li = i;
                    int from = lines[li][0];
                    int cnt = Math.Max(0, Math.Min(caret, from + lines[li][1]) - from);
                    if (caret > from + lines[li][1]) cnt = lines[li][1];
                    cx = tx + Wd(shown.Substring(from, cnt));
                    cy = ty + li * LineH;
                }
                using (Pen cp = new Pen(Ink, Math.Max(1f, 1.25f * K))) g.DrawLine(cp, cx, cy + 1.5f * K, cx, cy + LineH - 1.5f * K);
            }

            // Remove button (editor only)
            if (showClose)
            {
                RectangleF x = CloseRect(r);
                using (GraphicsPath xp = new GraphicsPath())
                {
                    xp.AddEllipse(x);
                    using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, xp);
                    using (Pen lp = new Pen(Color.FromArgb(60, 11, 11, 12), Math.Max(1f, K))) g.DrawPath(lp, xp);
                }
                float m = x.Width * 0.34f;
                using (Pen ip = new Pen(Ink, Math.Max(1f, 1.25f * K)))
                {
                    ip.StartCap = LineCap.Round; ip.EndCap = LineCap.Round;
                    g.DrawLine(ip, x.Left + m, x.Top + m, x.Right - m, x.Bottom - m);
                    g.DrawLine(ip, x.Right - m, x.Top + m, x.Left + m, x.Bottom - m);
                }
            }
        }

        // Cheap, good-looking blur for the frosted glass: shrink a lot, then scale back up smoothly.
        public static Bitmap MakeBlur(Bitmap src)
        {
            int w1 = Math.Max(1, src.Width / 6), h1 = Math.Max(1, src.Height / 6);
            int w2 = Math.Max(1, src.Width / 14), h2 = Math.Max(1, src.Height / 14);
            using (ImageAttributes ia = new ImageAttributes())
            using (Bitmap a = new Bitmap(w1, h1, PixelFormat.Format32bppPArgb))
            using (Bitmap b = new Bitmap(w2, h2, PixelFormat.Format32bppPArgb))
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                using (Graphics g = Graphics.FromImage(a)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(src, new Rectangle(0, 0, w1, h1), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia); }
                using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(a, new Rectangle(0, 0, w2, h2), 0, 0, w1, h1, GraphicsUnit.Pixel, ia); }
                Bitmap outB = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(outB))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(b, new Rectangle(0, 0, src.Width, src.Height), 0, 0, w2, h2, GraphicsUnit.Pixel, ia);
                }
                return outB;
            }
        }
    }

    // Shows the captured image, scaled to fit, and lets you draw highlights, arrows and comments on it.
    class Canvas : Control
    {
        Bitmap img, blur;
        public readonly List<Shape> Shapes = new List<Shape>();
        public int Tool = Shape.Rect;
        public Color PenColor = Color.Black;
        public float StrokeW = 2f;
        public event EventHandler Changed;

        Shape live, editing, hover, dragBubble, selected, dragNodeOf;
        int dragNode = -1;
        int caret;
        bool caretOn = true, moved;
        PointF dragOff;
        Point downAt;
        float zoom = 1f;
        PointF off;
        readonly Timer blink = new Timer();
        readonly Timer anim = new Timer();

        public Canvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = false;
            Cursor = Cursors.Cross;
            blink.Interval = 530;
            blink.Tick += delegate { caretOn = !caretOn; if (editing != null) Invalidate(); };
            anim.Interval = 15;
            anim.Tick += delegate
            {
                long t = Ann.Clock.ElapsedMilliseconds;
                bool any = false;
                foreach (Shape sh in Shapes) if (sh.Born != 0 && t - sh.Born < Ann.AnimMs) any = true;
                if (!any) anim.Stop();
                Invalidate();
            };
        }

        public Bitmap Img
        {
            get { return img; }
            set { img = value; Ann.Scene = Shapes; if (blur != null) blur.Dispose(); blur = img == null ? null : Ann.MakeBlur(img); }
        }
        public Bitmap Blur { get { return blur; } }
        public bool IsEditing { get { return editing != null; } }

        void Fit()
        {
            if (img == null || dragBubble != null) return;   // keep the view still while a bubble is dragged
            RectangleF cb = Ann.Content(Shapes, img.Size, false);
            float pad = Ui.S(16);
            float aw = Math.Max(1, Width - 2 * pad), ah = Math.Max(1, Height - 2 * pad);
            zoom = Math.Min(1f, Math.Min(aw / cb.Width, ah / cb.Height));
            off = new PointF((float)Math.Round((Width - cb.Width * zoom) / 2 - cb.X * zoom), (float)Math.Round((Height - cb.Height * zoom) / 2 - cb.Y * zoom));
        }

        protected override void OnResize(EventArgs e) { Fit(); base.OnResize(e); }

        PointF ToImgRaw(Point p) { return new PointF((p.X - off.X) / zoom, (p.Y - off.Y) / zoom); }
        PointF Clamp(PointF p) { return new PointF(Math.Max(0, Math.Min(img.Width, p.X)), Math.Max(0, Math.Min(img.Height, p.Y))); }

        Shape BubbleAt(PointF p)
        {
            for (int i = Shapes.Count - 1; i >= 0; i--)
            {
                Shape s = Shapes[i];
                if (Ann.BubbleRect(s, img.Size, s == editing, false).Contains(p)) return s;
            }
            return null;
        }

        Shape CloseAt(PointF p)
        {
            for (int i = Shapes.Count - 1; i >= 0; i--)
            {
                Shape s = Shapes[i];
                if (s != editing && s != hover) continue;
                RectangleF x = Ann.CloseRect(Ann.BubbleRect(s, img.Size, s == editing, false));
                x.Inflate(2 * Ann.K, 2 * Ann.K);
                if (x.Contains(p)) return s;
            }
            return null;
        }

        Shape BoxAt(PointF p)
        {
            for (int i = Shapes.Count - 1; i >= 0; i--)
                if (Shapes[i].Kind == Shape.Rect && Shapes[i].Box.Contains(p)) return Shapes[i];
            return null;
        }

        int NodeAt(PointF p)
        {
            if (selected == null || selected.Kind != Shape.Arrow) return -1;
            float r = Math.Max(Ann.NodeR * 1.6f, 8 / zoom);
            for (int i = 1; i >= 0 && i <= 2; i = i == 1 ? 0 : (i == 0 ? 2 : 3))
            {
                PointF c = selected.Node(i);
                if ((c.X - p.X) * (c.X - p.X) + (c.Y - p.Y) * (c.Y - p.Y) <= r * r) return i;
            }
            return -1;
        }

        Shape ArrowAt(PointF p)
        {
            for (int i = Shapes.Count - 1; i >= 0; i--)
            {
                Shape s = Shapes[i];
                if (s.Kind == Shape.Arrow && Ann.DistToArrow(s, p) <= Math.Max(s.W + 4 * Ann.K, 6 / zoom)) return s;
            }
            return null;
        }

        public void BeginEdit(Shape s)
        {
            selected = s;
            editing = s;
            caret = s.Comment.Length;
            caretOn = true;
            blink.Stop(); blink.Start();
            Focus();
            Invalidate();
        }

        public void EndEdit()
        {
            if (editing == null) return;
            editing.Comment = editing.Comment.Trim();
            editing = null;
            blink.Stop();
            Invalidate();
        }

        void Remove(Shape s)
        {
            if (s == editing) { editing = null; blink.Stop(); }
            if (s == hover) hover = null;
            if (s == selected) selected = null;
            Shapes.Remove(s);
            Fit();
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || img == null) return;
            PointF p = ToImgRaw(e.Location);
            Shape x = CloseAt(p);
            if (x != null) { Remove(x); return; }
            downAt = e.Location;
            moved = false;
            int ni = NodeAt(p);
            if (ni >= 0)
            {
                dragNode = ni;
                dragNodeOf = selected;
                Capture = true;
                return;
            }
            Shape b = BubbleAt(p);
            if (b != null)
            {
                dragBubble = b;
                RectangleF br = Ann.BubbleRect(b, img.Size, b == editing, false);
                dragOff = new PointF(p.X - br.X, p.Y - br.Y);
                b.BubblePos = br.Location;
                Capture = true;
                return;
            }
            Shape arr = ArrowAt(p);
            if (arr != null) { BeginEdit(arr); return; }
            EndEdit();
            selected = null;
            live = new Shape();
            live.Kind = Tool; live.Color = PenColor; live.W = StrokeW;
            live.A = live.B = Clamp(p);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (img == null) return;
            PointF p = ToImgRaw(e.Location);
            if (Math.Abs(e.X - downAt.X) > 3 || Math.Abs(e.Y - downAt.Y) > 3) moved = true;
            if (dragNode >= 0)
            {
                if (moved)
                {
                    PointF q = Clamp(p);
                    dragNodeOf.N[dragNode] = q;
                    dragNodeOf.Pinned[dragNode] = true;
                    Invalidate();
                }
                return;
            }
            if (dragBubble != null)
            {
                if (moved) { dragBubble.BubblePos = new PointF(p.X - dragOff.X, p.Y - dragOff.Y); Invalidate(); }
                return;
            }
            if (live != null) { live.B = Clamp(p); Invalidate(); return; }

            Shape h = BubbleAt(p);
            if (h == null) { Shape c = CloseAt(p); if (c != null) h = c; }
            if (h != hover) { hover = h; Invalidate(); }
            if (NodeAt(p) >= 0) Cursor = Cursors.Hand;
            else if (h != null) Cursor = CloseAt(p) != null ? Cursors.Hand : Cursors.SizeAll;
            else Cursor = ArrowAt(p) != null ? Cursors.Hand : Cursors.Cross;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != null && dragBubble == null) { hover = null; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragNode >= 0)
            {
                Shape a = dragNodeOf;
                int i = dragNode;
                dragNode = -1; dragNodeOf = null;
                Capture = false;
                // Clicking a pulled node without dragging lets it go back to following the curve
                if (!moved) a.Pinned[i] = false;
                Fit();
                Invalidate();
                if (editing == null) BeginEdit(a);
                return;
            }
            if (dragBubble != null)
            {
                Shape b = dragBubble;
                dragBubble = null;
                Capture = false;
                // Keep the stored position equal to where it is drawn
                b.BubblePos = Ann.BubbleRect(b, img.Size, b == editing, false).Location;
                Fit();
                if (!moved) BeginEdit(b); else Invalidate();
                return;
            }
            if (live == null) return;
            Shape s = live;
            live = null;
            Capture = false;
            float dx = (s.B.X - s.A.X) * zoom, dy = (s.B.Y - s.A.Y) * zoom;
            if (Math.Sqrt(dx * dx + dy * dy) >= 6 && (s.Kind == Shape.Arrow || (Math.Abs(dx) >= 4 && Math.Abs(dy) >= 4)))
            {
                if (Fx.On) { s.Born = Ann.Clock.ElapsedMilliseconds + 1; anim.Start(); }
                Shapes.Add(s);
                if (Changed != null) Changed(this, EventArgs.Empty);
                BeginEdit(s);
                Fit();
            }
            else
            {
                // A click inside an existing highlight opens its comment
                Shape box = BoxAt(s.A);
                if (box != null) BeginEdit(box);
            }
            Invalidate();
        }

        public void Undo()
        {
            if (Shapes.Count == 0) return;
            Remove(Shapes[Shapes.Count - 1]);
        }

        // ---- typing into the bubble being edited ----

        protected override bool IsInputKey(Keys keyData)
        {
            if (editing == null) return base.IsInputKey(keyData);
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.Delete: case Keys.Back: case Keys.Enter:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        void Insert(string t)
        {
            t = t.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            editing.Comment = editing.Comment.Insert(caret, t);
            caret += t.Length;
            Typed();
        }

        void Typed()
        {
            caretOn = true; blink.Stop(); blink.Start();
            Fit();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (editing == null) { base.OnKeyDown(e); return; }
            string c = editing.Comment;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    if (e.Shift) Insert("\n"); else EndEdit();
                    break;
                case Keys.Back:
                    if (caret > 0)
                    {
                        int from = caret - 1;
                        if (e.Control) { from = caret; while (from > 0 && c[from - 1] == ' ') from--; while (from > 0 && c[from - 1] != ' ' && c[from - 1] != '\n') from--; }
                        editing.Comment = c.Remove(from, caret - from); caret = from; Typed();
                    }
                    break;
                case Keys.Delete:
                    if (caret < c.Length) { editing.Comment = c.Remove(caret, 1); Typed(); }
                    break;
                case Keys.Left: if (caret > 0) caret--; Typed(); break;
                case Keys.Right: if (caret < c.Length) caret++; Typed(); break;
                case Keys.Home: caret = 0; Typed(); break;
                case Keys.End: caret = c.Length; Typed(); break;
                case Keys.V:
                    if (e.Control) { try { if (Clipboard.ContainsText()) Insert(Clipboard.GetText()); } catch { } }
                    else handled = false;
                    break;
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
            else base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (editing != null && e.KeyChar >= ' ' && e.KeyChar != (char)127)
            {
                Insert(e.KeyChar.ToString());
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            EndEdit();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Sunken);
            if (img == null) return;
            Fit();
            RectangleF dest = new RectangleF(off.X, off.Y, img.Width * zoom, img.Height * zoom);
            g.InterpolationMode = zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, dest);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (Pen p = new Pen(Theme.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 11, 11, 12)))
                g.DrawRectangle(p, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);

            GraphicsState st = g.Save();
            g.TranslateTransform(off.X, off.Y);
            g.ScaleTransform(zoom, zoom);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            Ann.DrawAll(g, img, blur, Shapes, live, false, editing, caret, caretOn, hover, selected);
            g.Restore(st);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { blink.Dispose(); anim.Dispose(); if (blur != null) blur.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
