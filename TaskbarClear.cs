﻿// TaskbarClear：让 Windows 10 任务栏完全透明，不装任何 WinEvent 钩子或窗口钩子。
// 做法是给 Shell_TrayWnd / Shell_SecondaryTrayWnd 设置透明强调色（accent policy）。开始菜单、搜索、操作中心、
// 任务视图或任务栏本身在前台时不碰任务栏（这时 Explorer 会自己重设样式）；焦点回到普通窗口后补设几次，
// 另外定期慢速兜底一次。前台窗口用定时器每 100 ms 轮询一次。只允许运行一个实例。
// 托盘图标的右键菜单：立即重新设置透明、开机自启、重启、退出。
// 用法：TaskbarClear.exe                 后台常驻（兜底间隔默认 30000 ms），显示托盘图标
//       TaskbarClear.exe /notray         不显示托盘图标
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
using Microsoft.Win32;

static class TaskbarClear {
    const string Version = "1.0.0";

    // ---------- 透明强调色 ----------
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

    // ---------- 窗口、定时器、托盘图标（全部是普通 Win32 调用，不装任何钩子） ----------
    delegate IntPtr WndProcFn(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX {
        public int cbSize; public int style; public WndProcFn lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
        public string lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public int time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA {
        public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState; public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int exStyle, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr inst, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    const uint WM_DESTROY = 0x0002, WM_NULL = 0x0000, WM_TIMER = 0x0113, WM_COMMAND = 0x0111, WM_APP_TRAY = 0x8001;
    const uint WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    const int NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    const int CMD_APPLY = 1, CMD_AUTOSTART = 2, CMD_RESTART = 3, CMD_EXIT = 4;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static WndProcFn wndProc = WndProc;   // 保存委托引用，防止被垃圾回收
    static IntPtr hwndMain, trayIcon;
    static uint msgTaskbarCreated;
    static bool showTray = true, zh, debug;
    static string[] startArgs;
    static Mutex mutex;
    static string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

    static string T(string cn, string en) { return zh ? cn : en; }

    static void AddTrayIcon() {
        if (!showTray) return;
        var d = TrayData();
        d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        d.uCallbackMessage = (int)WM_APP_TRAY;
        d.hIcon = trayIcon;
        d.szTip = "TaskbarClear " + Version;
        Shell_NotifyIcon(NIM_ADD, ref d);   // Explorer 还没启动好时会失败，等它广播 TaskbarCreated 再加
    }
    static void RemoveTrayIcon() {
        if (!showTray) return;
        var d = TrayData();
        Shell_NotifyIcon(NIM_DELETE, ref d);
    }
    static NOTIFYICONDATA TrayData() {
        var d = new NOTIFYICONDATA { hWnd = hwndMain, uID = 1, szTip = "", szInfo = "", szInfoTitle = "" };
        d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
        return d;
    }

    // 开机自启：HKCU 的 Run 项指向当前这个 exe 才算开启
    static bool AutostartOn() {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) {
            var v = k == null ? null : k.GetValue("TaskbarClear") as string;
            return v != null && v.Trim().Trim('"').Equals(exePath, StringComparison.OrdinalIgnoreCase);
        }
    }
    static void SetAutostart(bool on) {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) {
            if (on) k.SetValue("TaskbarClear", "\"" + exePath + "\"");
            else k.DeleteValue("TaskbarClear", false);
        }
        Log("autostart " + on);
    }

    static void ShowMenu() {
        IntPtr menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING | MF_GRAYED, IntPtr.Zero, "TaskbarClear " + Version);
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, (IntPtr)CMD_APPLY, T("立即重新设置透明", "Re-apply transparency now"));
        AppendMenu(menu, MF_STRING | (AutostartOn() ? MF_CHECKED : 0), (IntPtr)CMD_AUTOSTART, T("开机自启", "Start with Windows"));
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, (IntPtr)CMD_RESTART, T("重启 TaskbarClear", "Restart TaskbarClear"));
        AppendMenu(menu, MF_STRING, (IntPtr)CMD_EXIT, T("退出（恢复默认任务栏）", "Exit (restore default taskbar)"));
        POINT p; GetCursorPos(out p);
        SetForegroundWindow(hwndMain);   // 不先设前台，点菜单外面时菜单不会消失
        int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, p.X, p.Y, 0, hwndMain, IntPtr.Zero);
        PostMessage(hwndMain, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        RunCommand(cmd);
    }

    // 菜单命令；WM_COMMAND 也走这里，方便从外部发消息触发（自测用）
    static void RunCommand(int cmd) {
        switch (cmd) {
            case CMD_APPLY: Apply(false); StartSettle(); break;
            case CMD_AUTOSTART: SetAutostart(!AutostartOn()); break;
            case CMD_RESTART: Restart(); break;
            case CMD_EXIT: Restore(); Quit(); break;
        }
    }

    static void Quit() {
        RemoveTrayIcon();
        DestroyWindow(hwndMain);   // 触发 WM_DESTROY，退出消息循环
    }

    // 重启：先放开单实例锁，再用同样的参数启动新进程，然后自己退出
    static void Restart() {
        Log("restart");
        RemoveTrayIcon();
        try { mutex.ReleaseMutex(); } catch { }
        var sb = new System.Text.StringBuilder();
        foreach (var a in startArgs) {
            if (a.ToLowerInvariant() == "/restarted") continue;
            sb.Append(a.IndexOf(' ') >= 0 ? "\"" + a + "\" " : a + " ");
        }
        sb.Append("/restarted");   // 告诉新进程：旧进程还没完全退出，要等它放锁
        try { System.Diagnostics.Process.Start(exePath, sb.ToString()); } catch (Exception e) { Log("restart failed: " + e.Message); }
        showTray = false;   // 图标已经移除，避免退出时重复删除
        DestroyWindow(hwndMain);
    }

    // ---------- 轮询前台窗口（原来的主循环，改成定时器驱动） ----------
    // 跨进程调用 GetWindowCompositionAttribute(WCA_ACCENT_POLICY) 会失败，读不出样式有没有被重置。
    // 所以改为轮询前台窗口的类名（只是一次读取，不用钩子）：
    //  - 系统界面（开始菜单、搜索、操作中心、任务视图或任务栏本身）在前台时，不碰任务栏：
    //    这时 Explorer 会不断重设样式，跟它抢会导致闪烁；
    //  - 焦点回到普通窗口后，趁 Explorer 还在调整，补设几次；
    //  - 其余时间只做慢速兜底（默认 30 秒），系统界面在前台时兜底也跳过。
    static readonly int[] settle = { 0, 100, 300, 700 };   // 离开系统界面后第几毫秒补设
    static int interval = 30000, step = settle.Length;
    static uint timerMs;
    static IntPtr lastFg = IntPtr.Zero;
    static bool shellOpen, restored;
    static readonly System.Diagnostics.Stopwatch sinceLeave = System.Diagnostics.Stopwatch.StartNew();
    static readonly System.Diagnostics.Stopwatch sincePeriodic = System.Diagnostics.Stopwatch.StartNew();

    static void StartSettle() { sinceLeave.Restart(); step = 0; UpdateTimer(); }
    // 补设阶段每 20 ms 检查一次，平时每 100 ms 一次
    static void UpdateTimer() {
        uint want = step < settle.Length ? 20u : 100u;
        if (want != timerMs) { timerMs = want; SetTimer(hwndMain, (IntPtr)1, want, IntPtr.Zero); }
    }

    static void Tick() {
        IntPtr fg = GetForegroundWindow();
        if (fg != lastFg) {
            lastFg = fg;
            string cls = ClassOf(fg);
            if (cls == "") { Log("fg none"); return; }   // 焦点切换途中短暂出现的「没有前台窗口」：忽略
            bool nowShell = IsShellSurface(cls);
            if (debug) Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " fg class=" + cls + " shell=" + nowShell);
            Log("fg class=" + cls + " shell=" + nowShell);
            if (shellOpen && !nowShell) StartSettle();
            // 打开开始菜单、搜索或任务视图时，把任务栏交还给系统样式一次，免得 Explorer 在透明样式上反复重绘。
            // 注意：当初加这一步是为了解决搜索框闪烁，后来查明那是 ExplorerPatcher 副屏居中引起的
            // （见 docs/decisions.md），这一步对它无效。可用 /keeponshell 关闭。
            bool popup = nowShell && !cls.StartsWith("Shell_") && cls != "NotifyIconOverflowWindow";
            if (popup && restoreOnShell && !restored) { Restore(); restored = true; }
            if (!nowShell) restored = false;
            if (nowShell) step = settle.Length;
            shellOpen = nowShell;
        }
        if (!shellOpen) {
            if (step < settle.Length && sinceLeave.ElapsedMilliseconds >= settle[step]) { Apply(false); step++; sincePeriodic.Restart(); }
            else if (sincePeriodic.ElapsedMilliseconds >= interval) { Apply(false); sincePeriodic.Restart(); }
        }
        UpdateTimer();
    }

    static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) {
        if (msg == WM_TIMER) { Tick(); return IntPtr.Zero; }
        if (msg == WM_COMMAND) { RunCommand((int)(wParam.ToInt64() & 0xFFFF)); return IntPtr.Zero; }
        if (msg == WM_APP_TRAY) {
            uint ev = (uint)lParam.ToInt64();
            if (ev == WM_RBUTTONUP || ev == WM_CONTEXTMENU) ShowMenu();
            else if (ev == WM_LBUTTONDBLCLK) { Apply(false); StartSettle(); }
            return IntPtr.Zero;
        }
        if (msgTaskbarCreated != 0 && msg == msgTaskbarCreated) {
            // Explorer 重启后会广播这条消息：重新加托盘图标，并立刻给新任务栏设置透明
            Log("taskbar created");
            AddTrayIcon();
            Apply(false); StartSettle();
            return IntPtr.Zero;
        }
        if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static int Main(string[] args) {
        string all = string.Join(" ", args).ToLowerInvariant();
        if (all.Contains("/restore")) {
            Restore();
            return 0;
        }
        restoreOnShell = !all.Contains("/keeponshell");
        if (all.Contains("/once")) { Apply(false); return 0; }
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/interval") int.TryParse(args[i + 1], out interval);
        if (interval < 1000) interval = 1000;
        debug = all.Contains("/debug");
        showTray = !all.Contains("/notray");
        for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "/log") logPath = args[i + 1];
        startArgs = args;
        zh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        // 不能用 new Mutex(true, ..., out created) 判断：created 只说明互斥量是不是新建的。
        // 重启时旧进程已放开锁但句柄还在，互斥量仍然存在，新进程会被误判成重复实例而退出。
        // 所以改为打开后用 WaitOne 抢锁；重启出来的进程最多等 5 秒，让旧进程退干净。
        mutex = new Mutex(false, @"Local\TaskbarClearSingleInstance");
        bool owned;
        try { owned = mutex.WaitOne(all.Contains("/restarted") ? 5000 : 0); }
        catch (AbandonedMutexException) { owned = true; }   // 上一个实例被强杀，锁归我们
        if (!owned) { mutex.Dispose(); return 1; }
        try {
            SetProcessDPIAware();   // 高 DPI 下托盘图标和菜单不发糊
            IntPtr inst = GetModuleHandle(null);
            var wc = new WNDCLASSEX { lpfnWndProc = wndProc, hInstance = inst, lpszClassName = "TaskbarClearWnd" };
            wc.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
            RegisterClassEx(ref wc);
            // 普通的隐藏顶层窗口（不是 message-only 窗口），这样才能收到 TaskbarCreated 广播
            hwndMain = CreateWindowEx(0, "TaskbarClearWnd", "TaskbarClear", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            msgTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

            var small = new IntPtr[1];
            if (ExtractIconEx(exePath, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero) trayIcon = small[0];
            else trayIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512);   // exe 里没有图标时用系统默认图标
            AddTrayIcon();

            Apply(false);
            UpdateTimer();
            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessage(ref m); }
        } finally {
            try { mutex.ReleaseMutex(); } catch { }   // 重启时已经放开过，这里会抛异常，忽略
            mutex.Dispose();
        }
        return 0;
    }
}
