# 交替对照：TaskbarClear 运行时和未运行时，NotifyWinEvent 每次调用的耗时有没有差别。
# 每轮先测「运行中」再测「未运行」，每种状态测 $Samples 遍取中位数，共 $Rounds 轮。
# 交替进行是为了抵消系统背景负载随时间的漂移；单次数值波动很大，要看几轮之间有没有稳定的差距。
# 结束后恢复测试前的状态（原来在运行就重新启动）。普通权限即可。
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\TaskbarClear.exe'),
    [int]$Rounds = 3,
    [int]$Samples = 7,
    [int]$SettleMs = 2000   # 启动或退出后等一会儿再测
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$probe = Join-Path $PSScriptRoot 'winevent_probe.ps1'
$wasRunning = [bool](Get-Process TaskbarClear -ErrorAction SilentlyContinue)

function Median([double[]]$v) { $s = $v | Sort-Object; $n = $s.Count; if ($n % 2) { $s[[int](($n - 1) / 2)] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function SetState([bool]$on) {
    if ($on) {
        if (-not (Get-Process TaskbarClear -ErrorAction SilentlyContinue)) { Start-Process $Exe }
    } else {
        Get-Process TaskbarClear -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    Start-Sleep -Milliseconds $SettleMs
}
function Get-ProbeMedian {
    & $probe -Raw -Passes $Samples   # 探针内部已经丢掉预热遍并取中位数
}

$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
"系统：$($cv.ProductName) $($cv.DisplayVersion) build $($cv.CurrentBuild).$($cv.UBR)"
"CPU：$((Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim())"
"程序：$Exe"
"每种状态测 $Samples 遍取中位数，共 $Rounds 轮。单位 µs/次。"
""
$rows = foreach ($r in 1..$Rounds) {
    foreach ($on in $true, $false) {
        SetState $on
        $m = Get-ProbeMedian
        $row = [pscustomobject]@{ 轮 = $r; 状态 = $(if ($on) { '运行中' } else { '未运行' }); CREATE = [math]::Round($m.Create, 1); NAMECHANGE = [math]::Round($m.NameChange, 1) }
        Write-Host ("第 {0} 轮 {1}：CREATE {2}  NAMECHANGE {3}" -f $row.轮, $row.状态, $row.CREATE, $row.NAMECHANGE)
        $row
    }
}
SetState $wasRunning

""
"汇总（各轮中位数的中位数）："
foreach ($s in '运行中', '未运行') {
    $g = $rows | Where-Object 状态 -eq $s
    "  {0}：CREATE {1:F1}  NAMECHANGE {2:F1}" -f $s, (Median ($g | ForEach-Object CREATE)), (Median ($g | ForEach-Object NAMECHANGE))
}
"判断方法：如果「运行中」在每一轮都明显高于同轮的「未运行」，说明它增加了开销；互有高低则说明测不出差别。"
