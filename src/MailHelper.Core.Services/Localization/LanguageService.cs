using System.Globalization;
using MailHelper.Core.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>语言服务（NFR-12）：ui.language（auto/zh-CN/en）→ 内嵌双语字典（D-55）。
/// ApplyAsync 设定当前语言并加载字典；T(key) 查当前语言 → 未知键回落键名。
/// 语言变更在应用重启后完全生效（设置页已提示，D-56）。</summary>
public sealed class LanguageService
{
    private readonly ISettingsStore _store;
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    /// <summary>当前生效语言标签（zh-CN / en）；ApplyAsync 前为默认 zh-CN。</summary>
    public string CurrentTag { get; private set; } = "zh-CN";

    public LanguageService(ISettingsStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>读取 ui.language 并装载对应字典；auto 跟随系统 UI 文化（zh 系 → zh-CN，其余 → en）。
    /// MAILHELPER_LANG 环境变量可在 auto 时强制语言（UI 冒烟测试依赖确定性文案）。</summary>
    public async Task ApplyAsync(CancellationToken ct)
    {
        var tag = await _store.GetAsync("ui.language", ct) is { Length: > 0 } raw ? raw : "auto";
        tag = tag switch
        {
            "zh-CN" or "en" => tag,
            _ => Environment.GetEnvironmentVariable("MAILHELPER_LANG") is { Length: > 0 } forced
                ? forced
                : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                    ? "zh-CN"
                    : "en",
        };
        CurrentTag = tag;
        _strings = Load(tag);
        CultureInfo.CurrentUICulture = new CultureInfo(tag);
    }

    /// <summary>按当前语言取文案；键不存在时返回键名（永不抛出）。</summary>
    public string T(string key) =>
        _strings.TryGetValue(key, out var value) ? value : key;

    private static IReadOnlyDictionary<string, string> Load(string tag) => tag == "en"
        ? MergeFallback(Strings.En, Strings.Zh) // en 缺键回落中文
        : Strings.Zh;

    private static IReadOnlyDictionary<string, string> MergeFallback(
        IReadOnlyDictionary<string, string> primary, IReadOnlyDictionary<string, string> fallback)
    {
        var merged = new Dictionary<string, string>(fallback);
        foreach (var kv in primary)
        {
            merged[kv.Key] = kv.Value;
        }

        return merged;
    }
}
