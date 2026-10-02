-- MailHelper 连接探测（docs/10 §6.2 草案；字典字段名以检查点②真机核验为准，字段访问全部「首选+回退」）
-- 输出：账户数 <US> 首个账户 email；失败经 stderr（osascript 退出码 1 + 错误号括号）
-- 约定：US = character id 31；先查 running（不触发启动），再数账户（New Outlook 无邮件字典 → -1708 族）
on strip(s)
    set AppleScript's text item delimiters to {character id 30, character id 31}
    set parts to text items of (s as text)
    set AppleScript's text item delimiters to ""
    return parts as text
end strip

on run argv
    if application "Microsoft Outlook" is not running then error "Outlook is not running (-600)" number -600
    tell application "Microsoft Outlook"
        set n to (count of exchange accounts) + (count of imap accounts) + (count of pop accounts)
        if n = 0 then return "0" & character id 31 & ""
        set firstEmail to ""
        try
            set firstEmail to my strip(email address of first exchange account as text)
        on error
            try
                set firstEmail to my strip(email address of first imap account as text)
            on error
                try
                    set firstEmail to my strip(email address of first pop account as text)
                end try
            end try
        end try
        return (n as text) & character id 31 & firstEmail
    end tell
end run
