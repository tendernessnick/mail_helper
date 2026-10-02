-- MailHelper 增量拉取（docs/10 §6.2 草案；检查点②核验 + §6.1 单查询限量）
-- argv: lookbackHours, maxCount
-- 输出：记录 RS(=character id 30) 分隔；字段 US(=31)：id/subject/fromName/fromAddress/时间/hasAttach/isRead/preview
-- 时间字段 = «class isot»（YYYYMMDDTHHMMSS，Outlook 本地时区；locale 无关，不跨进程传日期字面量）
-- 值内 0x1E/0x1F 由 strip() 剥离（防御解析红线）
on strip(s)
    set AppleScript's text item delimiters to {character id 30, character id 31}
    set parts to text items of (s as text)
    set AppleScript's text item delimiters to ""
    return parts as text
end strip

on run argv
    set lookbackHours to (item 1 of argv) as integer
    set maxCount to (item 2 of argv) as integer
    if application "Microsoft Outlook" is not running then error "Outlook is not running (-600)" number -600
    tell application "Microsoft Outlook"
        set cutoff to (current date) - lookbackHours * hours
        set fresh to (messages of inbox whose time received > cutoff)
        set out to ""
        set n to 0
        repeat with m in fresh
            if n ≥ maxCount then exit repeat
            set mSubject to ""
            set mFromName to ""
            set mFromAddr to ""
            set mPreview to ""
            try
                set mSubject to my strip(subject of m as text)
            end try
            try
                set mFromName to my strip(name of sender of m as text)
            end try
            try
                set mFromAddr to my strip(address of sender of m as text)
            on error
                try
                    set mFromAddr to my strip(sender of m as text)
                end try
            end try
            try
                set mPreview to my strip(plain text content of m as text)
                if length of mPreview > 2000 then set mPreview to text 1 thru 2000 of mPreview
            end try
            set mAttach to "0"
            try
                if has attachment of m then set mAttach to "1"
            on error
                try
                    if (count of attachments of m) > 0 then set mAttach to "1"
                end try
            end try
            set mRead to "0"
            try
                if is read of m then set mRead to "1"
            end try
            set oneLine to my strip(id of m as text) & character id 31 & mSubject & character id 31 & mFromName & character id 31 & mFromAddr & character id 31 & ((time received of m) as «class isot» as text) & character id 31 & mAttach & character id 31 & mRead & character id 31 & mPreview
            if length of out > 0 then set out to out & character id 30
            set out to out & oneLine
            set n to n + 1
        end repeat
        return out
    end tell
end run
