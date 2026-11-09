# TaskbarClear

## 项目是什么
一个让 Windows 10 任务栏完全透明的小工具，用来替代 TranslucentTB。它最大的约束是不装任何 WinEvent 钩子或窗口钩子，也不注入 Explorer。用户之前排查 Apex 卡顿和任务管理器卡死，最后发现根源是：每多一个 WinEvent 订阅者，持有全局 USER 锁时就多一份开销，而 TranslucentTB 让每个 CREATE 事件多花约 50–90 µs。

## 技术栈
- C#（单文件），用系统自带的 .NET Framework 4.x `csc.exe` 编译，没有项目文件，也没有 NuGet 依赖。
- Win32 API（P/Invoke 调用）：`SetWindowCompositionAttribute`（WCA_ACCENT_POLICY）、`GetForegroundWindow`、`FindWindowEx`、`GetClassName`，托盘用 `Shell_NotifyIcon` + `TrackPopupMenu`。

## 目录结构
- `TaskbarClear.cs`：全部源码，包括参数解析、找任务栏窗口、设置透明强调色、前台窗口轮询（定时器驱动）、托盘图标和菜单。
- `assets/icon/`：图标的 SVG 源文件、多尺寸 `TaskbarClear.ico` 和生成脚本 `build-icon.ps1`，编译时用 `-win32icon` 嵌进 exe。
- `README.md` / `README.en.md`：中英文说明，包括为什么要做、和 TranslucentTB 的对比、安装、用法、原理和常见问题。改一边要同步另一边。
- `LICENSE`：GPL-3.0。
- `bench/`：性能复测脚本。`winevent_probe.ps1` 测 NotifyWinEvent 每次调用耗时；`compare.ps1` 交替对照 TaskbarClear 运行与未运行。两个文件要保存为带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 会按 GBK 读坏中文。
- `CHANGELOG.md`：写给使用者看的更新日志，每次发版在最上面加一节（新功能 / 修复 / 其他改进）。
- `docs/devlog.md`：开发日志。
- `docs/decisions.md`：技术决策记录。
- `logs/`：本地调试日志（`/log` 参数输出），已加入 .gitignore。
- `TaskbarClear.exe`：编译产物，已加入 .gitignore，开机自启指向这个文件。

## 常用命令
- 构建（正式版，无控制台窗口）：
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -optimize -target:winexe -win32icon:assets\icon\TaskbarClear.ico -out:TaskbarClear.exe TaskbarClear.cs`
- 构建（控制台版，配合 `/debug` 看输出）：把 `-target:winexe` 换成 `-target:exe`。
- 重新编译前先退出正在运行的实例：`taskkill /f /im TaskbarClear.exe`（不加 `/f` 关不掉，因为它只有隐藏窗口），编译完再启动 `TaskbarClear.exe`。
- 不点菜单也能触发菜单命令：向类名 `TaskbarClearWnd` 的窗口 PostMessage `WM_COMMAND`（0x0111），wParam 为 1 立即重设、2 切换自启、3 重启、4 退出。PowerShell 里调 `FindWindow` 时标题参数要传 `[NullString]::Value`，传 `$null` 会变成空字符串而找不到窗口。
- 手动验证：`TaskbarClear.exe /once` 设置一次透明后退出；`TaskbarClear.exe /restore` 恢复系统样式后退出。
- 类型检查 / lint：没有，编译通过就算检查通过。
- 测试：没有自动测试。行为只能人眼看任务栏来验证。
- 性能回归：改动可能影响开销时跑 `powershell -ExecutionPolicy Bypass -File bench\compare.ps1 -Rounds 5`，看每轮「运行中」是否稳定高于「未运行」。README 的性能表述只写「本机对照中未测到增加」这类可复测的说法，不写「为零」。

## 架构要点
- 透明效果：对 `Shell_TrayWnd`（主屏）和所有 `Shell_SecondaryTrayWnd`（副屏）设置 `ACCENT_ENABLE_TRANSPARENTGRADIENT`，Flags=2，Color=0。
- 跨进程调用 `GetWindowCompositionAttribute` 会失败，读不出 Explorer 有没有把样式重置，所以改为轮询前台窗口类名：平时每 100 ms 一次，刚离开系统界面后的补设阶段每 20 ms 一次。
- 前台窗口是开始菜单、搜索、操作中心（`Windows.UI.Core.CoreWindow`）、任务视图（`MultitaskingViewFrame` / `XamlExplorerHostIslandWindow`），或任务栏本身时，算作「系统界面」，这时不碰任务栏。弹出类界面打开时会调一次 `Restore()`（可用 `/keeponshell` 关掉）。
- 离开系统界面后，在 0/100/300/700 ms 各补设一次透明；另外每 30 秒兜底一次（`/interval` 可调）。
- 主循环是隐藏顶层窗口的消息循环 + `SetTimer`（不能用 message-only 窗口，否则收不到 `TaskbarCreated` 广播）。收到 `TaskbarCreated` 时重新加托盘图标并立刻设置透明。`/notray` 关掉托盘图标。
- 单实例：命名互斥量 `Local\TaskbarClearSingleInstance`，用 `WaitOne` 抢锁（不能靠 `new Mutex(true, ..., out created)` 的 `created`，它只表示互斥量是不是新建的）。菜单「重启」启动的新进程带 `/restarted`，最多等 5 秒让旧进程放锁。
- 开机自启：注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `TaskbarClear` 项。

## 已知问题
- 在副屏（笔记本内置屏幕）打开开始菜单时，主屏搜索框有一半以上的概率闪烁。已确认跟本程序无关，原因是 ExplorerPatcher 的副屏图标居中（`HKCU\Software\ExplorerPatcher\MMOldTaskbarAl`）。用户选择保留副屏居中（`MMOldTaskbarAl=1`），接受偶尔闪烁，不要再为这个现象改程序。EP 的居中原理（注入后挂钩 `GetClientRect`）和不注入的替代方案见 `docs/decisions.md`。
- 没有自动测试，回归只能人工看。
- 用户装了 ExplorerPatcher（dxgi.dll），用来让主副屏任务栏图标居中，要保留。它可能影响副屏任务栏的行为。

## 项目约定
- **硬性约束：不准用 WinEvent 钩子、窗口钩子，也不准注入 Explorer。** 低 WinEvent 开销是这个工具存在的理由。也不要引入 TranslucentTB。
- 只用系统自带的 `csc.exe` 编译，保持单文件、零依赖。
- 代码注释写中文（历史代码里的英文注释改到哪里就顺手换成中文）。
- 需要用户看屏幕才能判断的实验，先讲清楚怎么做、看什么，等用户反馈后再继续。
