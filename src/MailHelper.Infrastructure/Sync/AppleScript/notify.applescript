-- MailHelper 系统通知（docs/10 §5.2：osascript display notification；v1 已知差异=署名「脚本编辑器」、无点击回传）
-- argv: title, body
on run argv
    set noticeTitle to item 1 of argv
    set noticeBody to item 2 of argv
    display notification noticeBody with title noticeTitle sound name "Glass"
end run
