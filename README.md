# TaskbarClear

中文 | [English](README.en.md)

一个让 Windows 10 任务栏完全透明的小工具。**不装任何钩子，不注入 Explorer**，在本机对照中，未测到 `NotifyWinEvent` 调用耗时增加（测量方法和复测步骤见[性能对照与复测](#性能对照与复测)）。

![上：系统默认任务栏；下：TaskbarClear 的全透明任务栏](docs/preview/preview.png)

<sub>示意图：壁纸和图标是模拟的，不是真实截图。源文件是 [docs/preview/preview.html](docs/preview/preview.html)。</sub>

## 为什么要做

我在排查 Apex Legends 偶发卡顿和任务管理器切换标签时整机卡死的问题时，用探针测了 `NotifyWinEvent`（Windows 广播窗口事件的函数）每次调用的耗时：

- 我的电脑上每次要 **73–232 µs**，干净的系统通常只要个位数微秒。
- 原因是每个用 `SetWinEventHook` 订阅窗口事件的程序，都会让每个事件在**持有全局 USER 锁**期间多花一些时间。键盘、鼠标输入也要拿这把锁。
- 任务管理器切一次标签会发出上万个事件，乘以每次上百微秒，就是几十秒的整机卡顿。

逐个关闭常驻程序后，TranslucentTB 是订阅大户之一。下面是用仓库里的对照脚本重测的结果（运行中 / 未运行，单位 µs/次）：

| 测的程序 | CREATE 事件 | NAMECHANGE 事件 | 每一轮都更高？ |
|---|---|---|---|
| TranslucentTB 2026.2（开启动态状态） | 88.4 / 18.4 | 94.2 / 1.2 | 是，5/5 轮 |
| TranslucentTB 2026.2（关掉全部动态状态） | 49.2 / 10.1 | 67.3 / 1.3 | 是，5/5 轮 |
| **TaskbarClear 1.1.0** | **15.4 / 13.9** | **0.6 / 0.6** | **否，7 轮里 4 轮高、3 轮低** |

（每格是各轮的中位数，每轮测 7 遍。TranslucentTB 两行测于 2026-11-09，每组 5 轮；TaskbarClear 测于 2026-11-10，7 轮。NAMECHANGE 最能看出差别：TranslucentTB 每个事件多 66–93 µs，TaskbarClear 测不出差别。CREATE 本身波动就有几 µs 到几十 µs。测量环境、复测方法和这次测量覆盖不到的地方，见[性能对照与复测](#性能对照与复测)。）

关掉 TranslucentTB 等几个订阅大户后，任务管理器切换标签从 **30–60 秒整机卡死**变成 **5–10 秒完成**。可我还想要透明任务栏，于是写了 TaskbarClear：只保留「透明」这一个功能，用不需要钩子的方式实现。

## 和 TranslucentTB 比

| | TaskbarClear | TranslucentTB |
|---|---|---|
| WinEvent 钩子 | 无 | 有 |
| 注入 Explorer | 无 | 有（`ExplorerHooks.dll`） |
| 窗口事件额外开销 | 本机对照中未测到增加 | 本机测得每个事件多几十 µs |
| 体积 | 单个 exe，约 40 KB（大部分是图标） | 需要安装 |
| 依赖 | Win10 自带的 .NET Framework 4.x | 自带运行库 |
| 源码 | 一个文件，约 400 行 | 完整的应用工程 |
| 资源占用 | 内存约 20 MB；CPU 本机实测每分钟约 16 ms（单核的 0.03%） | — |
| 动态效果（最大化变色、开始菜单变样式等） | 不支持 | 支持 |

**适合你**，如果：你只想要一个全透明的任务栏，同时在意游戏帧稳定或系统响应。
**不适合你**，如果：你需要按窗口状态切换任务栏样式，或者用的是 Windows 11 原生任务栏（原因和办法见[常见问题](#常见问题)）。

## 安装

从 [Releases](https://github.com/xy-tsuki/TaskbarClear/releases) 下载 `TaskbarClear.exe`，放到一个固定的目录。

也可以自己编译。Win10 自带编译器，不用装任何东西：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -optimize -target:winexe -win32icon:assets\icon\TaskbarClear.ico -out:TaskbarClear.exe TaskbarClear.cs
```

双击 `TaskbarClear.exe` 就开始在后台运行，托盘区会出现它的图标（可能被收在「^」里，可以拖出来）。只允许运行一个实例。

开机自启和退出都可以在托盘菜单里完成，见下面的「托盘菜单」。不想用托盘的话，也可以用命令行。

开机自启（把路径换成你自己的）：

```bat
reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /t REG_SZ /d "\"D:\Tools\TaskbarClear.exe\"" /f
```

取消自启：

```bat
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v TaskbarClear /f
```

强制退出（不会恢复任务栏样式，之后可以再运行一次 `TaskbarClear.exe /restore`）：

```bat
taskkill /f /im TaskbarClear.exe
```

## 托盘菜单

右键托盘图标会弹出菜单。系统语言是中文时显示中文，否则显示英文。

| 菜单项 | 作用 |
|---|---|
| `TaskbarClear 1.1.0` | 标题，显示版本号 |
| 立即重新设置透明 | 任务栏偶尔变回不透明时点一下。双击托盘图标效果一样 |
| 开机自启 | 打勾表示已开启，点一下切换。改的是上面那个 `Run` 注册表项，只有指向当前这个 exe 时才打勾 |
| 重启 TaskbarClear | 用同样的参数重新启动 |
| 退出（恢复默认任务栏） | 先把任务栏恢复成系统样式，再退出 |

托盘图标用的是普通的 `Shell_NotifyIcon`，不装钩子。加上托盘后重新做了对照，仍未测到 `NotifyWinEvent` 耗时增加。Explorer 重启后图标会自动回来。

## 用法

| 参数 | 作用 |
|---|---|
| （无参数） | 后台常驻，每 30 秒兜底设置一次透明 |
| `/interval 60000` | 把兜底间隔改为 60 秒（最小 1 秒） |
| `/once` | 只设置一次透明，然后退出 |
| `/restore` | 恢复系统默认样式，然后退出 |
| `/keeponshell` | 开始菜单、搜索打开时也不切回系统样式 |
| `/notray` | 不显示托盘图标（只能用命令行退出） |
| `/log D:\path\log.txt` | 把前台窗口变化和每次设置的时间记到文件里，便于排查 |
| `/debug` | 用控制台方式编译（`-target:exe`）时，把前台窗口变化输出到 stderr |

## 工作原理

- 用 `SetWindowCompositionAttribute` 给主屏任务栏 `Shell_TrayWnd` 和所有副屏任务栏 `Shell_SecondaryTrayWnd` 设置透明强调色（`ACCENT_ENABLE_TRANSPARENTGRADIENT`，颜色为 0）。
- Explorer 时不时会把样式改回去，而跨进程又读不到任务栏当前的样式（`GetWindowCompositionAttribute` 会失败），所以改为**每 100 ms 读一次前台窗口的类名**。这只是一次读取，不订阅任何事件。
- 开始菜单、搜索、操作中心、任务视图或任务栏本身在前台时，Explorer 正在自己设置任务栏样式，这时**不去碰任务栏**，避免两边来回抢着设置导致闪烁。
- 焦点回到普通窗口后立刻重新设置透明，并在 100、300、700 ms 后各补一次；另外每 30 秒兜底一次。
- Explorer 重启时会广播 `TaskbarCreated` 消息。程序收到后立刻给新任务栏设置透明，并重新加上托盘图标。

## 性能对照与复测

上面说的「开销」，指的是 `NotifyWinEvent` 每次调用的耗时。系统每广播一个窗口事件，都要在持有全局 USER 锁时逐个检查 WinEvent 订阅者，订阅者越多、越重，这个耗时就越长。对照方法很简单：分别在 TaskbarClear 运行和未运行时测这个耗时，看有没有稳定的差距。

**测量环境**：Windows 10 家庭中文版 22H2（build 19045.6466），AMD Ryzen 7 5800H，普通权限，装有 ExplorerPatcher。

**TaskbarClear 的逐轮结果**（2026-11-10，v1.1.0，带托盘图标；每格是 7 遍的中位数，单位 µs/次）：

| 轮次 | 运行中 CREATE | 未运行 CREATE | 运行中 NAMECHANGE | 未运行 NAMECHANGE |
|---|---|---|---|---|
| 1 | 11.4 | 15.9 | 0.6 | 0.6 |
| 2 | 18.6 | 15.7 | 0.6 | 0.5 |
| 3 | 15.4 | 13.5 | 0.6 | 0.5 |
| 4 | 16.3 | 10.6 | 1.2 | 0.6 |
| 5 | 14.0 | 13.6 | 0.5 | 0.7 |
| 6 | 14.9 | 13.9 | 0.6 | 0.7 |
| 7 | 15.8 | 17.6 | 0.9 | 0.6 |
| **中位数** | **15.4** | **13.9** | **0.6** | **0.6** |

CREATE 两种状态互有高低，差距落在轮与轮之间的波动里；NAMECHANGE 几乎一样。紧接着又跑了一组 7 轮，中位数变成 14.1 / 18.3，方向正好反过来，说明这几 µs 的差别是噪声。作为对照，测 TranslucentTB 时，5 轮里每一轮「运行中」都明显更高（见上面「为什么要做」里的表）。原始输出在 [`bench/results/2026-11-10.txt`](bench/results/2026-11-10.txt)（TaskbarClear 两组）和 [`bench/results/2026-11-09.txt`](bench/results/2026-11-09.txt)（TranslucentTB 两组，以及一组环境较吵时测的 TaskbarClear，未运行时 CREATE 在 18.7 到 95.4 之间跳动）。所以结论是：**在本机对照中，未测到 `NotifyWinEvent` 调用耗时增加。**

**自己复测**（只需要系统自带的 Windows PowerShell，不用管理员权限）：

1. 把 `TaskbarClear.exe` 和仓库里的 [`bench/`](bench) 目录放在一起（`bench` 和 exe 同级）。
2. 运行交替对照，大约 1–2 分钟。它会自动启动、结束 TaskbarClear，结束后恢复测试前的状态：

   ```bat
   powershell -ExecutionPolicy Bypass -File bench\compare.ps1 -Rounds 5
   ```

3. 看每一轮「运行中」是否都明显高于同轮的「未运行」。互有高低，就说明测不出差别。

测别的程序时，用 `-Name` 指定进程名，用 `-Start` 指定怎么启动它。测其他任务栏美化工具时，脚本会先退出 TaskbarClear，测完再启动回来。比如测应用商店版 TranslucentTB：

```bat
powershell -ExecutionPolicy Bypass -File bench\compare.ps1 -Rounds 5 -Name TranslucentTB -Start "shell:AppsFolder\28017CharlesMilette.TranslucentTB_v826wp6bftszj!TranslucentTB"
```

只想看系统现在的耗时（比如关掉某个程序前后各测一次，看它是不是重度订阅者），单独跑探针就行：

```bat
powershell -ExecutionPolicy Bypass -File bench\winevent_probe.ps1
```

**这个测量覆盖不到的地方**
- 只测了 WinEvent 分发的开销，没有测轮询本身占用的 CPU，也没有测游戏帧时间。
- 只在上面这一台机器上测过。数值受后台程序影响很大，同一台机器不同时间也可能差几倍，所以只能看同一时段内的交替对照，不同机器之间的绝对值不能直接比。
- 新进程刚开窗口的头几秒测出来会偏高很多，探针已经先丢掉一遍、再取多遍中位数来避开这个问题。「为什么要做」开头那个 73–232 µs 是最初排查时用早期探针测的，当时没有这样处理，绝对值可能偏高；表里的数据都是用现在的脚本重测的。

## 常见问题

**装了 ExplorerPatcher，副屏打开开始菜单时主屏搜索框会闪？**
这是 ExplorerPatcher 的副屏图标居中引起的，跟 TaskbarClear 无关：退出 TaskbarClear 后照样会闪，只关掉 ExplorerPatcher 的副屏居中就不闪了。想不闪，可以在 ExplorerPatcher 设置里关掉副屏居中，或者运行下面的命令后重启 Explorer：

```bat
reg add HKCU\Software\ExplorerPatcher /v MMOldTaskbarAl /t REG_DWORD /d 0 /f
```

**任务栏偶尔变回不透明？**
最多等 30 秒，或者切换一下窗口就会恢复；也可以双击托盘图标，或在托盘菜单里点「立即重新设置透明」。重启 Explorer 后也会马上对新的任务栏生效。

**Windows 11 上为什么不管用？**
从 Windows 11 22H2 的 22621.1343 起，任务栏的背景改成由 Explorer 里的 XAML 界面自己绘制，盖在窗口的强调色上面。TaskbarClear 的设置调用会成功，但被这层背景挡住，看不出变化。TranslucentTB 当时也因此失效，后来改成往 Explorer 里注入代码去改 XAML 背景；TaskbarClear 不注入 Explorer，所以没法这样做。

检测到 Windows 11 原生任务栏时，TaskbarClear 会弹一次托盘提示，托盘菜单里也会显示「Win11 原生任务栏：透明不生效」。

办法：用 [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) 把任务栏样式切换成 Windows 10 样式（ExplorerPatcher 设置 →「任务栏」→「任务栏样式」）。切换后任务栏又是传统的 Win32 窗口，TaskbarClear 就能照常让它透明。只想让原生任务栏更透一点、不要求全透明的话，可以打开系统的「透明效果」设置。

（作者的电脑是 Windows 10，Windows 11 上的行为来自公开资料和 TranslucentTB 的 issue，还没有在真机上验证过。遇到问题欢迎开 issue。）

## 技术栈

C#（单文件）+ Win32 API（P/Invoke 调用），用 Windows 自带的 .NET Framework 4.x `csc.exe` 编译，没有任何第三方依赖。

## 许可证

[GPL-3.0](LICENSE)
