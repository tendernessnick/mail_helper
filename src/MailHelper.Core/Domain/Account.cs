namespace MailHelper.Core.Domain;

/// <summary>账户状态（04 章 §3.1 accounts.status："Active|ReauthRequired|Disabled"）。</summary>
public enum AccountStatus
{
    Active,
    ReauthRequired,
    Disabled,
}

/// <summary>账户（04 章 §3.1 ACCOUNTS 实体）。单账户为 v1.0 范围，多行为 V1.2 演进预留。</summary>
public sealed record Account(
    string Id,
    string Email,
    string? DisplayName,
    string? TenantId,
    ChannelKind Channel,
    string? AzureClientId,        // 自助注册预案时非空（EX-01）
    AccountStatus Status,
    DateTime CreatedAtUtc);
