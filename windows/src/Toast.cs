using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapNote
{
    // Small Linework card in the bottom-right corner. Never takes focus.
    class Toast : Form
    {
        static Toast current;
        readonly string title, body;
        readonly bool error;
        readonly Timer timer = new Timer();
        Rectangle bodyRect;

        public static void Show(string title, string body, int ms, bool error)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new Toast(title, body, ms, error);
            current.Show();
        }

        Toast(string title, string body, int ms, bool error)
        {
            this.title = title; this.body = body; this.error = error;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Ui.Scale = Native.DpiScaleAt(wa.X + wa.Width / 2, wa.Y + wa.Height / 2);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            int w = Ui.S(340);
            int textW = w - Ui.S(16 + 20 + 16);
            Size bs = TextRenderer.MeasureText(body ?? "", Ui.Sans(13), new Size(textW, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int h = Ui.S(16) + Ui.S(20) + Ui.S(4) + bs.Height + Ui.S(16);
            bodyRect = new Rectangle(Ui.S(36), Ui.S(16 + 20 + 4), textW, bs.Height + 2);
            Bounds = new Rectangle(wa.Right - w - Ui.S(16), wa.Bottom - h - Ui.S(16), w, h);

            timer.Interval = ms;
            timer.Tick += delegate { Close(); };
            timer.Start();
            Click += delegate { Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x8;   // no-activate, tool window, topmost
                cp.ClassStyle |= 0x20000;                 // drop shadow
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle, Theme.Line);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Theme.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (error)
            {
                float d = Ui.S(6);
                using (SolidBrush b = new SolidBrush(Theme.Danger))
                    g.FillEllipse(b, Ui.S(18), Ui.S(16) + (Ui.S(20) - d) / 2f, d, d);
            }
            else Logo.Draw(g, new RectangleF(Ui.S(12), Ui.S(16), Ui.S(18), Ui.S(18)), 1f, false, Theme.Dark);
            TextRenderer.DrawText(g, title, Ui.Medium(14), new Point(Ui.S(36), Ui.S(16)), Theme.Ink, Theme.Surface, Ui.TextFlags);
            TextRenderer.DrawText(g, body ?? "", Ui.Sans(13), bodyRect, Theme.InkMuted, Theme.Surface, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
