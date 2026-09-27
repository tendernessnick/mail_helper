# 覆盖率门禁（并集语义）：跨测试工程合并同一行被任一测试命中即算覆盖
# 用法: powershell -File tools/coverage-gate.ps1 [-Assembly MailHelper.Core] [-MinRate 0.80]
param(
    [string]$Assembly = "MailHelper.Core",
    [double]$MinRate = 0.80
)

$ErrorActionPreference = 'Stop'
$reports = Get-ChildItem -Path . -Recurse -Filter 'coverage.cobertura.xml'
if (-not $reports) { Write-Error '未找到覆盖率文件'; exit 1 }

$map = @{}
foreach ($r in $reports) {
    [xml]$x = Get-Content -LiteralPath $r.FullName -Raw
    $packages = $x.SelectNodes('//package') | Where-Object { $_.name -eq $Assembly }
    foreach ($p in $packages) {
        foreach ($c in $p.SelectNodes('.//class')) {
            if ($c.filename -match 'obj[\\/]') { continue }  # 源生成器产物（D-14）
            # 归一化：不同测试工程的 cobertura filename 前缀不一致（'Rules\x.cs' vs 'MailHelper.Core\Rules\x.cs'）
            $file = [string]$c.filename
            $marker = "$Assembly\"
            $idx = $file.LastIndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase)
            if ($idx -ge 0) { $file = $file.Substring($idx + $marker.Length) }
            foreach ($l in $c.SelectNodes('.//line')) {
                if ($l.branch -eq 'True') { continue }
                $key = "$file|$($l.number)"
                $hit = [double]$l.hits -gt 0
                if ($map.ContainsKey($key)) { $map[$key] = ($map[$key] -or $hit) }
                else { $map[$key] = $hit }
            }
        }
    }
}

$total = $map.Count
if ($total -eq 0) {
    Write-Host "::notice::$Assembly 当前无可执行行（骨架阶段），门禁自逻辑代码落地起严格执行 >= $($MinRate * 100)%"
    exit 0
}

$covered = 0
foreach ($v in $map.Values) { if ($v) { $covered++ } }
$rate = $covered / $total
Write-Host ("{0} line coverage (union) = {1:P2} ({2}/{3})" -f $Assembly, $rate, $covered, $total)
if ($rate -lt $MinRate) { Write-Error "$Assembly 行覆盖率低于门禁 $($MinRate * 100)%（NFR-10/G1）"; exit 1 }
exit 0
