using MailHelper.Core.Domain;

namespace MailHelper.Core.Services;

/// <summary>RemoteMessage（通道输出）→ MailMessage（本地存储行）转换。
/// 02 章 §9：正文预览仅存纯文本前 500 字符；Removed 变更 → IsDeletedRemote（本地不物理删除）。</summary>
public static class RemoteMessageMapper
{
    public const int PreviewLength = 500;

    public static MailMessage ToStored(RemoteMessage message, string accountId) => new(
        message.ProviderMessageId,
        accountId,
        message.InternetMessageId,
        message.Subject,
        message.FromName,
        message.FromAddress,
        TruncatePreview(message.BodyPreview),
        message.HtmlPath,
        message.ReceivedAtUtc,
        message.HasAttachments,
        message.IsRead,
        CategoryIds.Other,           // 新行待分类（CHG-002：显式 other/P2）
        Importance.P2,
        Confidence: null,
        ClassifiedBy: "rule",
        ClassifiedAtUtc: null,
        message.RemoteChangeKey,
        message.Kind == ChangeKind.Removed);

    internal static string? TruncatePreview(string? preview) =>
        preview is { Length: > PreviewLength } ? preview[..PreviewLength] : preview;
}
