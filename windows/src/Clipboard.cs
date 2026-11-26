using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SnapNote
{
    static class Clip
    {
        // Puts the image on the clipboard as a bitmap and as PNG (browsers and chat apps read one or the other).
        public static bool Put(Bitmap bmp, string text)
        {
            try
            {
                DataObject d = new DataObject();
                d.SetData(DataFormats.Bitmap, true, bmp);
                MemoryStream ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                d.SetData("PNG", false, ms);
                if (!string.IsNullOrEmpty(text)) d.SetData(DataFormats.UnicodeText, true, text);
                Clipboard.SetDataObject(d, true, 10, 60);
                return true;
            }
            catch { return false; }
        }

        public static bool PutText(string text)
        {
            try { Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, 10, 60); return true; }
            catch { return false; }
        }
    }

    // After "Copy for chat": the clipboard holds only the screenshot. When you press Ctrl+V
    // in any app, we wait a moment, swap the clipboard to your note and press Ctrl+V again for you.
    static class SmartPaste
    {
        static IntPtr hook = IntPtr.Zero;
        static Native.HookProc proc;
        static string pending;
        static bool triggered;
        static Timer fire, expire;

        // 0 = idle, 1 = waiting for Ctrl+V, 2 = just pasted. The tray icon follows this.
        public static event Action<int> StateChanged;
        static int state;
        static void SetState(int s) { if (state == s) return; state = s; if (StateChanged != null) StateChanged(s); }

        public static void Init()
        {
            proc = Hook;
            fire = new Timer();
            fire.Tick += delegate { Fire(); };
            expire = new Timer();
            expire.Tick += delegate { Disarm(); };
        }

        public static bool Arm(string note)
        {
            Disarm();
            hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) return false;
            pending = note;
            triggered = false;
            expire.Interval = Math.Max(5, Program.Cfg.SmartPasteTimeoutSec) * 1000;
            expire.Start();
            SetState(1);
            return true;
        }

        public static void Disarm()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            pending = null;
            if (fire != null) fire.Stop();
            if (expire != null) expire.Stop();
            if (state == 1) SetState(0);
        }

        static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && pending != null && !triggered)
            {
                int msg = w.ToInt32();
                if (msg == 0x100 || msg == 0x104)
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(Native.KBDLLHOOKSTRUCT));
                    bool injected = (k.flags & 0x10) != 0;
                    if (!injected && k.vkCode == 0x56 && Native.KeyDown(0x11) && !Native.KeyDown(0x12) && !Native.KeyDown(0x10)
                        && !Native.KeyDown(0x5B) && !Native.KeyDown(0x5C) && !OwnWindowFocused())
                    {
                        triggered = true;
                        fire.Interval = Math.Max(50, Program.Cfg.SmartPasteDelayMs);
                        fire.Start();
                    }
                }
            }
            return Native.CallNextHookEx(hook, code, w, l);
        }

        static bool OwnWindowFocused()
        {
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        static void Fire()
        {
            fire.Stop();
            string n = pending;
            Disarm();
            if (n == null || !Clip.PutText(n)) return;
            bool ctrlHeld = Native.KeyDown(0x11);
            List<Native.INPUT> keys = new List<Native.INPUT>();
            if (!ctrlHeld) keys.Add(Native.Key(0x11, false));
            keys.Add(Native.Key(0x56, false));
            keys.Add(Native.Key(0x56, true));
            if (!ctrlHeld) keys.Add(Native.Key(0x11, true));
            Native.SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
            SetState(2);
        }
    }
}
