using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Services;

namespace MailHelper.App.ViewModels;

/// <summary>设置页（FR-03/04/06、05 §3.4 五分组）：账户/同步/通知/外观/高级。
/// 值变更即时写设置表；开机自启同步写注册表 Run 键（TC-020）；同步间隔变更重启周期循环（FR-04）。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IAccountStore _accounts;
    private readonly Action<int> _onSyncIntervalChanged; // 周期循环热更新（FR-04）
    private readonly Updates.UpdateService _updates;
    private readonly bool _devMode;
    private string? _pendingInstallerPath; // 已下载待安装的更新包（S17）

    /// <summary>autostart 读写委托（App 层注入注册表实现，TC-020）。</summary>
    public Func<bool, Task>? SetAutostartAsync { get; init; }

    /// <summary>静默升级启动后由 App 关闭应用；安装器完成覆盖安装并自动重启（S17）。</summary>
    public Action? ShutdownCallback { get; set; }

    public SettingsViewModel(
        SettingsService settings,
        IAccountStore accounts,
        Action<int> onSyncIntervalChanged,
        Updates.UpdateService updates,
        bool devMode = false,
        Func<bool, Task>? setAutostartAsync = null)
    {
        _settings = settings;
        _accounts = accounts;
        _onSyncIntervalChanged = onSyncIntervalChanged;
        _updates = updates;
        _devMode = devMode;
        SetAutostartAsync = setAutostartAsync;
    }

    // —— 账户（FR-03）——
    [ObservableProperty]
    private string accountEmail = "—";

    [ObservableProperty]
    private string channel = "经典版 Outlook";

    [ObservableProperty]
    private string lastSync = "—";

    [ObservableProperty]
    private bool hasAccount;

    // —— 同步 ——
    [ObservableProperty]
    private int intervalMinutes = 5;

    [ObservableProperty]
    private bool syncOnStartup = true;

    // —— 通知（FR-14 开关落 04 §7 键）——
    [ObservableProperty]
    private bool notifyP0 = true;

    [ObservableProperty]
    private bool notifyP1 = true;

    [ObservableProperty]
    private string quietHours = string.Empty;

    // —— 外观 ——
    [ObservableProperty]
    private string language = "auto"; // auto | zh-CN | en（NFR-12）

    [ObservableProperty]
    private string theme = "auto";

    // —— 高级 ——
    [ObservableProperty]
    private bool autostart;

    [ObservableProperty]
    private bool diagnostics;

    [ObservableProperty]
    private int bodyLimitMb = 2048;

    // —— 关于/更新（S17，Inno 安装包路线）——
    [ObservableProperty]
    private string appVersion = Updates.UpdateService.CurrentVersion;

    [ObservableProperty]
    private string updateStatus = string.Empty;

    [ObservableProperty]
    private bool isCheckingUpdate;

    /// <summary>更新包已下载，展示「重启并安装更新」按钮。</summary>
    [ObservableProperty]
    private bool updateReady;

    /// <summary>开发模式（源码运行）下禁用入口。</summary>
    [ObservableProperty]
    private bool canCheckUpdate;

    public async Task LoadAsync(CancellationToken ct)
    {
        IntervalMinutes = await _settings.GetSyncIntervalMinutesAsync(ct);
        SyncOnStartup = await _settings.IsSyncOnStartupAsync(ct);
        NotifyP0 = await _settings.GetNotifyEnabledAsync("P0", ct);
        NotifyP1 = await _settings.GetNotifyEnabledAsync("P1", ct);
        QuietHours = await _settings.GetQuietHoursAsync(ct) ?? string.Empty;
        Language = await _settings.GetLanguageAsync(ct);
        Theme = await _settings.GetThemeAsync(ct);
        Diagnostics = await _settings.IsDiagnosticsAsync(ct);
        BodyLimitMb = await _settings.GetBodyLimitMbAsync(ct);

        var account = (await _accounts.FindAllAsync(ct)).FirstOrDefault();
        HasAccount = account is not null;
        if (account is { } a)
        {
            AccountEmail = a.Email;
            Channel = a.Channel switch // FR-03：接入通道显示（CHG-013 起恒为经典版 Outlook）
            {
                ChannelKind.OutlookDesktop => "经典版 Outlook",
                ChannelKind.Imap => "IMAP",
                ChannelKind.Graph => "Graph",
                _ => a.Channel.ToString(),
            };
            var checkpoint = await _accounts.GetCheckpointAsync(a.Id, ct);
            LastSync = checkpoint?.LastSyncAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
        }
    }

    partial void OnIntervalMinutesChanged(int value)
    {
        if (value is >= 1 and <= 60)
        {
            _ = _settings.SetSyncIntervalMinutesAsync(value, CancellationToken.None);
            _onSyncIntervalChanged(value); // FR-04：新间隔即刻生效
        }
    }

    partial void OnSyncOnStartupChanged(bool value) =>
        _ = _settings.SetSyncOnStartupAsync(value, CancellationToken.None);

    partial void OnNotifyP0Changed(bool value) =>
        _ = _settings.SetNotifyEnabledAsync("P0", value, CancellationToken.None);

    partial void OnNotifyP1Changed(bool value) =>
        _ = _settings.SetNotifyEnabledAsync("P1", value, CancellationToken.None);

    partial void OnQuietHoursChanged(string value) =>
        _ = _settings.SetQuietHoursAsync(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), CancellationToken.None);

    partial void OnLanguageChanged(string value) =>
        _ = _settings.SetLanguageAsync(value, CancellationToken.None);

    partial void OnThemeChanged(string value) =>
        _ = _settings.SetThemeAsync(value, CancellationToken.None);

    partial void OnDiagnosticsChanged(bool value) =>
        _ = _settings.SetDiagnosticsAsync(value, CancellationToken.None);

    partial void OnBodyLimitMbChanged(int value)
    {
        if (value > 0)
        {
            _ = _settings.SetBodyLimitMbAsync(value, CancellationToken.None);
        }
    }

    /// <summary>开机自启开关（UI 绑定；写注册表由 App 注入，TC-020）。</summary>
    public async Task ToggleAutostartAsync(bool enabled)
    {
        Autostart = enabled;
        if (SetAutostartAsync is not null)
        {
            await SetAutostartAsync(enabled);
        }

        _ = _settings.SetAutostartAsync(enabled, CancellationToken.None); // 04 §7：设置值与注册表同步
    }

    /// <summary>检查更新（S17）：有新版即后台下载完整安装包（字节进度），完成后交由用户确认静默升级。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync(CancellationToken ct)
    {
        if (IsCheckingUpdate || !CanCheckUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        UpdateReady = false;
        _pendingInstallerPath = null;
        UpdateStatus = "正在检查更新…";
        try
        {
            var info = await _updates.CheckForUpdateAsync(ct);
            if (info is null)
            {
                UpdateStatus = $"当前已是最新版本（v{AppVersion}）";
                return;
            }

            UpdateStatus = $"发现新版本 v{info.Version}，正在后台下载…";
            _pendingInstallerPath = await _updates.DownloadUpdateAsync(
                info,
                (read, total) => UpdateStatus = total > 0
                    ? $"正在下载更新… {read * 100 / total}%"
                    : $"正在下载更新… {read / 1024 / 1024} MB",
                CancellationToken.None); // 下载不随 UI 取消中断，进度照常汇报
            UpdateReady = true;
            UpdateStatus = $"v{info.Version} 已就绪，点「重启并安装更新」完成升级（自动重启应用）";
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败，请确认网络后重试";
            App.WriteCrashLog("CheckUpdate", ex);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>启动静默升级：安装器同目录覆盖安装并自动重启应用，本进程随即退出。</summary>
    [RelayCommand]
    private void ApplyUpdate()
    {
        if (_pendingInstallerPath is not { } path || !File.Exists(path))
        {
            return;
        }

        _updates.InstallSilently(path);
        ShutdownCallback?.Invoke();
    }
}
