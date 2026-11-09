# TaskbarClear

中文 | [English](README.en.md)

一个让 Windows 10 任务栏完全透明的小工具。**不装任何钩子，不注入 Explorer**，对系统窗口事件的开销实测为零。

![上：系统默认任务栏；下：TaskbarClear 的全透明任务栏](docs/preview/preview.png)

<sub>示意图：壁纸和图标是模拟的，不是真实截图。源文件是 [docs/preview/preview.html](docs/preview/preview.html)。</sub>

## 为什么要做

我在排查 Apex Legends 偶发卡顿和任务管理器切换标签时整机卡死的问题时，用探针测了 `NotifyWinEvent`（Windows 广播窗口事件的函数）每次调用的耗时：

- 我的电脑上每次要 **73–232 µs**，干净的系统通常只要个位数微秒。
- 原因是每个用 `SetWinEventHook` 订阅窗口事件的程序，都会让每个事件在**持有全局 USER 锁**期间多花一些时间。键盘、鼠标输入也要拿这把锁。
- 任务管理器切一次标签会发出上万个事件，乘以每次上百微秒，就是几十秒的整机卡顿。

逐个关闭常驻程序后，TranslucentTB 是订阅大户之一：

| 状态 | CREATE 事件 | NAMECHANGE 事件 |
|---|---|---|
| TranslucentTB 未运行 | 20.7 µs | 0.8 µs |
| TranslucentTB 运行（默认开启动态状态） | 69–84 µs | 81–89 µs |
| TranslucentTB 运行（关掉全部动态状态） | 47 µs | 46 µs |
| **TaskbarClear 运行** | **与未运行一致** | **与未运行一致** |

（普通权限，7 次中位数。TaskbarClear 的对照方法是：开着它、关掉它、只设置一次透明后退出，三种情况数值一致。）

关掉 TranslucentTB 等几个订阅大户后，任务管理器切换标签从 **30–60 秒整机卡死**变成 **5–10 秒完成**。可我还想要透明任务栏，于是写了 TaskbarClear：只保留「透明」这一个功能，用不需要钩子的方式实现。

## 和 TranslucentTB 比

| | TaskbarClear | TranslucentTB |
|---|---|---|
| WinEvent 钩子 | 无 | 有 |
| 注入 Explorer | 无 | 有（`ExplorerHooks.dll`） |
| 窗口事件额外开销 | 实测为零 | 每个事件多几十 µs |
| 体积 | 单个 exe，约 9 KB | 需要安装 |
| 依赖 | Win10 自带的 .NET Framework 4.x | 自带运行库 |
| 源码 | 一个文件，约 140 行 | 完整的应用工程 |
| 资源占用 | 内存约 15 MB，CPU 几乎为零 | — |
| 动态效果（最大化变色、开始菜单变样式等） | 不支持 | 支持 |

**适合你**，如果：你只想要一个全透明的任务栏，同时在意游戏帧稳定或系统响应。
**不适合你**，如果：你需要按窗口状态切换任务栏样式，或者用的是 Windows 11。

## 安装

目前没有发布编译好的版本，需要自己编译。Win10 自带编译器，不用装任何东西：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -optimize -target:winexe -out:TaskbarClear.exe TaskbarClear.cs
```

双击 `TaskbarClear.exe` 就开始在后台运行，没有窗口，只允许运行一个实例。

开机自启（把路径换成你自己的）：

```bat
reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /t REG_SZ /d "\"D:\Tools\TaskbarClear.exe\"" /f
```

取消自启：

```bat
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /f
```

退出程序（它没有窗口，必须加 `/f`）：

```bat
taskkill /f /im TaskbarClear.exe
```

## 用法

| 参数 | 作用 |
|---|---|
| （无参数） | 后台常驻，每 30 秒兜底设置一次透明 |
| `/interval 60000` | 把兜底间隔改为 60 秒（最小 1 秒） |
| `/once` | 只设置一次透明，然后退出 |
| `/restore` | 恢复系统默认样式，然后退出 |
| `/keeponshell` | 开始菜单、搜索打开时也不切回系统样式 |
| `/log D:\path\log.txt` | 把前台窗口变化和每次设置的时间记到文件里，便于排查 |
| `/debug` | 用控制台方式编译（`-target:exe`）时，把前台窗口变化输出到 stderr |

## 工作原理

- 用 `SetWindowCompositionAttribute` 给主屏任务栏 `Shell_TrayWnd` 和所有副屏任务栏 `Shell_SecondaryTrayWnd` 设置透明强调色（`ACCENT_ENABLE_TRANSPARENTGRADIENT`，颜色为 0）。
- Explorer 时不时会把样式改回去，而跨进程又读不到任务栏当前的样式（`GetWindowCompositionAttribute` 会失败），所以改为**每 100 ms 读一次前台窗口的类名**。这只是一次读取，不订阅任何事件。
- 开始菜单、搜索、操作中心、任务视图或任务栏本身在前台时，Explorer 正在自己设置任务栏样式，这时**不去碰任务栏**，避免两边来回抢着设置导致闪烁。
- 焦点回到普通窗口后立刻重新设置透明，并在 100、300、700 ms 后各补一次；另外每 30 秒兜底一次。

## 常见问题

**装了 ExplorerPatcher，副屏打开开始菜单时主屏搜索框会闪？**
这是 ExplorerPatcher 的副屏图标居中引起的，跟 TaskbarClear 无关：退出 TaskbarClear 后照样会闪，只关掉 ExplorerPatcher 的副屏居中就不闪了。想不闪，可以在 ExplorerPatcher 设置里关掉副屏居中，或者运行下面的命令后重启 Explorer：

```bat
reg add HKCU\Software\ExplorerPatcher /v MMOldTaskbarAl /t REG_DWORD /d 0 /f
```

**任务栏偶尔变回不透明？**
最多等 30 秒，或者切换一下窗口就会恢复。重启 Explorer 后也会自动对新的任务栏生效。

## 技术栈

C#（单文件）+ Win32 API（P/Invoke 调用），用 Windows 自带的 .NET Framework 4.x `csc.exe` 编译，没有任何第三方依赖。

## 许可证

[GPL-3.0](LICENSE)
