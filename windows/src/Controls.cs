using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapNote
{
    // Linework Button / Chip (.lw-btn, .lw-chip)
    class LwButton : Control
    {
        public enum Kinds { Primary, Secondary, Ghost, Chip }
        public Kinds Kind = Kinds.Secondary;
        public string Hint;
        public Ui.IconFn Icon;
        bool pressed, hover, down;

        public LwButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }

        public bool Pressed { get { return pressed; } set { pressed = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        Font TextFont { get { return Kind == Kinds.Chip && !pressed ? Ui.Sans(13) : Ui.Medium(13); } }
        int Pad { get { return Kind == Kinds.Chip ? Ui.S(10) : Ui.S(12); } }

        int ContentWidth(Font f)
        {
            int w = 0;
            if (Icon != null) w += Ui.S(16);
            if (!string.IsNullOrEmpty(Text))
            {
                if (w > 0) w += Ui.S(6);
                w += TextRenderer.MeasureText(Text, f, Size.Empty, Ui.TextFlags).Width;
            }
            if (!string.IsNullOrEmpty(Hint)) w += Ui.S(8) + TextRenderer.MeasureText(Hint, Ui.Mono(11), Size.Empty, Ui.TextFlags).Width;
            return w;
        }

        public int PreferredWidth() { return ContentWidth(Ui.Medium(13)) + Pad * 2; }

        void Colors(Color pb, out Color bg, out Color fg, out Color hint)
        {
            switch (Kind)
            {
                case Kinds.Primary:
                    bg = hover ? Theme.Mix(Theme.Ink, pb, 0.12f) : Theme.Ink; fg = Theme.OnPrimary; hint = Theme.Mix(Theme.OnPrimary, bg, 0.45f); break;
                case Kinds.Secondary:
                    bg = hover ? Theme.Hover : Theme.Surface; fg = Theme.Ink; hint = Theme.InkFaint; break;
                case Kinds.Ghost:
                    bg = hover ? Theme.Hover : pb; fg = hover ? Theme.Ink : Theme.InkMuted; hint = Theme.InkFaint; break;
                default:
                    bg = pressed ? Theme.Sunken : (hover ? Theme.Hover : pb);
                    fg = pressed || hover ? Theme.Ink : Theme.InkMuted; hint = Theme.InkFaint; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color pb = Parent != null ? Parent.BackColor : Theme.Surface;
            g.Clear(pb);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, hc;
            Colors(pb, out bg, out fg, out hc);

            float inset = down && Enabled ? 0.8f * Ui.Scale : 0f;   // press: subtle shrink, like scale(.97)
            RectangleF rr = new RectangleF(0.5f + inset, 0.5f + inset, Width - 1 - 2 * inset, Height - 1 - 2 * inset);
            float rad = Kind == Kinds.Chip ? Ui.S(6) : Ui.S(8);
            using (GraphicsPath path = Ui.Round(rr, rad))
            {
                if (bg != pb) using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, path);
                if (Kind == Kinds.Secondary)
                    using (Pen p = new Pen(Theme.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(34, 11, 11, 12)))
                        g.DrawPath(p, path);
            }

            Font f = TextFont;
            int x = (Width - ContentWidth(f)) / 2;
            int cy = Height / 2;
            if (Icon != null)
            {
                Color ic = Kind == Kinds.Chip && !pressed && !hover ? Theme.InkFaint : fg;
                Icon(g, new RectangleF(x, cy - Ui.S(8), Ui.S(16), Ui.S(16)), ic);
                x += Ui.S(16) + (string.IsNullOrEmpty(Text) ? 0 : Ui.S(6));
            }
            if (!string.IsNullOrEmpty(Text))
            {
                Size sz = TextRenderer.MeasureText(Text, f, Size.Empty, Ui.TextFlags);
                TextRenderer.DrawText(g, Text, f, new Point(x, cy - sz.Height / 2), Enabled ? fg : Theme.InkFaint, bg, Ui.TextFlags);
                x += sz.Width;
            }
            if (!string.IsNullOrEmpty(Hint))
            {
                x += Ui.S(8);
                Size sz = TextRenderer.MeasureText(Hint, Ui.Mono(11), Size.Empty, Ui.TextFlags);
                TextRenderer.DrawText(g, Hint, Ui.Mono(11), new Point(x, cy - sz.Height / 2 + 1), hc, bg, Ui.TextFlags);
            }
        }
    }

    // Linework Switch (.lw-switch)
    class LwSwitch : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public LwSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        public int PreferredWidth() { return Ui.S(38) + TextRenderer.MeasureText(Text ?? "", Ui.Sans(13), Size.Empty, Ui.TextFlags).Width + 2; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color pb = Parent != null ? Parent.BackColor : Theme.Surface;
            g.Clear(pb);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Ui.S(18), w = Ui.S(30);
            RectangleF track = new RectangleF(0.5f, (Height - h) / 2f, w, h);
            using (GraphicsPath path = Ui.Round(track, h / 2))
            {
                using (SolidBrush b = new SolidBrush(on ? Theme.Ink : Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(on ? Theme.Ink : Theme.LineControl)) g.DrawPath(p, path);
            }
            float t = Ui.S(12);
            float tx = track.X + (on ? Ui.S(15) : Ui.S(3));
            using (SolidBrush b = new SolidBrush(on ? Theme.OnPrimary : Theme.InkMuted))
                g.FillEllipse(b, tx, track.Y + (h - t) / 2f, t, t);
            Size sz = TextRenderer.MeasureText(Text ?? "", Ui.Sans(13), Size.Empty, Ui.TextFlags);
            TextRenderer.DrawText(g, Text ?? "", Ui.Sans(13), new Point((int)w + Ui.S(8), (Height - sz.Height) / 2), Theme.Ink, pb, Ui.TextFlags);
        }
    }

    // Annotation color swatch
    class ColorDot : Control
    {
        public Color Swatch;
        bool selected, hover;

        public ColorDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Ui.S(12);
            float cx = Width / 2f, cy = Height / 2f;
            using (SolidBrush b = new SolidBrush(Swatch)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            if (selected || hover)
            {
                float r = Ui.S(18);
                using (Pen p = new Pen(selected ? Theme.Ink : Theme.LineControl, 1.25f * Ui.Scale))
                    g.DrawEllipse(p, cx - r / 2, cy - r / 2, r, r);
            }
        }
    }

    // Multi-line TextBox with placeholder text
    class CueTextBox : TextBox
    {
        public string Cue;
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && TextLength == 0 && !string.IsNullOrEmpty(Cue))
            {
                using (Graphics g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, Cue, Font, ClientRectangle, Theme.InkFaint, BackColor,
                        TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    // Linework Input (.lw-input) wrapping a borderless TextBox
    class NoteBox : Control
    {
        public readonly CueTextBox Box;

        public NoteBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new CueTextBox();
            Box.Multiline = true;
            Box.AcceptsReturn = true;
            Box.WordWrap = true;
            Box.BorderStyle = BorderStyle.None;
            Box.ScrollBars = ScrollBars.None;
            Box.BackColor = Theme.Surface;
            Box.ForeColor = Theme.Ink;
            Box.Font = Ui.Sans(14);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Box.Bounds = new Rectangle(Ui.S(12), Ui.S(9), Width - Ui.S(24), Height - Ui.S(18));
            base.OnLayout(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(8)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Box.Focused ? Theme.Ink : Theme.LineControl)) g.DrawPath(p, path);
            }
        }
    }

    // Tray menu in Linework colors
    class LwMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Surface); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Theme.Hover))
            using (GraphicsPath p = Ui.Round(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), Ui.S(6)))
                e.Graphics.FillPath(b, p);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.InkFaint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }
    }
}
