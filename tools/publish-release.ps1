<#
.SYNOPSIS
  发布产物上传至 GitHub Releases（S17：替代 vpk upload github）。
  创建（或复用）Release → 上传资产（同名先删后传）→ 计算 SHA256 清单 → 组装 Release 说明。
.DESCRIPTION
  兼容 Windows PowerShell 5.1 / pwsh 7；先经本地 DryRun 与真实 API 演练（beta 渠道实测）。
.PARAMETER Token
  GitHub Token（CI 用 GITHUB_TOKEN，需 contents:write）。
.PARAMETER Repo
  形如 owner/name。
.PARAMETER Tag
  Release 标签（如 v0.6.0 或 v0.6.0-beta.42）。
.PARAMETER Version
  语义化版本（tag 去 v 前缀；beta 含 -beta.N）。
.PARAMETER Prerelease
  $true 时标记为预发布（beta 渠道）。
.PARAMETER AssetsDir
  资产目录（内含安装包/便携包；SHA256SUMS.txt 在此生成并上传）。
.PARAMETER ChangelogPath
  CHANGELOG.md 路径；按版本号截取对应小节作为 Release 说明（beta 无对应小节时用通用文案）。
.PARAMETER DryRun
  只打印计划动作，不访问网络。
#>
param(
  [Parameter(Mandatory = $true)][string]$Token,
  [Parameter(Mandatory = $true)][string]$Repo,
  [Parameter(Mandatory = $true)][string]$Tag,
  [Parameter(Mandatory = $true)][string]$Version,
  # 注意用字符串比较而非 [bool]（PowerShell 中 [bool]'False' 为 $true）
  [string]$Prerelease = 'false',
  [Parameter(Mandatory = $true)][string]$AssetsDir,
  [string]$ChangelogPath = 'CHANGELOG.md',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$isPre = $Prerelease -eq 'true'
$sumsPath = Join-Path $AssetsDir 'SHA256SUMS.txt'

# 1. SHA256 清单（重跑防叠加）
Remove-Item $sumsPath -ErrorAction SilentlyContinue
$assetFiles = Get-ChildItem $AssetsDir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' }
$sums = ($assetFiles | ForEach-Object {
  (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name
}) -join "`n"
[System.IO.File]::WriteAllText($sumsPath, $sums + "`n")
Write-Host "sums: $($assetFiles.Count) assets"

if ($DryRun) {
  Write-Host "[dryrun] would create/get release $Tag (prerelease=$isPre) on $Repo"
  Write-Host "[dryrun] would upload: $($assetFiles.Name -join ', ') + SHA256SUMS.txt"
  Write-Host "[dryrun] body = notes + manual link + sha section"
  exit 0
}

$headers = @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json' }
$apiBase = "https://api.github.com/repos/$Repo"
$uploadsBase = "https://uploads.github.com/repos/$Repo"

function Invoke-GhApi([string]$Method, [string]$Uri, [object]$Body, [string]$ContentType, [string]$InFile) {
  if ($Body -isnot [string]) { $Body = $Body | ConvertTo-Json }
  if ($InFile) {
    Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType $ContentType -InFile $InFile
  } elseif ($null -ne $Body) {
    Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType $ContentType -Body $Body
  } else {
    Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
  }
}

# 2. Release：先查后建（同 tag 重跑时复用）
$rel = $null
try {
  $rel = Invoke-GhApi 'GET' "$apiBase/releases/tags/$Tag"
} catch {
  Write-Host "release $Tag not found, creating"
}

if ($null -eq $rel) {
  $rel = Invoke-GhApi 'POST' "$apiBase/releases" @{ tag_name = $Tag; name = "MailHelper v$Version"; prerelease = $isPre } 'application/json'
}

# 3. 资产上传（同名先删后传，重跑幂等）
foreach ($f in ($assetFiles + @(Get-Item $sumsPath))) {
  foreach ($a in @($rel.assets)) {
    if ($a.name -eq $f.Name) {
      Invoke-GhApi 'DELETE' "$apiBase/releases/assets/$($a.id)"
      Write-Host "replaced asset: $($f.Name)"
      break
    }
  }
  Invoke-GhApi 'POST' "$uploadsBase/releases/$($rel.id)/assets?name=$($f.Name)" $null 'application/octet-stream' $f.FullName | Out-Null
  Write-Host "uploaded: $($f.Name)"
}

# 4. 说明 = CHANGELOG 对应小节（beta 用通用文案）+ 手册链接 + SHA256 段
$notes = "自动构建（beta 渠道预发布），稳定性不作保证。"
if (Test-Path $ChangelogPath) {
  $escaped = [regex]::Escape($Version)
  $m = [regex]::Match((Get-Content $ChangelogPath -Raw), "## $escaped[\s\S]*?(?=\r?\n## \d)")
  if ($m.Success) { $notes = $m.Value.Trim() }
}
$fence = [string][char]96 + [string][char]96 + [string][char]96
$manual = "> 📖 [使用手册](https://github.com/$Repo/blob/main/docs/%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C.md)"
$base = $rel.body
if ($base) { $base = [regex]::Replace($base, "### SHA256[\s\S]*$", "").TrimEnd() }
$body = "$base`n`n$notes`n`n$manual`n`n### SHA256 校验和（未签名版本，安装前建议核对）`n$fence`n$sums$fence`n"
Invoke-GhApi 'PATCH' "$apiBase/releases/$($rel.id)" @{ body = $body } 'application/json' | Out-Null
Write-Host "release body patched, done."
