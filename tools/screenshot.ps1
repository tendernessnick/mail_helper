# 手工截屏脚本（总控指令六.S6：PowerShell 截屏，窗口截图优先由 FlaUI 冒烟自动完成）
# 用法: powershell -File tools/screenshot.ps1 [-OutPath artifacts\screens\manual.png]
param(
    [string]$OutPath = "artifacts\screens\manual.png"
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$directory = Split-Path -Parent $OutPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save([System.IO.Path]::GetFullPath($OutPath), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "已保存屏幕截图: $([System.IO.Path]::GetFullPath($OutPath))"
