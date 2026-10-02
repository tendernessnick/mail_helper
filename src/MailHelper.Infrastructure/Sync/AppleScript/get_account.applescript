-- MailHelper 登录账户地址（docs/10 §6.2；probe_connection 已含同语义输出，本脚本为独立入口备用/MS8 用）
-- 输出：首个账户 email
on strip(s)
    set AppleScript's text item delimiters to {character id 30, character id 31}
    set parts to text items of (s as text)
    set AppleScript's text item delimiters to ""
    return parts as text
end strip

on run argv
    tell application "Microsoft Outlook"
        try
            return my strip(email address of first exchange account as text)
        on error
            return my strip(email address of first imap account as text)
        end try
    end tell
end run
