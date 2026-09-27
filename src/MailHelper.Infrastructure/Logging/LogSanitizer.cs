using System.Security.Cryptography;
using System.Text;

namespace MailHelper.Infrastructure.Logging;

/// <summary>日志脱敏（04 章 §6 / 09 章 §5 红线）：
/// 主题截断 40 字符 + SHA256 前 8 位指纹；发件人仅保留域名；正文任何级别不落日志。</summary>
public static class LogSanitizer
{
    public const int SubjectMaxLength = 40;

    /// <summary>SHA256 前 8 位十六进制指纹（小写）。</summary>
    public static string Fingerprint8(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <summary>主题脱敏：截断 40 字符 + 原文指纹（指纹基于全文，可与完整主题对账）。</summary>
    public static string Subject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return "(empty)";
        }

        var truncated = subject.Length <= SubjectMaxLength ? subject : subject[..SubjectMaxLength];
        return truncated + "#" + Fingerprint8(subject);
    }

    /// <summary>发件人脱敏：仅保留域名（诊断模式才放开完整地址，08 §6.3）。</summary>
    public static string Sender(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "(unknown)";
        }

        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : "(unknown)";
    }
}
