# MailHelper 真实登录启动脚本（检查点①：经环境变量注入 ClientId/租户）
param(
    [string]$ClientId = "30ff03da-b63e-4f1d-a77b-b417ced67a9b",
    [string]$TenantId = "2109ce83-7de4-4471-91ff-2053f90a1fd9",
    [string]$ExePath = ""
)
if (-not $ExePath) {
    $cand = Get-ChildItem (Join-Path $PSScriptRoot '..\artifacts\publish') -Filter 'MailHelper.App.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    $ExePath = $cand
}
$env:MAILHELPER_CLIENT_ID = $ClientId
$env:MAILHELPER_TENANT_ID = $TenantId
Remove-Item Env:\MAILHELPER_DEV -ErrorAction SilentlyContinue
Write-Output "[real] ClientId=$ClientId TenantId=$TenantId"
Write-Output "[real] 启动 $ExePath —— 点「连接学校邮箱」后用 CityU 账号登录"
Start-Process -FilePath $ExePath
