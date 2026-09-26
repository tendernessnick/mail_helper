namespace MailHelper.Core.Domain;

/// <summary>远端邮件统一模型（04 章 §2.1，Graph 与 IMAP 双通道统一输出，签名不得偏离）。</summary>
public record RemoteMessage(
    string ProviderMessageId,        // Graph id 或 IMAP UID 复合键
    string InternetMessageId,
    string Subject,
    string FromName, string FromAddress,
    string BodyPreview,              // 预处理后的纯文本前 500 字符
    string? HtmlPath,                // 正文磁盘缓存相对路径（可能为 null）
    DateTime ReceivedAtUtc,
    bool HasAttachments, bool IsRead,
    ChangeKind Kind);                // Added / Updated / Removed
