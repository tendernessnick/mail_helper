param($Token, [string]$Dir, [string]$Repo, [string]$Tag)
$sumsPath = Join-Path $Dir 'SHA256SUMS.txt'
Remove-Item $sumsPath -ErrorAction SilentlyContinue
Get-ChildItem $Dir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
  $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
  "$hash  $($_.Name)" | Out-File $sumsPath -Append -Encoding utf8
}
$headers = @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json' }
$rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers
$sums = (Get-Content $sumsPath -Raw).TrimEnd()
$fence = [string][char]96 + [string][char]96 + [string][char]96
$base = $rel.body
if ($base) { $base = [regex]::Replace($base, "### SHA256[\s\S]*$", "").TrimEnd() }
$body = $base + "`n`n### SHA256 (unsigned build - verify before install)`n" + $fence + "`n" + $sums + "`n" + $fence + "`n"
$json = @{ body = $body } | ConvertTo-Json
Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/$Repo/releases/$($rel.id)" -Headers $headers -Body $json -ContentType 'application/json' | Out-Null
Write-Host "release body patched"
try {
  $uploadUri = "https://uploads.github.com/repos/$Repo/releases/$($rel.id)/assets?name=SHA256SUMS.txt"
  Invoke-RestMethod -Method Post -Uri $uploadUri -Headers $headers -ContentType 'text/plain' -InFile $sumsPath | Out-Null
  Write-Host "asset uploaded"
} catch {
  Write-Host ("asset upload skipped: " + $_.Exception.Message)
}
