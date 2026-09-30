# IMAP 兜底通道验证启动（FR-02 预案 2：看 CityU 是否放行 IMAP scope 同意）
$env:MAILHELPER_CLIENT_ID = "30ff03da-b63e-4f1d-a77b-b417ced67a9b"
$env:MAILHELPER_TENANT_ID = "2109ce83-7de4-4471-91ff-2053f90a1fd9"
$env:MAILHELPER_FORCE_IMAP = "1"
$env:MAILHELPER_IMAP_USER = "ruijiehu7-c@my.cityu.edu.hk"
Remove-Item Env:\MAILHELPER_DEV -ErrorAction SilentlyContinue
Write-Output "[imap] 启动 IMAP 通道验证 —— 点「连接学校邮箱」观察同意页"
Start-Process -FilePath "F:\AICoding\mail_helper\artifacts\publish\MailHelper.App.exe"
