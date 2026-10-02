using MailHelper.Core;
using MailHelper.Core.Abstractions;

namespace MailHelper.ViewModels;

/// <summary>类别目录（S14-C/CHG-012）：内置七类 + 自定义类别的进程内缓存。
/// 启动与类别变更后 Refresh；规则编辑/改判/分类栏统一从此取定义。</summary>
public static class CategoryCatalog
{
    public static IReadOnlyList<CategoryDefinition> All { get; private set; } = CategoryIds.Defaults;

    /// <summary>从存储重载（自定义部分）；内置定义恒以代码为准。</summary>
    public static async Task RefreshAsync(ICategoryStore store, CancellationToken ct = default)
    {
        var custom = await store.GetAllAsync(ct);
        All = CategoryIds.Defaults.Concat(custom.OrderBy(c => c.Sort)).ToArray();
    }

    public static CategoryDefinition? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(c => c.Id.Equals(id, StringComparison.Ordinal));

    public static string LabelOf(string id) => Find(id)?.Label ?? id;

    public static string IconOf(string id) => Find(id)?.Icon ?? "🗂";

    public static string ColorOf(string id) => Find(id)?.ColorHex ?? "#64748B";
}

/// <summary>列表查找扩展（索引不存在返回 -1）。</summary>
public static class CategoryCatalogExtensions
{
    public static int IndexIf<T>(this IReadOnlyList<T> list, Func<T, bool> predicate)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (predicate(list[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
