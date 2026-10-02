# 平台边界机械检索（docs/10 §7 边界红线的 CI 守护；MS0 落地）
# 红线：MailHelper.Core 与 MailHelper.Core.Services 不得出现——
#   条件编译(#if 族) / WPF 等表示层命名空间 / 注册表 / COM interop / AppleScript。
# Infrastructure 与 App 层不在此检查范围（Windows 实现集中于此两处，靠 [SupportedOSPlatform] 标注）。
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$targets = @('src/MailHelper.Core', 'src/MailHelper.Core.Services')

# 全部大小写不敏感匹配（-match 默认行为）
$rules = @(
    @{ Pattern = '^\s*#\s*(if|ifdef|ifndef|elif)';                 Name = '条件编译(#if 族)' },
    @{ Pattern = 'System\.Windows';                                Name = '表示层命名空间(System.Windows)' },
    @{ Pattern = 'Microsoft\.Win32\.Registry|using\s+Microsoft\.Win32\s*;'; Name = '注册表(Microsoft.Win32)' },
    @{ Pattern = 'System\.Runtime\.InteropServices|ComImport|\bMarshal\.|Microsoft\.Office'; Name = 'COM interop' },
    @{ Pattern = 'osascript|AppleScript';                          Name = 'AppleScript' }
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($t in $targets) {
    $dir = Join-Path $RepoRoot ($t -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $dir)) { throw "目标目录不存在: $dir" }
    Get-ChildItem $dir -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        ForEach-Object {
            $file = $_
            $lineNo = 0
            foreach ($line in Get-Content $file.FullName) {
                $lineNo++
                foreach ($r in $rules) {
                    if ($line -match $r.Pattern) {
                        $rel = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName)
                        $violations.Add("${rel}:${lineNo} [$($r.Name)] $line") | Out-Null
                    }
                }
            }
        }
}

if ($violations.Count -gt 0) {
    Write-Host "::error::平台边界红线被违反（docs/10 §7）——Core/Core.Services 出现以下内容："
    $violations | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}

Write-Host "平台边界检查通过：Core 与 Core.Services 干净（扫描 $($targets.Count) 个工程，0 处违规）"
exit 0
