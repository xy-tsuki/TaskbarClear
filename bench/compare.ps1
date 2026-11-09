# 交替对照：某个程序（默认 TaskbarClear）运行时和未运行时，NotifyWinEvent 每次调用的耗时有没有差别。
# 每轮先测「运行中」再测「未运行」，每种状态测 $Samples 遍取中位数，共 $Rounds 轮。
# 交替进行是为了抵消系统背景负载随时间的漂移；单次数值波动很大，要看几轮之间有没有稳定的差距。
# 结束后恢复测试前的状态（原来在运行就重新启动）。普通权限即可。
#
# 测别的程序时用 -Name 指定进程名、-Start 指定怎么启动它。测其他任务栏美化工具时会先退出 TaskbarClear
# （两个同时运行会互相抢着设置任务栏），测完再按原样启动回来。例如测应用商店版 TranslucentTB：
#   compare.ps1 -Name TranslucentTB -Start 'shell:AppsFolder\28017CharlesMilette.TranslucentTB_v826wp6bftszj!TranslucentTB'
param(
    [string]$Name = 'TaskbarClear',
    [string]$Start = (Join-Path $PSScriptRoot '..\TaskbarClear.exe'),
    [int]$Rounds = 3,
    [int]$Samples = 7,
    [int]$SettleMs = 2000   # 启动或退出后等一会儿再测
)
$ErrorActionPreference = 'Stop'
if ($Start -notmatch '^shell:') { $Start = (Resolve-Path $Start).Path }
$probe = Join-Path $PSScriptRoot 'winevent_probe.ps1'
$wasRunning = [bool](Get-Process $Name -ErrorAction SilentlyContinue)
$tbcExe = Join-Path $PSScriptRoot '..\TaskbarClear.exe'
$tbcPaused = $false
if ($Name -ne 'TaskbarClear' -and (Get-Process TaskbarClear -ErrorAction SilentlyContinue)) {
    Get-Process TaskbarClear | Stop-Process -Force; $tbcPaused = $true
    Write-Host "已临时退出 TaskbarClear，测完会重新启动它"
}

function Median([double[]]$v) { $s = $v | Sort-Object; $n = $s.Count; if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function SetState([bool]$on) {
    if ($on) {
        if (-not (Get-Process $Name -ErrorAction SilentlyContinue)) {
            # 应用商店版应用偶尔会报「Class not registered」，重试几次，后两次改用 explorer.exe 打开
            for ($try = 1; ; $try++) {
                try { if ($try -le 1) { Start-Process $Start } else { Start-Process explorer.exe $Start }; break }
                catch { if ($try -ge 3) { throw }; Start-Sleep -Seconds 2 }
            }
            # 等进程真正出现（应用商店版应用通过 shell: 启动，不能直接拿到进程对象）
            $sw = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Get-Process $Name -ErrorAction SilentlyContinue)) {
                if ($sw.ElapsedMilliseconds -gt 15000) { throw "15 秒内没等到进程 $Name 启动" }
                Start-Sleep -Milliseconds 200
            }
        }
    } else {
        Get-Process $Name -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    Start-Sleep -Milliseconds $SettleMs
}
function Get-ProbeMedian {
    & $probe -Raw -Passes $Samples   # 探针内部已经丢掉预热遍并取中位数
}

$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
"系统：$($cv.ProductName) $($cv.DisplayVersion) build $($cv.CurrentBuild).$($cv.UBR)"
"CPU：$((Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim())"
"程序：$Name（$Start）"
"每种状态测 $Samples 遍取中位数，共 $Rounds 轮。单位 µs/次。"
""
try {
$rows = foreach ($r in 1..$Rounds) {
    foreach ($on in $true, $false) {
        SetState $on
        $m = Get-ProbeMedian
        $row = [pscustomobject]@{ 轮 = $r; 状态 = $(if ($on) { '运行中' } else { '未运行' }); CREATE = [math]::Round($m.Create, 1); NAMECHANGE = [math]::Round($m.NameChange, 1) }
        Write-Host ("第 {0} 轮 {1}：CREATE {2}  NAMECHANGE {3}" -f $row.轮, $row.状态, $row.CREATE, $row.NAMECHANGE)
        $row
    }
}
} finally {
    # 不管中途有没有出错，都恢复测试前的状态
    SetState $wasRunning
    if ($tbcPaused) { Start-Process $tbcExe; Write-Host "已重新启动 TaskbarClear" }
}

""
"汇总（各轮中位数的中位数）："
foreach ($s in '运行中', '未运行') {
    $g = $rows | Where-Object 状态 -eq $s
    "  {0}：CREATE {1:F1}  NAMECHANGE {2:F1}" -f $s, (Median ($g | ForEach-Object CREATE)), (Median ($g | ForEach-Object NAMECHANGE))
}
"判断方法：如果「运行中」在每一轮都明显高于同轮的「未运行」，说明它增加了开销；互有高低则说明测不出差别。"
