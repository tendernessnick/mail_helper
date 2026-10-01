// 类别注册表 —— S14-C 自定义类别（CHG-012：02 章附录 A「七类固定」的受控扩展）。
// 内置七类 ID 与历史 DB 值/规则包/评估集完全一致（零数据迁移）；自定义类别经 categories 表管理。
namespace MailHelper.Core;

/// <summary>类别定义（内置 + 用户自定义的统一形态；ColorHex 形如 #3A7BD5）。</summary>
public sealed record CategoryDefinition(
    string Id,
    string Label,
    string Icon,
    string ColorHex,
    int Sort,
    bool IsBuiltin);

/// <summary>内置类别 ID 常量与默认定义（02 章附录 A 七类；ID=历史存储值，不可改）。</summary>
public static class CategoryIds
{
    public const string Course = "course";
    public const string Career = "career";
    public const string Admin = "admin";
    public const string Finance = "finance";
    public const string Announce = "announce";
    public const string Subscription = "subscription";
    public const string Other = "other";

    /// <summary>内置七 ID（SanityTests 锁定此列表；顺序=分类栏默认排序）。</summary>
    public static readonly string[] All =
        [Course, Career, Admin, Finance, Announce, Subscription, Other];

    /// <summary>内置默认定义（图标与类别色与 S13 设计令牌一致）。</summary>
    public static readonly CategoryDefinition[] Defaults =
    [
        new(Course, "课程学习", "📚", "#3A7BD5", 1, true),
        new(Career, "职业发展", "💼", "#0E9F6E", 2, true),
        new(Admin, "校园事务", "🏫", "#7C5CDB", 3, true),
        new(Finance, "财务缴费", "💰", "#D97706", 4, true),
        new(Announce, "通知公告", "📢", "#0E7490", 5, true),
        new(Subscription, "订阅营销", "📨", "#8C93A0", 6, true),
        new(Other, "其他", "🗂", "#64748B", 7, true),
    ];
}
