namespace MailHelper.Core.Domain;

/// <summary>本地存储的邮件领域记录（对应 04 章 §3.1 MESSAGES 实体；EF 实体隔离在 Infrastructure）。</summary>
public sealed record MailMessage(
    string Id,                    // graphId 或 imap:accountId:folder:uid
    string AccountId,
    string? InternetMessageId,
    string? Subject,
    string? FromName,
    string? FromAddress,
    string? BodyPreview,          // 纯文本前 500 字符（02 章 §9）
    string? BodyPath,             // 正文磁盘缓存相对路径
    DateTime ReceivedAtUtc,
    bool HasAttachments,
    bool IsRead,
    string Category,
    Importance Importance,
    double? Confidence,
    string ClassifiedBy,          // rule|user|llm
    DateTime? ClassifiedAtUtc,    // null = 未分类（待处理管线）
    string? RemoteChangeKey,
    bool IsDeletedRemote);
