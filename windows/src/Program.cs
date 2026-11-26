using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SnapNote
{
    static class Program
    {
        public const string Version = "0.2";
        public static Settings Cfg;
        public static string Dir;

        public static string SaveDir()
        {
            string f = Cfg.SaveFolder;
            if (string.IsNullOrEmpty(f)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapNote");
            return Path.IsPathRooted(f) ? f : Path.Combine(Dir, f);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Native.MakeDpiAware();
            try { Native.SetCurrentProcessExplicitAppUserModelID("SnapNote.App"); } catch { }
            Dir = AppDomain.CurrentDomain.BaseDirectory;
            bool capture = Array.IndexOf(args, "--capture") >= 0;

            int st = Array.IndexOf(args, "--selftest");
            if (st >= 0 && st + 1 < args.Length)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Cfg = Settings.Load(Path.Combine(Dir, "settings.ini"));
                Theme.Load(Cfg.ThemeName);
                SelfTest.Run(args[st + 1]);
                return;
            }

            bool created;
            Mutex mutex = new Mutex(true, "SnapNote.SingleInstance.v1", out created);
            if (!created)
            {
                // Already running: ask that copy to start a capture.
                IntPtr h = Native.FindWindow(null, HostWindow.Title);
                if (h != IntPtr.Zero)
                {
                    Native.AllowSetForegroundWindow(-1);
                    Native.PostMessage(h, HostWindow.WM_CAPTURE, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object o, ThreadExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(Dir, "error.log"), DateTime.Now + "  " + e.Exception + Environment.NewLine + Environment.NewLine); } catch { }
                Toast.Show("Something went wrong", "Details were written to error.log in the SnapNote folder.", 6000, true);
            };
            Cfg = Settings.Load(Path.Combine(Dir, "settings.ini"));
            Theme.Load(Cfg.ThemeName);
            Application.Run(new TrayApp(capture, Array.IndexOf(args, "--quiet") >= 0));
            GC.KeepAlive(mutex);
        }
    }

    class HostWindow : NativeWindow
    {
        public const string Title = "SnapNote.Host";
        public const int WM_CAPTURE = 0x8001;
        public event Action Capture;

        public HostWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = Title;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY || m.Msg == WM_CAPTURE)
            {
                if (Capture != null) Capture();
                return;
            }
            base.WndProc(ref m);
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly HostWindow host;
        Overlay overlay;
        Editor editor;
        string hotkeyText;

        public TrayApp(bool captureNow, bool quiet)
        {
            host = new HostWindow();
            host.Capture += StartCapture;
            SmartPaste.Init();

            // Try the configured hotkey first, then a few fallbacks, so the app always has a working shortcut.
            List<string> tries = new List<string>();
            tries.Add(Program.Cfg.Hotkey);
            foreach (string fb in new string[] { "Win+Shift+C", "Win+Shift+X", "Ctrl+Alt+A", "Win+Alt+A" })
                if (!fb.Equals(Program.Cfg.Hotkey, StringComparison.OrdinalIgnoreCase)) tries.Add(fb);
            foreach (string t in tries)
            {
                uint mods, vk;
                if (Hotkey.TryParse(t, out mods, out vk) && Native.RegisterHotKey(host.Handle, 1, mods | 0x4000, vk)) { hotkeyText = t; break; }
            }
            bool fellBack = hotkeyText != null && hotkeyText != Program.Cfg.Hotkey;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Ui.Scale = Native.DpiScaleAt(wa.X + wa.Width / 2, wa.Y + wa.Height / 2);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new LwMenuRenderer();
            menu.Font = Ui.Sans(13);
            menu.ShowImageMargin = false;
            ToolStripMenuItem cap = new ToolStripMenuItem("Capture", null, delegate { StartCapture(); });
            if (hotkeyText != null) cap.ShortcutKeyDisplayString = hotkeyText;
            menu.Items.Add(cap);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Edit settings", null, delegate { OpenPath(Program.Cfg.Path); }));
            menu.Items.Add(new ToolStripMenuItem("Open saved notes", null, delegate { try { Directory.CreateDirectory(Program.SaveDir()); } catch { } OpenPath(Program.SaveDir()); }));
            menu.Items.Add(new ToolStripMenuItem("Open tool folder", null, delegate { OpenPath(Program.Dir); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit SnapNote", null, delegate { Quit(); }));
            foreach (ToolStripItem it in menu.Items) it.Padding = new Padding(Ui.S(4), Ui.S(3), Ui.S(4), Ui.S(3));

            tray = new NotifyIcon();
            tray.Icon = Icons.Tray();
            tray.Text = "SnapNote" + (hotkeyText != null ? " (" + hotkeyText + ")" : "");
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) StartCapture(); };
            tray.Visible = true;

            // Waiting bubble: while SnapNote waits for your Ctrl+V the bubble on the tray icon breathes,
            // after the paste it shows a tick for a moment, then goes back to rest.
            restIcon = tray.Icon;
            tickIcon = Icons.Tray(1.08f, true);
            for (int i = 0; i < 14; i++)
                pulse.Add(Icons.Tray(0.9f + 0.26f * (0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / 14)), false));
            pulseTimer.Interval = 70;
            pulseTimer.Tick += delegate { frame = (frame + 1) % pulse.Count; tray.Icon = pulse[frame]; };
            tickTimer.Interval = 1600;
            tickTimer.Tick += delegate { tickTimer.Stop(); tray.Icon = restIcon; tray.Text = TrayText(); };
            SmartPaste.StateChanged += OnPasteState;

            if (hotkeyText == null)
                Toast.Show("No hotkey available",
                    Program.Cfg.Hotkey + " and the backups are taken by other apps. Pick another in settings.ini, then restart. You can still click the tray icon to capture.", 12000, true);
            else if (fellBack)
                Toast.Show("Using " + hotkeyText + " to capture",
                    Program.Cfg.Hotkey + " is taken by another app, so SnapNote is using " + hotkeyText + " instead. Change it in settings.ini.", 9000, false);
            else if (!quiet && !captureNow)
                Toast.Show("SnapNote is running", "Press " + hotkeyText + " to capture. Snap it, mark it, share it with AI or save it.", 5000, false);

            if (captureNow)
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 150;
                t.Tick += delegate { t.Stop(); t.Dispose(); StartCapture(); };
                t.Start();
            }
        }

        readonly List<Icon> pulse = new List<Icon>();
        Icon restIcon, tickIcon;
        readonly System.Windows.Forms.Timer pulseTimer = new System.Windows.Forms.Timer(), tickTimer = new System.Windows.Forms.Timer();
        int frame;

        string TrayText() { return "SnapNote" + (hotkeyText != null ? " (" + hotkeyText + ")" : ""); }

        void OnPasteState(int s)
        {
            pulseTimer.Stop();
            tickTimer.Stop();
            if (s == 1)
            {
                tray.Text = "SnapNote: waiting for your Ctrl+V";
                if (Fx.On) { frame = 0; pulseTimer.Start(); }
                else tray.Icon = pulse[pulse.Count / 2];
            }
            else if (s == 2)
            {
                tray.Icon = tickIcon;
                tray.Text = "SnapNote: pasted";
                tickTimer.Start();
            }
            else
            {
                tray.Icon = restIcon;
                tray.Text = TrayText();
            }
        }

        static void OpenPath(string p)
        {
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
        }

        void StartCapture()
        {
            if (editor != null && !editor.IsDisposed) { editor.BringUp(); return; }
            if (overlay != null) return;
            SmartPaste.Disarm();

            Rectangle vs = SystemInformation.VirtualScreen;
            Bitmap shot = new Bitmap(vs.Width, vs.Height);
            using (Graphics g = Graphics.FromImage(shot))
                g.CopyFromScreen(vs.X, vs.Y, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            List<Rectangle> wins = Overlay.VisibleWindows(vs);

            overlay = new Overlay(shot, vs, wins);
            overlay.FormClosed += delegate
            {
                Rectangle sel = overlay.Selection;
                overlay.Dispose();
                overlay = null;
                if (sel.Width >= 2 && sel.Height >= 2)
                {
                    Bitmap crop = shot.Clone(sel, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    shot.Dispose();
                    Rectangle screenRect = new Rectangle(sel.X + vs.X, sel.Y + vs.Y, sel.Width, sel.Height);
                    editor = new Editor(crop, screenRect);
                    editor.FormClosed += delegate { editor = null; };
                    editor.Show();
                }
                else shot.Dispose();
            };
            overlay.Show();
        }

        void Quit()
        {
            SmartPaste.Disarm();
            Native.UnregisterHotKey(host.Handle, 1);
            tray.Visible = false;
            tray.Dispose();
            host.DestroyHandle();
            ExitThread();
        }
    }

    static class Hotkey
    {
        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "") continue;
                if (p == "alt") mods |= 1;
                else if (p == "ctrl" || p == "control") mods |= 2;
                else if (p == "shift") mods |= 4;
                else if (p == "win" || p == "windows") mods |= 8;
                else
                {
                    Keys k;
                    if (p == "printscreen" || p == "prtsc" || p == "print") k = Keys.PrintScreen;
                    else if (p.Length == 1 && char.IsDigit(p[0])) k = Keys.D0 + (p[0] - '0');
                    else if (!Enum.TryParse<Keys>(raw.Trim(), true, out k)) return false;
                    vk = (uint)k;
                }
            }
            return vk != 0;
        }
    }

    static class Icons
    {
        public static Icon Tray() { return Tray(1f, false); }

        // Tray icon with the bubble at a given size (for the waiting pulse) or showing a tick (just pasted).
        public static Icon Tray(float bubble, bool tick)
        {
            int sz = SystemInformation.SmallIconSize.Width;
            return Make(sz, Theme.TaskbarDark() ? Color.White : Theme.H("#0b0b0c"), bubble, tick);
        }

        // The icon built into SnapNote.exe (src\SnapNote.ico), used for the editor's taskbar button.
        static Icon app;
        public static Icon App()
        {
            if (app == null)
            {
                try { app = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                if (app == null) app = Make(32, Theme.TaskbarDark() ? Color.White : Theme.H("#0b0b0c"));
            }
            return app;
        }

        // Crop marks with a small chat bubble in the corner.
        static Icon Make(int sz, Color c) { return Make(sz, c, 1f, false); }

        static Icon Make(int sz, Color c, float bubble, bool tick)
        {
            using (Bitmap b = new Bitmap(sz, sz))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float k = sz / 16f;
                    using (Pen p = new Pen(c, Math.Max(1.2f, 1.4f * k)))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                        float a = 1.5f * k, z = 14.5f * k, L = 4f * k;
                        g.DrawLines(p, new PointF[] { new PointF(a, a + L), new PointF(a, a), new PointF(a + L, a) });
                        g.DrawLines(p, new PointF[] { new PointF(z - L, a), new PointF(z, a), new PointF(z, a + L) });
                        g.DrawLines(p, new PointF[] { new PointF(a, z - L), new PointF(a, z), new PointF(a + L, z) });
                    }
                    GraphicsState st = g.Save();
                    g.TranslateTransform(11.75f * k, 11.5f * k);
                    g.ScaleTransform(bubble, bubble);
                    g.TranslateTransform(-11.75f * k, -11.5f * k);
                    using (SolidBrush br = new SolidBrush(Theme.H("#e5484d")))
                    {
                        RectangleF bub = new RectangleF(8.5f * k, 8.5f * k, 6.5f * k, 5f * k);
                        using (GraphicsPath gp = Ui.Round(bub, 1.5f * k)) g.FillPath(br, gp);
                        g.FillPolygon(br, new PointF[] { new PointF(10f * k, 13f * k), new PointF(12.5f * k, 13f * k), new PointF(10f * k, 15f * k) });
                    }
                    if (tick)
                        using (Pen tp = new Pen(Color.White, Math.Max(1f, 1.1f * k)))
                        {
                            tp.StartCap = LineCap.Round; tp.EndCap = LineCap.Round; tp.LineJoin = LineJoin.Round;
                            g.DrawLines(tp, new PointF[] { new PointF(10f * k, 11f * k), new PointF(11.3f * k, 12.2f * k), new PointF(13.6f * k, 9.8f * k) });
                        }
                    g.Restore(st);
                }
                IntPtr h = b.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return ic;
            }
        }
    }

    class Settings
    {
        public string Path;
        public string Hotkey = "Win+Shift+C";
        public bool SmartPaste = true;
        public int SmartPasteDelayMs = 400;
        public int SmartPasteTimeoutSec = 120;
        public bool NoteInImage = false;
        public Color AnnotationColor = ColorTranslator.FromHtml("#0B0B0C");
        public string ThemeName = "auto";
        public string SaveFolder = "";
        public bool AlsoSaveOnCopy = false;
        public bool Animations = true;

        const string Default =
@"# SnapNote settings. After editing: tray icon > Quit, then start the app again.

# Global hotkey. Combine Ctrl, Alt, Shift, Win with one key, e.g. Win+Shift+C, Ctrl+Alt+S, Ctrl+Shift+2
# If it is taken by another app, SnapNote falls back to Win+Shift+X, Ctrl+Alt+A or Win+Alt+A and tells you.
Hotkey=Win+Shift+C

# SmartPaste: after ""Copy for chat"", your next Ctrl+V pastes the screenshot, then the app pastes your note for you.
# false = image and note go on the clipboard together (some chat apps then paste only one of them).
SmartPaste=true

# Milliseconds between the image paste and the note paste. Raise it if the note sometimes replaces the image.
SmartPasteDelayMs=400

# Seconds SmartPaste waits for your Ctrl+V before giving up.
SmartPasteTimeoutSec=120

# Default for the ""Put note inside the image"" switch (the app remembers your last choice here).
NoteInImage=false

# Default colour for highlights and arrows (ink). The editor also offers blue and red.
AnnotationColor=#0B0B0C

# auto, light or dark
Theme=auto

# Where the Save button puts your notes (a PNG plus a .txt). Empty = Pictures\SnapNote.
SaveFolder=

# true = also save a copy there every time you copy for chat.
AlsoSaveOnCopy=false

# Little line-drawing animations (paper plane, tray, drawn-in marks, waiting bubble). false = off.
# They are also off when Windows animation effects are turned off.
Animations=true
";

        public static Settings Load(string path)
        {
            Settings s = new Settings();
            s.Path = path;
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, Default, new UTF8Encoding(false));
                foreach (string line in File.ReadAllLines(path))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 0) continue;
                    string k = t.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = t.Substring(eq + 1).Trim();
                    try
                    {
                        switch (k)
                        {
                            case "hotkey": s.Hotkey = v; break;
                            case "smartpaste": s.SmartPaste = Bool(v, true); break;
                            case "smartpastedelayms": s.SmartPasteDelayMs = int.Parse(v); break;
                            case "smartpastetimeoutsec": s.SmartPasteTimeoutSec = int.Parse(v); break;
                            case "noteinimage": s.NoteInImage = Bool(v, false); break;
                            case "annotationcolor": s.AnnotationColor = ColorTranslator.FromHtml(v); break;
                            case "theme": s.ThemeName = v; break;
                            case "savefolder": s.SaveFolder = v; break;
                            case "alsosaveoncopy": s.AlsoSaveOnCopy = Bool(v, false); break;
                            case "animations": s.Animations = Bool(v, true); break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return s;
        }

        static bool Bool(string v, bool def)
        {
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "on") return true;
            if (v == "false" || v == "0" || v == "no" || v == "off") return false;
            return def;
        }

        public void Set(string key, string value)
        {
            try
            {
                List<string> lines = new List<string>(File.Exists(Path) ? File.ReadAllLines(Path) : new string[0]);
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq > 0 && t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = key + "=" + value; found = true;
                    }
                }
                if (!found) lines.Add(key + "=" + value);
                File.WriteAllLines(Path, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
