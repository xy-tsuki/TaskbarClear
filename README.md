# TaskbarClear

一个让 Win10 任务栏全透明的小程序，用来替代 TranslucentTB。

## 工作方式
- 用系统接口给任务栏（`Shell_TrayWnd`，副屏为 `Shell_SecondaryTrayWnd`）设置“透明强调色”。
- 跨进程读不到任务栏当前的透明状态（`GetWindowCompositionAttribute` 会返回失败），所以改为每 100 毫秒读一次当前前台窗口的类名（只读，开销很小）。
- 开始菜单、搜索、操作中心（类名 `Windows.UI.Core.CoreWindow`）、任务视图或任务栏本身在前台时，**不碰任务栏**，让系统按自己的样式显示，避免和 Explorer 来回抢着设置导致闪烁。
- 开始菜单、搜索或任务视图刚打开时，主动把任务栏切回系统默认样式一次。否则 Explorer 会在透明样式上反复重绘，搜索框会一直闪。点任务栏本身时不切换。
- 焦点回到普通窗口后立刻重新设置透明，并在 100、300、700 毫秒各补设一次。另外每 30 秒兜底设置一次；系统界面在前台时，兜底这一次也跳过。
- **不安装任何 WinEvent 钩子或窗口钩子，也不注入 Explorer。** 探针实测，它运行与否对 `NotifyWinEvent` 的耗时没有影响。
- 内存约 15 MB，CPU 几乎为零（实测 15 秒内累计 0.19 秒）。

## 用法
- `TaskbarClear.exe`：后台常驻（兜底间隔默认 30 秒）。只允许运行一个实例。
- `TaskbarClear.exe /interval 60000`：把兜底间隔改为 60 秒（最小 1 秒）。
- 用控制台方式编译（`-target:exe`）后加 `/debug`：把前台窗口类名的变化输出到 stderr，便于排查。
- `TaskbarClear.exe /keeponshell`：开始菜单或搜索打开时不切回系统样式（旧行为）。
- `TaskbarClear.exe /log D:\path\log.txt`：把前台窗口变化和每次设置的时间记录到文件，便于排查闪烁。
- `TaskbarClear.exe /once`：只设置一次，然后退出。
- `TaskbarClear.exe /restore`：恢复系统默认样式，然后退出。

## 常见问题：副屏打开开始菜单时，主屏搜索框偶尔闪烁
- 现象：在副屏（笔记本内置屏幕）打开开始菜单时，主屏任务栏的搜索框有一半以上的概率会闪，没找到稳定复现的规律。
- **跟 TaskbarClear 无关**：退出 TaskbarClear 并运行 `/restore` 恢复系统样式后，照样会闪（2026-11-09 实测）。程序日志也显示，开始菜单打开期间它没有写过任务栏。
- **原因是 ExplorerPatcher 的副屏图标居中**（2026-11-09 用对照实验确认）：
  - 临时停用 ExplorerPatcher（把 `C:\Windows\dxgi.dll` 改名后重启 Explorer）：不闪。
  - 恢复 ExplorerPatcher，只关掉副屏居中（`HKCU\Software\ExplorerPatcher` 下的 `MMOldTaskbarAl` 设为 0），主屏保持居中（`OldTaskbarAl` = 1）：不闪。
- 解决办法：在 ExplorerPatcher 设置里关掉副屏任务栏的图标居中，或者直接运行下面这条命令，然后重启 Explorer：
  `reg add HKCU\Software\ExplorerPatcher /v MMOldTaskbarAl /t REG_DWORD /d 0 /f`

## 开机自启
已写入注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，名称为 `TaskbarClear`。

取消自启：
`reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /f`

退出程序：
`taskkill /im TaskbarClear.exe`

## 编译
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -optimize -target:winexe -out:TaskbarClear.exe TaskbarClear.cs`
