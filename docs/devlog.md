# 开发日志

新的写在上面。

## 2026-11-11
- 查 Win11 下不生效的原因：22621.1343 起任务栏背景由 Explorer 内的 XAML 绘制，盖住窗口强调色，`SetWindowCompositionAttribute` 返回成功但看不到效果。TranslucentTB 靠注入 Explorer 改 XAML 背景解决，本项目不注入，所以只做检测和提示：检测到 Win11 原生任务栏时弹一次托盘气泡、菜单加一行灰色说明，README 常见问题写明原因和 ExplorerPatcher 切回 Win10 样式任务栏的办法。本机是 Win10，用「把 Win10 的 `TrayNotifyWnd` 当成 XAML 宿主」的临时变体验证了提示路径能走通，正式版在 Win10 上不触发；Win11 真机上的检测还没验证。
- 顺手查了 TranslucentTB 的 issue：没有人指出 WinEvent 订阅本身拖慢窗口事件，最接近的是 TranslucentTB/TranslucentTB#318（游戏中卡顿，2020 年至今未解决）。

## 2026-11-10
- 发布 v1.1.0：PR xy-tsuki/TaskbarClear#4 以 merge 方式合并到 main（`3375fc1`），在上面打 tag `v1.1.0`。Release 建成草稿，附带用 main 代码编译的 `TaskbarClear.exe`（42 KB，SHA-256 `6f4308fc…d06e47`）。PR 开好后等了 10 分钟 Codex review，没有提出问题。
- 新增 `CHANGELOG.md`；PR 模板改成新格式（「过程中的调整」，每条附短 hash）。
- 重测 TaskbarClear：README 表里的 CREATE 28.3 / 41.2 看起来像「运行中反而更快」。查原始数据发现，11-09 那组环境比较吵，未运行时 CREATE 在 18.7 到 95.4 之间跳动。用 `compare.ps1 -Rounds 7` 连跑两组：第 1 组 15.4 / 13.9（4 轮高、3 轮低），第 2 组 14.1 / 18.3，方向相反。NAMECHANGE 两组都是 0.6–0.9，运行中和未运行一样。结论不变。README 换成波动最小的第 1 组，写明 TTB 和 TaskbarClear 是分两天测的。原始输出存进 `bench/results/2026-11-10.txt`。教训：CREATE 噪声能到几十 µs，判断时要看 NAMECHANGE 和逐轮方向，不能只看两个中位数谁大。

## 2026-11-09
- 用新脚本重测 TranslucentTB 2026.2（应用商店版），同一时段测三组，每组 5 轮。NAMECHANGE 中位数（运行中 / 未运行）：TTB 开启动态状态 94.2 / 1.2，关掉全部动态状态 67.3 / 1.3，TaskbarClear 1.1.0 是 1.2 / 1.2。TTB 两档每一轮都更高，TaskbarClear 互有高低。和最初的数据（81–89、46）大体一致，结论不变。README 的表换成新数据，原始输出存进 `bench/results/2026-11-09.txt`。`compare.ps1` 加了 `-Name`/`-Start`，可以测任意程序：测其他美化工具时会先退出 TaskbarClear，启动失败会重试，中途出错也会恢复原状。期间 `shell:AppsFolder` 启动 TTB 偶发「Class not registered」，所以加了重试。
- 按 review 意见让性能结论可复测：探针搬进仓库 `bench/winevent_probe.ps1`，新增交替对照脚本 `bench/compare.ps1`。README 里「开销实测为零」改成「在本机对照中，未测到 NotifyWinEvent 调用耗时增加」，新增「性能对照与复测」一节（系统版本、硬件、5 轮数据、复测步骤、覆盖不到的地方）。CPU 也补了实测：每分钟约 16 ms。
- 发现探针的坑：新进程开窗口的头几秒测出来明显偏高（第一遍 CREATE 约 185 µs，几秒后 10–35 µs），只预热一遍也不够。改成同一进程里丢掉一遍、再测 7 遍取中位数，单独运行也稳定在 13–16 µs。所以之前说的「CREATE 基线涨到约 200 µs」是误读，不是有新的订阅者。
- 加程序图标（SVG 源文件 + 多尺寸 ICO，64 像素帧用 PNG 压到 26 KB），编译时嵌进 exe。
- 加托盘图标和右键菜单：立即重设透明、开机自启、重启、退出（退出前恢复默认任务栏）。主循环改成隐藏窗口 + 定时器；Explorer 重启时收到 `TaskbarCreated` 后立刻重设透明。探针交替测 3 轮，开着和关着没有稳定差距，窗口事件开销仍是零。（当时以为 CREATE 基线涨到了约 200 µs，后来查明是探针在新进程里第一遍偏高造成的误读，见下一条。）
- 修复：菜单「重启」后程序消失。原因是单实例判断用了 `new Mutex(true, ..., out created)`，旧进程放了锁但句柄还在，新进程拿到 `created=false` 就退出了。改为 `WaitOne` 抢锁，重启的新进程带 `/restarted` 等最多 5 秒。外部触发连续 8 次重启都在 1 秒内接上。
- 内存：刚启动约 19 MB 工作集 / 6 MB 私有；程序化开关菜单 40 次、切换自启、重启都不会明显上涨。但用户手动用过菜单的实例一度到 59 MB / 34 MB，原因未确认（推测是成为前台窗口时载入了输入法组件）。
- 发布 v1.0.0：在 main（`5558211`）上打 tag，Release 附带编译好的 `TaskbarClear.exe`（SHA-256 `4af2346a…ffb899`）。README 的安装说明改为指向 Releases。
- README 重写（为什么要做、和 TranslucentTB 的对比），新增英文版、GPL-3.0 许可证、效果示意图；源码注释改为中文。
- 程序从 `排查apex卡顿\tools\TaskbarClear` 搬到独立目录，单独开对话维护。
- 排查「打开开始菜单时搜索框闪烁」：
  - v3：系统界面在前台时完全不碰任务栏。日志确认开始菜单打开期间程序零写入，但搜索框照样闪。
  - 当前版：开始菜单、搜索、任务视图打开时，先 `Restore()` 一次。还是闪。
  - 对照实验：退出程序并 `/restore` 后照样闪。准确的现象是：在副屏（笔记本内置屏幕）打开开始菜单时，**主屏**搜索框偶尔闪，概率大于 50%。结论：跟本程序无关，疑似 Explorer 或 ExplorerPatcher 的问题。写进 README「已知问题」。
- 联网搜索没找到已确认原因的同类案例。微软 2022 年那个任务栏闪烁的已知问题，本机 19045.6466 早已包含修复，可以排除。截图确认「搜索突出显示」（搜索亮点）本来就是关的，也排除。
- 二分排查 ExplorerPatcher：停用 EP（改名 `C:\Windows\dxgi.dll`）后不闪；恢复 EP、只关掉副屏居中（`MMOldTaskbarAl=0`）也不闪。结论：**元凶是 EP 的副屏图标居中**。研究了 EP 的居中实现后，用户决定暂不自己实现，把副屏居中恢复（`MMOldTaskbarAl=1`），接受偶尔闪烁。
- 初始化 git 仓库，补上 AGENTS.md、.gitignore、开发日志、决策记录、CI 和 PR 模板。
