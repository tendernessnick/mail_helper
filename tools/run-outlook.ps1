# Outlook 桌面通道（CHG-011）：复用经典 Outlook 本机登录态，无需任何 OAuth 授权
# 前置：经典版 Outlook 已登录 CityU 账号
$env:MAILHELPER_CHANNEL = "Outlook"
Remove-Item Env:\MAILHELPER_DEV -ErrorAction SilentlyContinue
Remove-Item Env:\MAILHELPER_FORCE_IMAP -ErrorAction SilentlyContinue
Write-Output "[outlook] 启动 Outlook 桌面通道 —— 连接后将从本机 Outlook 收件箱同步"
Start-Process -FilePath "F:\AICoding\mail_helper\artifacts\publish\MailHelper.App.exe"
