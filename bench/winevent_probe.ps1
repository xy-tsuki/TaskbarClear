# 测一次 NotifyWinEvent 调用现在要花多少微秒。
# 这个耗时主要来自 win32k 在持有全局 USER 锁时逐个检查 WinEvent 订阅者，订阅者越多、越重，数值越大。
# 用法：在关掉某个程序前后各跑一次，就能看出它是不是重度 WinEvent 订阅者。普通权限即可。
#
# 注意：新进程刚开窗口的头几秒，测出来的数值会明显偏高（实测第一遍 CREATE 约 185 µs，几秒后降到 10–35 µs），
# 所以同一个进程里先丢掉一遍，再测 $Passes 遍、每遍间隔 $GapMs，取中位数。整次约 3–4 秒。
param([int]$Count = 200, [int]$Passes = 7, [int]$GapMs = 300, [switch]$Raw)
Add-Type -AssemblyName System.Windows.Forms
if (-not ('WinEventProbe.N' -as [type])) {
    Add-Type -Namespace WinEventProbe -Name N -MemberDefinition '[DllImport("user32.dll")] public static extern void NotifyWinEvent(uint e, IntPtr h, int o, int c);'
}
function Median([double[]]$v) { $s = $v | Sort-Object; $n = $s.Count; if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }

# 0x8000 = EVENT_OBJECT_CREATE，0x8002 = SHOW，0x800C = NAMECHANGE，0x8005 = FOCUS；OBJID_CLIENT = -4
$events = 0x8000, 0x8002, 0x800C, 0x8005
$f = New-Object System.Windows.Forms.Form; $h = $f.Handle   # 需要一个本进程的窗口作为事件来源
$all = foreach ($p in 0..$Passes) {
    $one = foreach ($e in $events) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt $Count; $i++) { [WinEventProbe.N]::NotifyWinEvent($e, $h, -4, $i + 1) }
        $sw.Elapsed.TotalMilliseconds * 1000 / $Count
    }
    if ($p -gt 0) { ,$one }   # 第 0 遍是预热，丢掉
    Start-Sleep -Milliseconds $GapMs
}
$f.Dispose()
$res = 0..3 | ForEach-Object { $k = $_; Median ($all | ForEach-Object { $_[$k] }) }

if ($Raw) {
    # 供 compare.ps1 调用：直接输出数值
    [pscustomobject]@{ Create = $res[0]; Show = $res[1]; NameChange = $res[2]; Focus = $res[3] }
} else {
    "{0}  开机 {1:F1} 小时  NotifyWinEvent 每次调用 µs（{6} 遍中位数）：CREATE {2:F1}  SHOW {3:F1}  NAMECHANGE {4:F1}  FOCUS {5:F1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), ((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalHours, $res[0], $res[1], $res[2], $res[3], $Passes
}
