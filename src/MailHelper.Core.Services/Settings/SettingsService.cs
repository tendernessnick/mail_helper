using MailHelper.Core.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>设置服务（04 §7 配置项清单的类型化封装；settings 表经 ISettingsStore）。
/// 键名与默认值逐项对应 04 §7；同步间隔钳制 1–60（02 章 FR-04 度量）。</summary>
public sealed class SettingsService
{
    public const string KeySyncInterval = "sync.interval_minutes";
    public const string KeySyncOnStartup = "sync.on_startup";
    public const string KeyNotifyP0 = "notify.p0_enabled";
    public const string KeyNotifyP1 = "notify.p1_enabled";
    public const string KeyQuietHours = "notify.quiet_hours";
    public const string KeyLanguage = "ui.language";
    public const string KeyTheme = "ui.theme";
    public const string KeyAutostart = "app.autostart";
    public const string KeyBodyLimitMb = "cache.body_limit_mb";
    public const string KeyDiagnostics = "diagnostics.enabled";

    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<int> GetSyncIntervalMinutesAsync(CancellationToken ct)
    {
        var raw = await _store.GetAsync(KeySyncInterval, ct);
        return int.TryParse(raw, out var minutes) ? Math.Clamp(minutes, 1, 60) : 5;
    }

    public async Task SetSyncIntervalMinutesAsync(int minutes, CancellationToken ct) =>
        await _store.SetAsync(KeySyncInterval, Math.Clamp(minutes, 1, 60).ToString(), ct);

    public async Task<bool> IsSyncOnStartupAsync(CancellationToken ct) =>
        await _store.GetAsync(KeySyncOnStartup, ct) is not "false"; // 默认 true（04 §7）

    public Task SetSyncOnStartupAsync(bool enabled, CancellationToken ct) =>
        _store.SetAsync(KeySyncOnStartup, enabled ? "true" : "false", ct);

    /// <summary>level ∈ P0/P1（04 §7 键名 notify.p0_enabled / p1_enabled）。</summary>
    public async Task<bool> GetNotifyEnabledAsync(string level, CancellationToken ct) =>
        await _store.GetAsync($"notify.{level.ToLowerInvariant()}_enabled", ct) is not "false";

    public Task SetNotifyEnabledAsync(string level, bool enabled, CancellationToken ct) =>
        _store.SetAsync($"notify.{level.ToLowerInvariant()}_enabled", enabled ? "true" : "false", ct);

    public async Task<string?> GetQuietHoursAsync(CancellationToken ct) =>
        await _store.GetAsync(KeyQuietHours, ct) is { Length: > 0 } raw ? raw : null;

    public Task SetQuietHoursAsync(string? quietHours, CancellationToken ct) =>
        _store.SetAsync(KeyQuietHours, quietHours ?? string.Empty, ct);

    /// <summary>auto | zh-CN | en（NFR-12；auto 跟随系统 UI 文化）。</summary>
    public async Task<string> GetLanguageAsync(CancellationToken ct) =>
        await _store.GetAsync(KeyLanguage, ct) is { Length: > 0 } raw ? raw : "auto";

    public Task SetLanguageAsync(string language, CancellationToken ct) =>
        _store.SetAsync(KeyLanguage, language, ct);

    public async Task<int> GetBodyLimitMbAsync(CancellationToken ct)
    {
        var raw = await _store.GetAsync(KeyBodyLimitMb, ct);
        return int.TryParse(raw, out var mb) && mb > 0 ? mb : 2048;
    }

    public Task SetBodyLimitMbAsync(int mb, CancellationToken ct) =>
        _store.SetAsync(KeyBodyLimitMb, mb.ToString(), ct);

    public async Task<bool> GetAutostartAsync(CancellationToken ct) =>
        await _store.GetAsync(KeyAutostart, ct) == "true";

    public Task SetAutostartAsync(bool enabled, CancellationToken ct) =>
        _store.SetAsync(KeyAutostart, enabled ? "true" : "false", ct);

    public async Task<bool> IsDiagnosticsAsync(CancellationToken ct) =>
        await _store.GetAsync(KeyDiagnostics, ct) == "true"; // 默认关（04 §6 Debug 级仅诊断模式）

    public Task SetDiagnosticsAsync(bool enabled, CancellationToken ct) =>
        _store.SetAsync(KeyDiagnostics, enabled ? "true" : "false", ct);

    public async Task<string> GetThemeAsync(CancellationToken ct) =>
        await _store.GetAsync(KeyTheme, ct) is { Length: > 0 } raw ? raw : "auto";

    public Task SetThemeAsync(string theme, CancellationToken ct) =>
        _store.SetAsync(KeyTheme, theme, ct);
}
