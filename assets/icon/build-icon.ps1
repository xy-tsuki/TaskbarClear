# 从 icon.svg 生成 TaskbarClear.ico：用无头 Edge 把 SVG 渲染成各尺寸的透明 PNG，再打包成 ICO（PNG 压缩格式，Vista 起支持）。
# 用法：powershell -ExecutionPolicy Bypass -File assets\icon\build-icon.ps1
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svg = Join-Path $dir 'icon.svg'
$out = Join-Path $dir 'TaskbarClear.ico'
$sizes = 16, 20, 24, 32, 48, 64, 256

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw '没找到 Edge 或 Chrome' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('tcicon_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    $svgUri = ([Uri]$svg).AbsoluteUri
    $pngs = @()
    foreach ($s in $sizes) {
        # 每个尺寸单独一页：透明背景，SVG 正好铺满 s x s 像素
        $html = Join-Path $tmp "i$s.html"
        "<!doctype html><html><body style='margin:0;background:transparent'><img src='$svgUri' width='$s' height='$s' style='display:block'></body></html>" |
            Set-Content -Encoding utf8 $html
        $png = Join-Path $tmp "i$s.png"
        # 用 Start-Process 调用：Edge 会往 stderr 打无关日志，直接调用会被 PowerShell 当成错误
        $edgeArgs = @('--headless', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                  '--default-background-color=00000000', "--window-size=$s,$s",
                  "--user-data-dir=`"$tmp\profile`"", "--screenshot=`"$png`"", ([Uri]$html).AbsoluteUri)
        Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -WindowStyle Hidden `
            -RedirectStandardError (Join-Path $tmp 'edge.log') -RedirectStandardOutput (Join-Path $tmp 'edge.out')
        if (-not (Test-Path $png)) { throw "渲染 $s 像素失败" }
        $pngs += , @($s, [IO.File]::ReadAllBytes($png))
    }

    # 48 像素及以下转成 32 位 BMP（带 alpha）：.NET Framework 的 System.Drawing.Icon 读不了小尺寸的 PNG 帧。
    # 64 和 256 像素保留 PNG（Windows 的惯例），体积小得多。
    Add-Type -AssemblyName System.Drawing
    $frames = @()
    foreach ($p in $pngs) {
        $s = $p[0]
        if ($s -gt 48) { $frames += , @($s, $p[1]); continue }
        $img = New-Object Drawing.Bitmap (New-Object IO.MemoryStream (, $p[1]))
        $fs = New-Object IO.MemoryStream
        $fw = New-Object IO.BinaryWriter $fs
        # BITMAPINFOHEADER：高度写成两倍（XOR 图 + AND 掩码）
        $fw.Write([uint32]40); $fw.Write([int32]$s); $fw.Write([int32]($s * 2)); $fw.Write([uint16]1); $fw.Write([uint16]32)
        $fw.Write([uint32]0); $fw.Write([uint32]0); $fw.Write([int32]0); $fw.Write([int32]0); $fw.Write([uint32]0); $fw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {          # 像素从下往上存，顺序为 BGRA
            for ($x = 0; $x -lt $s; $x++) {
                $c = $img.GetPixel($x, $y)
                $fw.Write([byte]$c.B); $fw.Write([byte]$c.G); $fw.Write([byte]$c.R); $fw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([math]::Ceiling($s / 32) * 4)   # AND 掩码每行按 4 字节对齐，全 0（透明度由 alpha 决定）
        $fw.Write((New-Object byte[] ($maskRow * $s)))
        $fw.Flush(); $img.Dispose()
        $frames += , @($s, $fs.ToArray())
    }

    # ICO 文件：6 字节文件头 + 每帧 16 字节目录项 + 各帧数据
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($p in $frames) {
        $s = $p[0]; $len = $p[1].Length
        $wb = if ($s -ge 256) { 0 } else { $s }   # 256 像素在目录项里记为 0
        $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
        $offset += $len
    }
    foreach ($p in $frames) { $bw.Write($p[1]) }
    $bw.Flush()
    [IO.File]::WriteAllBytes($out, $ms.ToArray())
    Write-Output ("已生成 {0}（{1} 个尺寸，{2} KB）" -f $out, $pngs.Count, [math]::Round($ms.Length / 1KB, 1))
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
