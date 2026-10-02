// 领域枚举 —— 签名与 04 章 §2.1 完全一致（总控指令三.4：接口/字段不得偏离）。
namespace MailHelper.Core;

// 邮件类别自 S14-C（CHG-012）起由枚举升级为「类别注册表」：内置七 ID 见 Categories.cs（CategoryIds），
// 用户自定义类别经 ICategoryStore 管理——DB/规则包/评估集的存量字符串值完全兼容。

/// <summary>重要程度四级；数值越大越重要（04 章 §2.1，与 DDL「importance INTEGER 0..3」对应）。</summary>
public enum Importance
{
    P3 = 0,
    P2 = 1,
    P1 = 2,
    P0 = 3,
}

/// <summary>邮件接入通道（04 章 §2.1：Graph 主通道 / IMAP 兜底）。</summary>
public enum ChannelKind
{
    Graph,
    Imap,
    OutlookDesktop, // CHG-011：复用经典 Outlook 本机登录态（COM），绕开租户 OAuth 同意限制
    OutlookMac,     // CHG-014：macOS 侧复用 Outlook for Mac（经典版界面）登录态直读（设计见 docs/10 ADR-007；实现细节只入基础设施层）
}

/// <summary>远端变更类型（04 章 §2.1 RemoteMessage.Kind：Added/Updated/Removed）。</summary>
public enum ChangeKind
{
    Added,
    Updated,
    Removed,
}
