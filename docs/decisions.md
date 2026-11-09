# 技术决策记录

## 不用钩子、不注入，靠轮询前台窗口（根据历史补记）
- 背景：Apex 卡顿和任务管理器卡死，查到根源是每个 WinEvent 钩子都会在持有全局 USER 锁时增加开销。TranslucentTB 是最大的订阅者之一，让每个 CREATE 事件多花约 50–90 µs。
- 比较过：TranslucentTB（用 WinEvent 钩子，开销大）；自己写钩子版（同样有开销）；轮询（有极小的 CPU 开销，但对 WinEvent 零影响）。
- 结论：每 100 ms 读一次前台窗口类名。探针实测，程序运行与否对 `NotifyWinEvent` 的耗时没有影响。
- 以后什么情况会改：系统提供不依赖 WinEvent 的样式变化通知时。

## 不读回任务栏状态，按前台窗口变化去补设（根据历史补记）
- 背景：跨进程调用 `GetWindowCompositionAttribute(WCA_ACCENT_POLICY)` 会失败，读不出 Explorer 有没有重置样式。
- 结论：离开系统界面后在 0/100/300/700 ms 各补设一次，另外每 30 秒兜底一次。
- 以后什么情况会改：找到能跨进程读出当前强调色的办法时。

## 系统界面在前台时不碰任务栏（根据历史补记）
- 背景：开始菜单或搜索打开时，Explorer 会自己重设任务栏样式，两边来回抢着设置，导致任务栏闪烁。
- 结论：这期间完全不设置透明；弹出类界面打开时调一次 `Restore()`（`/keeponshell` 可关闭）。这样处理后任务栏不闪了。
- 补充（2026-11-09）：主屏搜索框的闪烁经对照实验确认跟本程序无关，是 ExplorerPatcher 的副屏图标居中引起的，所以不再为它改程序，改为在 README 里写明怎么关掉副屏居中。

## 用系统自带 csc.exe 单文件编译（根据历史补记）
- 背景：这个工具很小，希望在任何 Win10 上都能直接编译，不装 SDK。
- 结论：只用 .NET Framework 4.x 的 `csc.exe`，不用项目文件和 NuGet。
- 以后什么情况会改：需要拆成多个文件或引入依赖时，再考虑改成 SDK 风格的项目。

## 暂不自己实现副屏图标居中（2026-11-09）
- 背景：EP 的副屏居中会让主屏搜索框闪，想看看能不能把居中集成进 TaskbarClear。
- EP 的做法（`ExplorerPatcher/TaskbarCenter.cpp`）：注入 Explorer 后挂钩 `MSTaskListWClass` 调用的 `GetClientRect`，返回一个偏移过的区域，让 Explorer 自己把图标排到中间；每次调用都在进程内用 MSAA 量按钮宽度，还会 `SetWindowPos`/`MoveWindow` 挪开始按钮和搜索框，副屏的会额外 `InvalidateRect`；另外直接改 Explorer 内存里的一处跳转来关掉动画。必须注入，违反本项目的硬性约束，不能照搬。
- 比较过：照搬 EP（需要注入，否决）；TaskbarX（用 UI Automation 频繁轮询，可能增加事件开销，自带透明还会冲突，否决）；不注入的自制版（跨进程 MSAA 量宽度 + `SetWindowPos` 挪副屏的 `MSTaskListWClass`，用 `EnumWindows` 数窗口作为变化触发，不碰开始按钮和搜索框）。
- 结论：用户选择先不做，保留 EP 的副屏居中，接受偶尔闪烁。
- 以后什么情况会改：闪烁难以忍受时，在 `spike/secondary-center` 分支上做不注入的自制版原型，做完要用探针确认 WinEvent 耗时不增加。
