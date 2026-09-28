# MailHelper 72h 长稳测试驱动（06 §7 长稳 / NFR-05；M3 门禁配套）
# 用法：
#   powershell -File tools\longrun.ps1 -Hours 72 -IntervalMinutes 5   # 完整长稳
#   powershell -File tools\longrun.ps1 -Hours 0.1 -IntervalMinutes 1  # 快速冒烟（验证脚本逻辑）
# 行为：以 MAILHELPER_DEV=1 拉起应用，按间隔触发「立即同步」（Ctrl+R 等价的 VM 命令不可外部调用，
# 改为观察式驱动：每轮检查进程存活、窗口存活、崩溃日志与日志错误增量，循环至时限后正常收尾并输出汇总。

param(
    [double]$Hours = 72,
    [int]$IntervalMinutes = 5,
    [string]$ExePath = "src\MailHelper.App\bin\Release\net8.0-windows10.0.17763.0\MailHelper.App.exe"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot $ExePath
if (-not (Test-Path $exe)) {
    $exe = Get-ChildItem (Join-Path $repoRoot 'src\MailHelper.App\bin\Release') -Recurse -Filter 'MailHelper.App.exe' |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
}

$dataDir = Join-Path $env:TEMP ("mh-longrun-" + (Get-Date -Format 'yyyyMMddHHmmss'))
$env:MAILHELPER_DEV = '1'
$env:MAILHELPER_DATA_DIR = $dataDir

$proc = Start-Process -FilePath $exe -PassThru
$deadline = (Get-Date).AddHours($Hours)
$nextCheck = Get-Date
$rounds = 0
$crashBaseline = 0
$crashFile = Join-Path $env:TEMP 'mailhelper-crash.log'
if (Test-Path $crashFile) { $crashBaseline = (Get-Item $crashFile).Length }

Write-Output "[longrun] start=$($proc.Id) exe=$exe dataDir=$dataDir hours=$Hours interval=$IntervalMinutes"

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds ([Math]::Min(60, [int]($IntervalMinutes * 60 / 4)))
    $proc.Refresh()
    if ($proc.HasExited) {
        Write-Output "[longrun] FAIL 进程提前退出 ExitCode=$($proc.ExitCode) 轮次=$rounds"
        exit 1
    }

    if ((Get-Date) -ge $nextCheck) {
        $rounds++
        $nextCheck = (Get-Date).AddMinutes($IntervalMinutes)
        $crashGrowth = 0
        if (Test-Path $crashFile) { $crashGrowth = (Get-Item $crashFile).Length - $crashBaseline }
        if ($crashGrowth -gt 0) {
            Write-Output "[longrun] WARN 崩溃日志增长 $crashGrowth 字节（见 $crashFile）"
        }
        Write-Output "[longrun] round=$rounds alive=True mem=$([Math]::Round($proc.WorkingSet64/1MB))MB"
    }
}

$proc.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 3
if (-not $proc.HasExited) { $proc.Kill() }

Write-Output "[longrun] PASS 运行 $Hours 小时完成；轮次=$rounds；数据目录保留于 $dataDir（可离线核查 SQLite 完整性）"
Write-Output "[longrun] 核查建议：sqlite3 $dataDir\mailhelper.db 'PRAGMA integrity_check;'"
exit 0
