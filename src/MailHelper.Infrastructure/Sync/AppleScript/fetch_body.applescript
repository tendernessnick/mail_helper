-- MailHelper 按需正文拉取（docs/10 §6.2 草案；MS8 接线阅读窗格；检查点②核验按 id 取消息的语法）
-- argv: messageId；输出：HTML content 全文
on run argv
    set mId to item 1 of argv
    if application "Microsoft Outlook" is not running then error "Outlook is not running (-600)" number -600
    tell application "Microsoft Outlook"
        set m to message id mId of inbox
        return content of m
    end tell
end run
