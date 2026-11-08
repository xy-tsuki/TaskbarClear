// TaskbarClear: make the Windows 10 taskbar fully transparent WITHOUT any WinEvent / window hooks.
// It sets the accent policy on Shell_TrayWnd / Shell_SecondaryTrayWnd. While Start/Search/Action Center/Task View or
// the taskbar itself is in front it leaves the taskbar alone (Explorer restyles it then); when focus returns to a normal
// window it re-applies a few times, plus a slow periodic re-apply. Foreground is polled every 100 ms. Single instance.
// Usage: TaskbarClear.exe            run in background (default periodic interval 30000 ms)
//        TaskbarClear.exe /debug     (console build only) log foreground classes to stderr
//        TaskbarClear.exe /once      apply once and exit
//        TaskbarClear.exe /interval 60000
//        TaskbarClear.exe /restore   put the normal taskbar look back and exit
//        TaskbarClear.exe /keeponshell   do not switch to the system look while Start/Search is open
//        TaskbarClear.exe /log C:\pathile.txt   append foreground changes and re-applies to a file
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

static class TaskbarClear {
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State; public int Flags; public uint Color; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [DllImport("user32.dll")] static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);
    [DllImport("user32.dll")] static extern bool GetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_DISABLED = 0, ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;

    static List<IntPtr> Taskbars() {
        var r = new List<IntPtr>();
        IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (h != IntPtr.Zero) r.Add(h);
        IntPtr s = IntPtr.Zero;
        while ((s = FindWindowEx(IntPtr.Zero, s, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) r.Add(s);
        return r;
    }

    static bool Call(IntPtr hwnd, ref AccentPolicy a, bool set) {
        int size = Marshal.SizeOf(typeof(AccentPolicy));
        IntPtr p = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(a, p, false);
            var d = new WinCompAttrData { Attribute = WCA_ACCENT_POLICY, Data = p, SizeOfData = size };
            bool ok = set ? SetWindowCompositionAttribute(hwnd, ref d) : GetWindowCompositionAttribute(hwnd, ref d);
            if (!set && ok) a = (AccentPolicy)Marshal.PtrToStructure(p, typeof(AccentPolicy));
            return ok;
        } finally { Marshal.FreeHGlobal(p); }
    }

    static bool restoreOnShell = true;
    static void Restore() {
        Log("restore");
        foreach (var h in Taskbars()) { var a = new AccentPolicy { State = ACCENT_DISABLED }; Call(h, ref a, true); }
    }

    static string logPath;
    static void Log(string m) {
        if (logPath == null) return;
        try { System.IO.File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + m + Environment.NewLine); } catch { }
    }

    static void Apply(bool onlyIfReset) {
        Log("apply");
        foreach (var h in Taskbars()) {
            if (onlyIfReset) {
                var cur = new AccentPolicy();
                if (Call(h, ref cur, false) && cur.State == ACCENT_ENABLE_TRANSPARENTGRADIENT && cur.Color == 0) continue;
            }
            var a = new AccentPolicy { State = ACCENT_ENABLE_TRANSPARENTGRADIENT, Flags = 2, Color = 0x00000000 };
            Call(h, ref a, true);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    static string ClassOf(IntPtr h) {
        if (h == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString();
    }
    // Start / Search / Action Center are UWP CoreWindows; Task View is MultitaskingViewFrame (or a XAML island host);
    // clicking the taskbar itself makes Shell_TrayWnd / Shell_SecondaryTrayWnd the foreground window.
    static bool IsShellSurface(string cls) {
        return cls == "Windows.UI.Core.CoreWindow" || cls == "MultitaskingViewFrame" || cls == "XamlExplorerHostIslandWindow"
            || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "NotifyIconOverflowWindow";
    }

    static int Main(string[] args) {
        string all = string.Join(" ", args).ToLowerInvariant();
        if (all.Contains("/restore")) {
            Restore();
            return 0;
        }
        restoreOnShell = !all.Contains("/keeponshell");
        if (all.Contains("/once")) { Apply(false); return 0; }
        // GetWindowCompositionAttribute(WCA_ACCENT_POLICY) fails cross-process, so the reset cannot be read back.
        // Instead poll the foreground window's class (a cheap read, no hook):
        //  - while a shell surface (Start, Search, Action Center, Task View, or the taskbar itself) is in front, leave the
        //    taskbar alone - Explorer keeps re-styling it then, and fighting it causes flicker;
        //  - when focus returns to a normal window, re-apply a few times while Explorer settles;
        //  - otherwise only a slow periodic re-apply (default 30 s), also skipped while a shell surface is in front.
        int interval = 30000;
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/interval") int.TryParse(args[i + 1], out interval);
        if (interval < 1000) interval = 1000;
        bool debug = all.Contains("/debug");
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/log") logPath = args[i + 1];
        int[] settle = { 0, 100, 300, 700 };   // ms after leaving a shell surface
        bool created;
        using (var m = new Mutex(true, @"Local\TaskbarClearSingleInstance", out created)) {
            if (!created) return 1;
            Apply(false);
            IntPtr lastFg = IntPtr.Zero; bool shellOpen = false, restored = false;
            var sinceLeave = System.Diagnostics.Stopwatch.StartNew(); int step = settle.Length;
            var sincePeriodic = System.Diagnostics.Stopwatch.StartNew();
            while (true) {
                Thread.Sleep(step < settle.Length ? 20 : 100);
                IntPtr fg = GetForegroundWindow();
                if (fg != lastFg) {
                    lastFg = fg;
                    string cls = ClassOf(fg);
                    if (cls == "") { Log("fg none"); continue; }   // transient "no foreground window" while focus moves: ignore
                    bool nowShell = IsShellSurface(cls);
                    if (debug) Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " fg class=" + cls + " shell=" + nowShell);
                    Log("fg class=" + cls + " shell=" + nowShell);
                    if (shellOpen && !nowShell) { sinceLeave.Restart(); step = 0; }
                    // Entering a shell surface: hand the taskbar back to the system look once, so Explorer is not
                    // redrawing on top of our transparent accent (that made the search box flicker while Start was open).
                    bool popup = nowShell && !cls.StartsWith("Shell_") && cls != "NotifyIconOverflowWindow";
                    if (popup && restoreOnShell && !restored) { Restore(); restored = true; }
                    if (!nowShell) restored = false;
                    if (nowShell) step = settle.Length;
                    shellOpen = nowShell;
                }
                if (shellOpen) continue;
                if (step < settle.Length && sinceLeave.ElapsedMilliseconds >= settle[step]) { Apply(false); step++; sincePeriodic.Restart(); }
                else if (sincePeriodic.ElapsedMilliseconds >= interval) { Apply(false); sincePeriodic.Restart(); }
            }
        }
    }
}
