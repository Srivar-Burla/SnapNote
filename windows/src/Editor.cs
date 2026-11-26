using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace SnapNote
{
    // Annotate + comment window.
    class Editor : Form
    {
        readonly Bitmap img;
        readonly float capScale;
        readonly Canvas canvas;
        readonly Panel top, bottom;
        readonly NoteBox note;
        readonly LwSwitch burn;
        readonly LwButton btnChat, btnImg, btnSave, btnClose, chipRect, chipArrow, btnUndo;
        readonly List<ColorDot> dots = new List<ColorDot>();
        bool lastWasShape;

        public Editor(Bitmap image, Rectangle srcScreen)
        {
            img = image;
            Point c = new Point(srcScreen.X + srcScreen.Width / 2, srcScreen.Y + srcScreen.Height / 2);
            capScale = Native.DpiScaleAt(c.X, c.Y);
            Ui.Scale = capScale;

            Text = "SnapNote";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            ShowInTaskbar = true;
            BackColor = Theme.Line;            // shows as a 1px border around the window
            Padding = new Padding(1);
            DoubleBuffered = true;
            Icon = Icons.App();

            canvas = new Canvas();
            Ann.K = capScale;
            canvas.Img = img;
            canvas.Dock = DockStyle.Fill;
            canvas.PenColor = Program.Cfg.AnnotationColor;
            canvas.StrokeW = Math.Max(1.5f, 2f * capScale);
            canvas.Changed += delegate { lastWasShape = canvas.Shapes.Count > 0; btnUndo.Enabled = canvas.Shapes.Count > 0; btnUndo.Invalidate(); };

            // Top bar: brand, tools, colours, undo, close
            top = new DragPanel(this);
            top.Dock = DockStyle.Top;
            top.Height = Ui.S(48);
            top.BackColor = Theme.Surface;
            top.Paint += PaintTop;

            chipRect = Chip("Highlight", Ui.IconRect);
            chipArrow = Chip("Arrow", Ui.IconArrow);
            chipRect.Pressed = true;
            chipRect.Click += delegate { SetTool(Shape.Rect); };
            chipArrow.Click += delegate { SetTool(Shape.Arrow); };
            btnUndo = Chip("Undo", Ui.IconUndo);
            btnUndo.Enabled = false;
            btnUndo.Click += delegate { Undo(); };
            List<Color> swatches = new List<Color>();
            swatches.Add(Program.Cfg.AnnotationColor);
            foreach (string hx in new string[] { "#0b0b0c", "#2b59c3", "#e5484d" })
            {
                Color sw = Theme.H(hx);
                if (swatches.Count < 3 && sw.ToArgb() != Program.Cfg.AnnotationColor.ToArgb()) swatches.Add(sw);
            }
            foreach (Color col in swatches)
            {
                ColorDot d = new ColorDot();
                d.Swatch = col;
                d.Size = new Size(Ui.S(26), Ui.S(28));
                d.Selected = dots.Count == 0;
                d.Click += DotClick;
                dots.Add(d);
                top.Controls.Add(d);
            }
            btnClose = new LwButton();
            btnClose.Kind = LwButton.Kinds.Ghost;
            btnClose.Icon = Ui.IconClose;
            btnClose.Size = new Size(Ui.S(28), Ui.S(28));
            btnClose.Click += delegate { Close(); };
            top.Controls.AddRange(new Control[] { chipRect, chipArrow, btnUndo, btnClose });
            top.Resize += delegate { LayoutTop(); };

            // Bottom: note, switch, buttons
            bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Ui.S(152);
            bottom.BackColor = Theme.Surface;
            bottom.Paint += PaintBottom;

            note = new NoteBox();
            note.Box.Cue = "Your overall question (optional). For specific spots, draw a highlight and type in its bubble.";
            note.Box.TextChanged += delegate { lastWasShape = false; };
            burn = new LwSwitch();
            burn.Text = "Put note inside the image";
            burn.Checked = Program.Cfg.NoteInImage;
            btnSave = new LwButton();
            btnSave.Kind = LwButton.Kinds.Secondary;
            btnSave.Text = "Save";
            btnSave.Click += delegate { DoSave(); };
            btnImg = new LwButton();
            btnImg.Kind = LwButton.Kinds.Secondary;
            btnImg.Text = "Copy image only";
            btnImg.Click += delegate { DoCopy(false); };
            btnChat = new LwButton();
            btnChat.Kind = LwButton.Kinds.Primary;
            btnChat.Text = "Copy for chat";
            btnChat.Hint = "Ctrl+Enter";
            btnChat.Click += delegate { DoCopy(true); };
            bottom.Controls.AddRange(new Control[] { note, burn, btnSave, btnImg, btnChat });
            bottom.Resize += delegate { LayoutBottom(); };

            Controls.Add(canvas);   // added first so it fills what the bars leave
            Controls.Add(top);
            Controls.Add(bottom);

            // Size the window around the image (1:1 when it fits)
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int chromeH = top.Height + bottom.Height + 2;
            int padC = Ui.S(16) * 2;
            float z = Math.Min(1f, Math.Min((wa.Width * 0.92f - padC) / img.Width, (wa.Height * 0.92f - chromeH - padC) / img.Height));
            int w = Math.Max(Ui.S(760), (int)(img.Width * z) + padC + 2);
            int h = Math.Max(chromeH + Ui.S(180), (int)(img.Height * z) + padC + chromeH);
            w = Math.Min(w, wa.Width); h = Math.Min(h, wa.Height);
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }

        LwButton Chip(string text, Ui.IconFn icon)
        {
            LwButton b = new LwButton();
            b.Kind = LwButton.Kinds.Chip;
            b.Text = text;
            b.Icon = icon;
            b.Size = new Size(b.PreferredWidth(), Ui.S(28));
            return b;
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }   // drop shadow
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle, Theme.Line);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
            note.Box.Focus();
        }

        public void BringUp()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            note.Box.Focus();
        }

        void LayoutTop()
        {
            int cy = (top.Height - Ui.S(28)) / 2;
            string brand = "SnapNote";
            int x = Ui.S(16 + 24 + 8) + TextRenderer.MeasureText(brand, Ui.Medium(15), Size.Empty, Ui.TextFlags).Width
                + Ui.S(8) + TextRenderer.MeasureText("v" + Program.Version, Ui.Mono(11), Size.Empty, Ui.TextFlags).Width + Ui.S(24);
            chipRect.Location = new Point(x, cy); x += chipRect.Width + Ui.S(4);
            chipArrow.Location = new Point(x, cy); x += chipArrow.Width + Ui.S(14);
            sepX = x - Ui.S(7);
            foreach (ColorDot d in dots) { d.Location = new Point(x, cy); x += d.Width; }
            x += Ui.S(10);
            btnUndo.Location = new Point(x, cy);
            btnClose.Location = new Point(top.Width - Ui.S(12) - btnClose.Width, cy);
            tagRight = btnClose.Left - Ui.S(12);
            tagLeft = btnUndo.Right + Ui.S(24);
            top.Invalidate();
        }
        int sepX, tagLeft, tagRight;
        public const string Tagline = "Snap it, mark it, share it with AI or save it.";

        void PaintTop(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            string brand = "SnapNote";
            Size b = TextRenderer.MeasureText(brand, Ui.Medium(15), Size.Empty, Ui.TextFlags);
            int y = (top.Height - b.Height) / 2;
            // The SnapNote logo, then the name and version
            int lz = Ui.S(24);
            Logo.Draw(g, new RectangleF(Ui.S(16), (top.Height - lz) / 2f, lz, lz), 1f, false, Theme.Dark);
            int bx = Ui.S(16 + 24 + 8);
            TextRenderer.DrawText(g, brand, Ui.Medium(15), new Point(bx, y), Theme.Ink, Theme.Surface, Ui.TextFlags);
            Size v = TextRenderer.MeasureText("v" + Program.Version, Ui.Mono(11), Size.Empty, Ui.TextFlags);
            TextRenderer.DrawText(g, "v" + Program.Version, Ui.Mono(11), new Point(bx + b.Width + Ui.S(8), y + b.Height - v.Height - 1), Theme.InkMuted, Theme.Surface, Ui.TextFlags);
            using (Pen p = new Pen(Theme.Line))
            {
                g.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1);
                g.DrawLine(p, sepX, Ui.S(16), sepX, top.Height - Ui.S(16));
            }
            Size t = TextRenderer.MeasureText(Tagline, Ui.Sans(12), Size.Empty, Ui.TextFlags);
            if (tagRight - t.Width > tagLeft)
                TextRenderer.DrawText(g, Tagline, Ui.Sans(12), new Point(tagRight - t.Width, (top.Height - t.Height) / 2), Theme.InkMuted, Theme.Surface, Ui.TextFlags);
        }

        void LayoutBottom()
        {
            int pad = Ui.S(16);
            note.Bounds = new Rectangle(pad, pad, bottom.Width - 2 * pad, Ui.S(76));
            int rowY = note.Bottom + Ui.S(12);
            int rowH = Ui.S(34);
            burn.Bounds = new Rectangle(pad, rowY, burn.PreferredWidth(), rowH);
            btnChat.Size = new Size(btnChat.PreferredWidth(), rowH);
            btnChat.Location = new Point(bottom.Width - pad - btnChat.Width, rowY);
            btnImg.Size = new Size(btnImg.PreferredWidth(), rowH);
            btnImg.Location = new Point(btnChat.Left - Ui.S(8) - btnImg.Width, rowY);
            btnSave.Size = new Size(btnSave.PreferredWidth(), rowH);
            btnSave.Location = new Point(btnImg.Left - Ui.S(8) - btnSave.Width, rowY);
            bottom.Invalidate();
        }

        void PaintBottom(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, 0, 0, bottom.Width, 0);
            string hint = "Click an arrow to bend it  ·  Esc to discard";
            Size sz = TextRenderer.MeasureText(hint, Ui.Mono(11), Size.Empty, Ui.TextFlags);
            int x = btnSave.Left - Ui.S(16) - sz.Width;
            if (x > burn.Right + Ui.S(16))
                TextRenderer.DrawText(g, hint, Ui.Mono(11), new Point(x, btnImg.Top + (btnImg.Height - sz.Height) / 2), Theme.InkFaint, Theme.Surface, Ui.TextFlags);
        }

        void SetTool(int t)
        {
            canvas.Tool = t;
            chipRect.Pressed = t == Shape.Rect;
            chipArrow.Pressed = t == Shape.Arrow;
        }

        void DotClick(object sender, EventArgs e)
        {
            foreach (ColorDot d in dots) d.Selected = d == sender;
            canvas.PenColor = ((ColorDot)sender).Swatch;
        }

        void Undo()
        {
            canvas.Undo();
            btnUndo.Enabled = canvas.Shapes.Count > 0;
            btnUndo.Invalidate();
            lastWasShape = canvas.Shapes.Count > 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Enter: DoCopy(true); return true;
                case Keys.Control | Keys.Shift | Keys.C: DoCopy(false); return true;
                case Keys.Control | Keys.S: DoSave(); return true;
                case Keys.Escape:
                    if (canvas.IsEditing) canvas.EndEdit(); else Close();
                    return true;
                case Keys.Control | Keys.Z:
                    if (canvas.IsEditing) return true;
                    if (lastWasShape || note.Box.TextLength == 0) { Undo(); return true; }
                    break;
                case Keys.Alt | Keys.B: SetTool(Shape.Rect); return true;
                case Keys.Alt | Keys.A: SetTool(Shape.Arrow); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Image with highlights, arrows and comment bubbles; optionally with the note as a caption strip under it.
        // If a bubble sticks out past the edge of a small capture, the image grows to make room for it.
        public Bitmap Render(string caption)
        {
            canvas.EndEdit();
            float k = capScale;
            RectangleF cb = Ann.Content(canvas.Shapes, img.Size, true);
            Rectangle area = new Rectangle(0, 0, img.Width, img.Height);
            if (cb.Left < 0 || cb.Top < 0 || cb.Right > img.Width || cb.Bottom > img.Height)
            {
                cb.Inflate(12 * k, 12 * k);
                area = Rectangle.FromLTRB((int)Math.Floor(Math.Min(0, cb.Left)), (int)Math.Floor(Math.Min(0, cb.Top)),
                    (int)Math.Ceiling(Math.Max(img.Width, cb.Right)), (int)Math.Ceiling(Math.Max(img.Height, cb.Bottom)));
            }
            Bitmap annotated = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(annotated))
            {
                g.Clear(Theme.H("#f5f5f6"));
                g.TranslateTransform(-area.X, -area.Y);
                g.DrawImage(img, 0, 0, img.Width, img.Height);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                Ann.DrawAll(g, img, canvas.Blur, canvas.Shapes, null, true, null, -1, false, null, null);
            }
            if (string.IsNullOrEmpty(caption)) return annotated;

            using (Font f = new Font(Ui.SansName, 15 * k, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int pad = (int)(16 * k);
                int aw = annotated.Width, ah = annotated.Height;
                int W = Math.Max(aw, (int)(480 * k));
                SizeF ts;
                using (Graphics mg = Graphics.FromImage(annotated)) ts = mg.MeasureString(caption, f, W - 2 * pad);
                int H = ah + pad * 2 + (int)Math.Ceiling(ts.Height);
                Bitmap outB = new Bitmap(W, H, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(outB))
                {
                    g.Clear(Color.White);
                    if (W > aw) using (SolidBrush b = new SolidBrush(Theme.H("#f5f5f6"))) g.FillRectangle(b, aw, 0, W - aw, ah);
                    g.DrawImageUnscaled(annotated, 0, 0);
                    using (SolidBrush b = new SolidBrush(Theme.H("#e8e8eb"))) g.FillRectangle(b, 0, ah, W, Math.Max(1, (int)k));
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (SolidBrush b = new SolidBrush(Theme.H("#0b0b0c")))
                        g.DrawString(caption, f, b, new RectangleF(pad, ah + pad, W - 2 * pad, ts.Height + pad));
                }
                annotated.Dispose();
                return outB;
            }
        }

        // The text that goes into the chat box: the overall note, then each bubble's comment by its number.
        public string ChatText(string noteText)
        {
            canvas.EndEdit();
            List<string> items = new List<string>();
            int num = 0;
            foreach (Shape s in canvas.Shapes)
            {
                num++;
                string c = s.Comment.Trim();
                if (c.Length > 0) items.Add(num + ". " + c.Replace("\n", "\n   "));
            }
            if (items.Count == 0) return noteText;
            string head = items.Count == 1 ? "About the marked spot in the screenshot:" : "About the numbered areas in the screenshot:";
            string list = head + "\n" + string.Join("\n", items.ToArray());
            return noteText.Length > 0 ? noteText + "\n\n" + list : list;
        }

        void DoCopy(bool forChat)
        {
            string noteOnly = note.Box.Text.Trim();
            // Bubble comments are always drawn on the image. The pasted text also lists them by number,
            // unless the note is being put inside the image (then everything is in the picture).
            bool inImage = forChat && burn.Checked && noteOnly.Length > 0;
            string n = inImage ? noteOnly : ChatText(noteOnly);
            string title, body;
            Bitmap snap = null;
            using (Bitmap final = Render(inImage ? n : null))
            {
                bool ok;
                if (!forChat || n.Length == 0)
                {
                    ok = Clip.Put(final, null);
                    title = "Screenshot copied"; body = "Paste it with Ctrl+V.";
                }
                else if (inImage)
                {
                    ok = Clip.Put(final, null);
                    title = "Screenshot and note copied"; body = "Your note is inside the image. Paste it with Ctrl+V.";
                }
                else if (Program.Cfg.SmartPaste && (ok = Clip.Put(final, null)) && SmartPaste.Arm(n))
                {
                    title = "Ready to paste";
                    body = "Click your chat box and press Ctrl+V once. The screenshot goes in first, then your note.";
                }
                else
                {
                    ok = Clip.Put(final, n);
                    title = "Copied";
                    body = "The image and your note are both on the clipboard. Some chat apps paste only one of them.";
                }
                if (!ok)
                {
                    Toast.Show("Could not copy", "Another app is holding the clipboard. Try again.", 4000, true);
                    return;
                }
                Save(final, n);
                snap = new Bitmap(final);
            }
            if (Program.Cfg.NoteInImage != burn.Checked)
            {
                Program.Cfg.NoteInImage = burn.Checked;
                Program.Cfg.Set("NoteInImage", burn.Checked ? "true" : "false");
            }
            Rectangle from = Bounds;
            Close();
            // The screenshot folds into a paper plane and flies to the corner, where the toast appears.
            Fx.Play(Fx.Kind.Plane, snap, from, delegate { Toast.Show(title, body, 5000, false); });
            snap.Dispose();
        }

        // Copies also get saved when AlsoSaveOnCopy=true.
        void Save(Bitmap final, string noteText)
        {
            if (!Program.Cfg.AlsoSaveOnCopy) return;
            string dummy;
            WriteFiles(final, noteText, out dummy);
        }

        static bool WriteFiles(Bitmap final, string noteText, out string pngPath)
        {
            pngPath = null;
            try
            {
                string folder = Program.SaveDir();
                Directory.CreateDirectory(folder);
                string stem = Path.Combine(folder, "SnapNote " + DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
                final.Save(stem + ".png", ImageFormat.Png);
                if (noteText.Length > 0) File.WriteAllText(stem + ".txt", noteText);
                pngPath = stem + ".png";
                return true;
            }
            catch { return false; }
        }

        // Save button: the marked-up image (with bubbles) as a PNG, plus a .txt with the note and comments.
        void DoSave()
        {
            string noteOnly = note.Box.Text.Trim();
            bool inImage = burn.Checked && noteOnly.Length > 0;
            string text = ChatText(noteOnly);
            string path;
            bool ok;
            Bitmap snap = null;
            using (Bitmap final = Render(inImage ? noteOnly : null))
            {
                ok = WriteFiles(final, text, out path);
                if (ok) snap = new Bitmap(final);
            }
            if (!ok)
            {
                Toast.Show("Could not save", "Check the SaveFolder line in settings.ini.", 5000, true);
                return;
            }
            Rectangle from = Bounds;
            Close();
            // The screenshot drops into a little tray, then the toast says where it went.
            string msg = Path.GetFileName(path) + (text.Length > 0 ? " and its notes" : "") + " in " + Program.SaveDir();
            Fx.Play(Fx.Kind.Tray, snap, from, delegate { Toast.Show("Saved", msg, 5000, false); });
            snap.Dispose();
        }

        // Self-test: two highlights with comments, one arrow, and a note.
        public void TestSetup(string text)
        {
            float w = img.Width, h = img.Height;
            Shape r1 = new Shape(); r1.Kind = Shape.Rect; r1.Color = canvas.PenColor; r1.W = canvas.StrokeW;
            r1.A = new PointF(w * 0.06f, h * 0.08f); r1.B = new PointF(w * 0.36f, h * 0.30f);
            r1.Comment = "This header wraps onto two lines at 125% scaling. Can it stay on one?";
            Shape r2 = new Shape(); r2.Kind = Shape.Rect; r2.Color = canvas.PenColor; r2.W = canvas.StrokeW;
            r2.A = new PointF(w * 0.55f, h * 0.62f); r2.B = new PointF(w * 0.80f, h * 0.84f);
            r2.Comment = "Why is this greyed out?";
            Shape a = new Shape(); a.Kind = Shape.Arrow; a.Color = canvas.PenColor; a.W = canvas.StrokeW;
            a.A = new PointF(w * 0.30f, h * 0.70f); a.B = new PointF(w * 0.52f, h * 0.72f);
            a.N[1] = new PointF(w * 0.40f, h * 0.55f); a.Pinned[1] = true;      // one curve
            a.Comment = "This should point here instead";
            Shape a2 = new Shape(); a2.Kind = Shape.Arrow; a2.Color = canvas.PenColor; a2.W = canvas.StrokeW;
            a2.A = new PointF(w * 0.10f, h * 0.92f); a2.B = new PointF(w * 0.45f, h * 0.92f);
            a2.N[0] = new PointF(w * 0.18f, h * 0.80f); a2.Pinned[0] = true;    // double bend
            canvas.Shapes.Add(r1); canvas.Shapes.Add(r2); canvas.Shapes.Add(a); canvas.Shapes.Add(a2);
            btnUndo.Enabled = true;
            note.Box.Text = text;
        }

        public string TestChatText() { return ChatText(note.Box.Text.Trim()); }

        public void TestEditFirst() { foreach (Shape s in canvas.Shapes) if (s.Kind == Shape.Arrow) { canvas.BeginEdit(s); break; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) img.Dispose();
            base.Dispose(disposing);
        }

        // Top bar you can drag the window by
        class DragPanel : Panel
        {
            readonly Form owner;
            public DragPanel(Form f)
            {
                owner = f;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Native.ReleaseCapture();
                Native.SendMessage(owner.Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            }
        }
    }
}
