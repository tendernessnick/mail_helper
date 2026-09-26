namespace MailHelper.Core.Rules;

/// <summary>规则类型（04 章 §2.3 RuleKind：发件人地址/发件人域名/主题正则/主题关键词；模式大小写不敏感）。
/// 注意：不存在 SenderKeyword——04 §7 示例中该值未定义，见 CHG-001。</summary>
public enum RuleKind
{
    SenderAddress,
    SenderDomain,
    SubjectRegex,
    SubjectKeyword,
}

/// <summary>规则来源（04 章 §2.3：Builtin 预置 / User 用户自定义 / Feedback 反馈生成）。</summary>
public enum RuleSource
{
    Builtin,
    User,
    Feedback,
}
