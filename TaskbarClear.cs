// TaskbarClear：让 Windows 10 任务栏完全透明，不装任何 WinEvent 钩子或窗口钩子。
// 做法是给 Shell_TrayWnd / Shell_SecondaryTrayWnd 设置透明强调色（accent policy）。开始菜单、搜索、操作中心、
// 任务视图或任务栏本身在前台时不碰任务栏（这时 Explorer 会自己重设样式）；焦点回到普通窗口后补设几次，
// 另外定期慢速兜底一次。前台窗口每 100 ms 轮询一次。只允许运行一个实例。
// 用法：TaskbarClear.exe                 后台常驻（兜底间隔默认 30000 ms）
//       TaskbarClear.exe /debug          （仅控制台版）把前台窗口类名输出到 stderr
//       TaskbarClear.exe /once           设置一次后退出
//       TaskbarClear.exe /interval 60000 修改兜底间隔
//       TaskbarClear.exe /restore        恢复系统默认样式后退出
//       TaskbarClear.exe /keeponshell    开始菜单、搜索打开时不切回系统样式
//       TaskbarClear.exe /log D:\path\log.txt   把前台窗口变化和每次设置追加记录到文件
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
    // 开始菜单、搜索、操作中心都是 UWP 的 CoreWindow；任务视图是 MultitaskingViewFrame（或 XAML 岛宿主窗口）；
    // 点击任务栏本身时，前台窗口会变成 Shell_TrayWnd / Shell_SecondaryTrayWnd。
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
        // 跨进程调用 GetWindowCompositionAttribute(WCA_ACCENT_POLICY) 会失败，读不出样式有没有被重置。
        // 所以改为轮询前台窗口的类名（只是一次读取，不用钩子）：
        //  - 系统界面（开始菜单、搜索、操作中心、任务视图或任务栏本身）在前台时，不碰任务栏：
        //    这时 Explorer 会不断重设样式，跟它抢会导致闪烁；
        //  - 焦点回到普通窗口后，趁 Explorer 还在调整，补设几次；
        //  - 其余时间只做慢速兜底（默认 30 秒），系统界面在前台时兜底也跳过。
        int interval = 30000;
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/interval") int.TryParse(args[i + 1], out interval);
        if (interval < 1000) interval = 1000;
        bool debug = all.Contains("/debug");
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/log") logPath = args[i + 1];
        int[] settle = { 0, 100, 300, 700 };   // 离开系统界面后第几毫秒补设
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
                    if (cls == "") { Log("fg none"); continue; }   // 焦点切换途中短暂出现的「没有前台窗口」：忽略
                    bool nowShell = IsShellSurface(cls);
                    if (debug) Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " fg class=" + cls + " shell=" + nowShell);
                    Log("fg class=" + cls + " shell=" + nowShell);
                    if (shellOpen && !nowShell) { sinceLeave.Restart(); step = 0; }
                    // 打开开始菜单、搜索或任务视图时，把任务栏交还给系统样式一次，免得 Explorer 在透明样式上反复重绘。
                    // 注意：当初加这一步是为了解决搜索框闪烁，后来查明那是 ExplorerPatcher 副屏居中引起的
                    // （见 docs/decisions.md），这一步对它无效。可用 /keeponshell 关闭。
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
