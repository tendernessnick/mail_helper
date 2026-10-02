# S13 截图辅助：把 MailHelper 窗口拉到前台并截屏
# 用法: powershell -File tools/front-and-shot.ps1 [-OutPath artifacts\screens\s13.png] [-Maximize]
param(
    [string]$OutPath = "artifacts\screens\s13.png",
    [string]$ProcessName = "MailHelper.App",
    [switch]$Maximize
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MhWin32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[MhWin32]::SetProcessDPIAware() | Out-Null  # 截物理像素（高 DPI 屏 Bounds 虚拟化问题，S6 教训）

$p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "NOT_RUNNING"; exit 1 }

if ($Maximize) { [MhWin32]::ShowWindow($p.MainWindowHandle, 3) | Out-Null }
[MhWin32]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 900

$directory = Split-Path -Parent $OutPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save([System.IO.Path]::GetFullPath($OutPath), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Write-Output "SAVED $([System.IO.Path]::GetFullPath($OutPath))"
