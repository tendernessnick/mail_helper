// 领域枚举 —— 签名与 04 章 §2.1 完全一致（总控指令三.4：接口/字段不得偏离）。
namespace MailHelper.Core;

/// <summary>邮件七类别（02 章附录 A：课程学习/职业发展/校园事务/财务缴费/通知公告/订阅营销/其他）。</summary>
public enum MailCategory
{
    Course,
    Career,
    Admin,
    Finance,
    Announce,
    Subscription,
    Other,
}

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
}

/// <summary>远端变更类型（04 章 §2.1 RemoteMessage.Kind：Added/Updated/Removed）。</summary>
public enum ChangeKind
{
    Added,
    Updated,
    Removed,
}
