using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SnapNote
{
    // SnapNote.exe --selftest <folder>
    // Renders the overlay, editor and final images to PNGs without user input. For development only.
    static class SelfTest
    {
        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            Rectangle vs = SystemInformation.VirtualScreen;
            Bitmap shot = new Bitmap(vs.Width, vs.Height);
            using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(vs.X, vs.Y, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            List<Rectangle> wins = Overlay.VisibleWindows(vs);
            File.WriteAllText(Path.Combine(outDir, "windows.txt"), "virtual=" + vs + "\r\n" + string.Join("\r\n", wins.ConvertAll(r => r.ToString()).ToArray()));

            using (Overlay ov = new Overlay(shot, vs, wins))
            {
                Rectangle hv = wins.Count > 0 ? wins[0] : new Rectangle(100, 100, 600, 400);
                using (Bitmap b = ov.Preview(hv)) b.Save(Path.Combine(outDir, "overlay.png"), ImageFormat.Png);
            }

            Rectangle sel = new Rectangle(Math.Min(200, vs.Width / 4), Math.Min(150, vs.Height / 4), Math.Min(900, vs.Width / 2), Math.Min(560, vs.Height / 2));
            Bitmap crop = shot.Clone(sel, PixelFormat.Format32bppArgb);
            shot.Dispose();
            Rectangle screenRect = new Rectangle(sel.X + vs.X, sel.Y + vs.Y, sel.Width, sel.Height);
            Editor ed = new Editor(crop, screenRect);
            ed.TestSetup("Why is this button greyed out? I filled in every field above.");
            ed.Show();
            Application.DoEvents();
            using (Bitmap b = new Bitmap(ed.Width, ed.Height))
            {
                ed.DrawToBitmap(b, new Rectangle(0, 0, ed.Width, ed.Height));
                b.Save(Path.Combine(outDir, "editor.png"), ImageFormat.Png);
            }
            ed.TestEditFirst();
            Application.DoEvents();
            using (Bitmap b = new Bitmap(ed.Width, ed.Height))
            {
                ed.DrawToBitmap(b, new Rectangle(0, 0, ed.Width, ed.Height));
                b.Save(Path.Combine(outDir, "editor-editing.png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(outDir, "chat-text.txt"), ed.TestChatText());
            using (Bitmap f = ed.Render(null)) f.Save(Path.Combine(outDir, "final-plain.png"), ImageFormat.Png);
            using (Bitmap f = ed.Render("Why is this button greyed out? I filled in every field above.")) f.Save(Path.Combine(outDir, "final-with-note.png"), ImageFormat.Png);
            using (Bitmap f = ed.Render(null))
                File.WriteAllText(Path.Combine(outDir, "clipboard.txt"), "put=" + Clip.Put(f, null) + " formats=" + string.Join(",", Clipboard.GetDataObject().GetFormats()));
            ed.Close();

            // Small capture: bubbles do not fit inside, so the copied image grows around them.
            Bitmap small = new Bitmap(220, 90);
            using (Graphics g = Graphics.FromImage(small)) { g.Clear(Color.FromArgb(30, 30, 34)); g.FillRectangle(Brushes.SteelBlue, 20, 25, 120, 40); }
            Editor ed2 = new Editor(small, new Rectangle(screenRect.X, screenRect.Y, 220, 90));
            ed2.TestSetup("");
            ed2.Show();
            Application.DoEvents();
            using (Bitmap b = new Bitmap(ed2.Width, ed2.Height))
            {
                ed2.DrawToBitmap(b, new Rectangle(0, 0, ed2.Width, ed2.Height));
                b.Save(Path.Combine(outDir, "small-editor.png"), ImageFormat.Png);
            }
            using (Bitmap f = ed2.Render(null)) f.Save(Path.Combine(outDir, "small-final.png"), ImageFormat.Png);
            ed2.Close();
        }
    }
}
