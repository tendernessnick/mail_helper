# S14-A 应用图标生成：主色圆角底 + 白色信封 + 橙色角标（分类×重要度产品概念）
# 用法: powershell -File tools/make-icon.ps1
# 产出: src/MailHelper.App/Assets/app.ico（16/32/48/64/128/256 PNG 压缩条目）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outIco = Join-Path $PSScriptRoot "..\src\MailHelper.App\Assets\app.ico"
$outPng = Join-Path $PSScriptRoot "..\artifacts\screens\icon-preview.png"
New-Item -ItemType Directory -Force -Path (Split-Path $outIco) | Out-Null

function New-IconSurface([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    $r = [float]($size * 0.22)   # 圆角半径
    $m = [float]($size * 0.02)   # 边距
    $w = $size - 2 * $m

    # 主色渐变圆角底
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($m, $m, $d, $d, 180, 90)
    $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90)
    $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF($m, $m, $w, $w)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 0x11, 0x74, 0xCB),
        [System.Drawing.Color]::FromArgb(255, 0x0A, 0x4F, 0x8F),
        [float]90)
    $g.FillPath($brush, $path)

    # 白色信封（居中，比例化）
    $ew = [float]($size * 0.52); $eh = [float]($size * 0.36)
    $ex = [float](($size - $ew) / 2); $ey = [float](($size - $eh) / 2 + $size * 0.035)
    $er = [float]($size * 0.045)
    $ep = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ed = $er * 2
    $ep.AddArc($ex, $ey, $ed, $ed, 180, 90)
    $ep.AddArc($ex + $ew - $ed, $ey, $ed, $ed, 270, 90)
    $ep.AddArc($ex + $ew - $ed, $ey + $eh - $ed, $ed, $ed, 0, 90)
    $ep.AddArc($ex, $ey + $eh - $ed, $ed, $ed, 90, 90)
    $ep.CloseFigure()
    $white = [System.Drawing.Brushes]::White
    $g.FillPath($white, $ep)

    # 信封 V 形折线（主色描线）
    $pen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb(255, 0x0F, 0x6C, 0xBD), [float]([Math]::Max(2, $size * 0.030)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $ny = [float]($ey + $eh * 0.30)
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF($ex, $ny)),
        (New-Object System.Drawing.PointF(($ex + $ew / 2), ($ey + $eh * 0.72))),
        (New-Object System.Drawing.PointF(($ex + $ew), $ny))
    ))

    # 右上角橙色角标（白描边）
    $dotR = [float]($size * 0.115)
    $dotX = [float]($size - $m - $dotR * 1.7); $dotY = [float]($m + $dotR * 0.7)
    $g.FillEllipse([System.Drawing.Brushes]::White, ($dotX - $dotR * 1.28), ($dotY - $dotR * 1.28), $dotR * 2.56, $dotR * 2.56)
    $orange = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xF7, 0x63, 0x0C))
    $g.FillEllipse($orange, $dotX, $dotY, $dotR * 2, $dotR * 2)

    $g.Dispose()
    return $bmp
}

# PNG 编码 → ICO 容器（ICONDIR + ICONDIRENTRY×N + PNG 数据）
$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconSurface $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $bmp.Dispose()
    $ms.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)   # ICONDIR
$dataOffset = 6 + 16 * $pngs.Count
foreach ($entry in $pngs) {
    $s = [int]$entry[0]; $data = [byte[]]$entry[1]
    $bw.Write([byte]($s -band 0xFF))            # 宽（256→0）
    $bw.Write([byte]($s -band 0xFF))            # 高
    $bw.Write([byte]0)                          # 调色板
    $bw.Write([byte]0)                          # 保留
    $bw.Write([uint16]1)                        # 色平面
    $bw.Write([uint16]32)                       # 位深
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $data.Length
}
foreach ($entry in $pngs) { $bw.Write([byte[]]$entry[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outIco, $ico.ToArray())
$bw.Dispose()

# 预览图（256）
$preview = New-IconSurface 256
$preview.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Output "ICO=$([System.IO.Path]::GetFullPath($outIco)) ($((Get-Item $outIco).Length) bytes)"
Write-Output "PNG=$([System.IO.Path]::GetFullPath($outPng))"
