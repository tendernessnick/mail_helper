using System.IO;
using System.Runtime.Versioning;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Core.Services.Rules;
using MailHelper.Infrastructure.Logging;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using MailHelper.Infrastructure.SystemIntegration;
using MailHelper.Infrastructure.Updates;
using MailHelper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailHelper.App.Avalonia;

/// <summary>通用主机与依赖注入（Avalonia 版，MS3）：与 WPF Bootstrapper 同构——
/// 领域/服务/存储注册逐项一致，仅平台件与 UI 类型按本 UI 就位。
/// 真实模式：唯一通道 = 本机经典版 Outlook 登录态（OutlookDesktopMailProvider，CHG-013）；
/// DEV 模式（MAILHELPER_DEV=1）：DevSeed 假通道 + 种子数据。
/// 平台件演进：MS4 起 IAppPaths/IAutoStarter/ISingleInstanceLock 按 OS 分支（Windows 现 / Mac 新实现）；
/// MS9 起 IUpdateInstaller 增 Mac 引导安装实现。</summary>
internal static class AvaloniaBootstrapper
{
    public static IHost BuildHost(ISingleInstanceLock singleInstanceLock)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) => RegisterServices(services, singleInstanceLock))
            .Build();
    }

    private static void RegisterServices(IServiceCollection services, ISingleInstanceLock singleInstanceLock)
    {
        // 平台件 OS 分支立桩（docs/10 §8.2；MS4 落地 Mac 实现后改为双分支装配）：
        // MS3 骨架仅支持 Windows 预览运行；osx 双架构为交叉编译发布证据，不在 Mac 上运行
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "MailHelper（Avalonia）当前仅支持 Windows 预览；macOS 平台件随 MS4 落地（docs/10 §4 P-02/03/04）");
        }

        RegisterWindowsServices(services, singleInstanceLock);
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindowsServices(IServiceCollection services, ISingleInstanceLock singleInstanceLock)
    {
        var devMode = Environment.GetEnvironmentVariable("MAILHELPER_DEV") == "1";
        var paths = new WindowsAppPaths(Environment.GetEnvironmentVariable("MAILHELPER_DATA_DIR"));
        Directory.CreateDirectory(paths.DataDir);

        MailDatabase.EnsureReady(paths.DbPath);
        services.AddSingleton(MailHelperLogging.CreateFileLoggerFactory(paths.LogsDir));
        services.AddLogging();

        // —— 平台件（与 WPF 版同名接口；Avalonia 主线程调度器为本 UI 实现）——
        services.AddSingleton<IAppPaths>(_ => paths);
        services.AddSingleton(singleInstanceLock);
        services.AddSingleton<IAutoStarter>(_ => new AutostartService(
            @"Software\Microsoft\Windows\CurrentVersion\Run", "MailHelper")); // MS4：mac=LaunchAgent
        services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>(); // MS9：mac=引导下载 DMG
        services.AddSingleton<IMainThreadDispatcher>(_ => new AvaloniaMainThreadDispatcher());

        // —— 基础设施（与 WPF 版逐项一致）——
        services.AddSingleton(_ => new MailRepository(paths.DbPath));
        services.AddSingleton<IMessageStore>(sp => sp.GetRequiredService<MailRepository>());
        services.AddSingleton(_ => new AccountRepository(paths.DbPath));
        services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<AccountRepository>());
        services.AddSingleton(_ => new SettingsRepository(paths.DbPath));
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsRepository>());
        services.AddSingleton(_ => new CategoryStore(paths.DbPath));
        services.AddSingleton<ICategoryStore>(sp => sp.GetRequiredService<CategoryStore>());
        services.AddSingleton<IBodyCache>(_ => new BodyCacheStore(paths.BodiesDir));

        // —— 通道（CHG-013 唯一通道；DEV 假通道带种子数据；MS8 增 OutlookMac 假通道注入点）——
        services.AddSingleton<IMailProvider>(sp => devMode
            ? DevSeed.BuildMailProvider()
            : new OutlookDesktopMailProvider(
                new OutlookComMailSource(),
                sp.GetRequiredService<IBodyCache>(),
                cacheAccountKey: "acc-1",
                sp.GetRequiredService<ILogger<OutlookDesktopMailProvider>>()));

        // —— 规则与分类 ——
        services.AddSingleton(_ => LoadRuleEngine(paths.DbPath));
        services.AddSingleton<IClassifier>(sp => sp.GetRequiredService<RuleEngine>());

        services.AddSingleton(sp => new SyncCoordinator(
            sp.GetRequiredService<IMailProvider>(),
            sp.GetRequiredService<IMessageStore>(),
            sp.GetRequiredService<IAccountStore>(),
            "acc-1",
            sp.GetRequiredService<ILogger<SyncCoordinator>>()));

        services.AddSingleton(sp => new ClassificationService(
            sp.GetRequiredService<IClassifier>(),
            sp.GetRequiredService<IMessageStore>(),
            "acc-1",
            sp.GetRequiredService<ILogger<ClassificationService>>()));

        // —— 通知（决策层复用；MS6 接 osascript 发送实现）——
        services.AddSingleton(_ => new NotificationRepository(paths.DbPath));
        services.AddSingleton<INotificationStore>(sp => sp.GetRequiredService<NotificationRepository>());
        services.AddSingleton<NotificationService>();

        // —— 反馈闭环 ——
        services.AddSingleton(_ => new FeedbackRepository(paths.DbPath));
        services.AddSingleton<IFeedbackStore>(sp => sp.GetRequiredService<FeedbackRepository>());
        services.AddSingleton(_ => new RuleRepository(paths.DbPath));
        services.AddSingleton<IRulesStore>(sp => sp.GetRequiredService<RuleRepository>());
        services.AddSingleton<FeedbackService>();

        // —— 设置 / 规则管理 / 语言 / 搜索 ——
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton(sp => new RuleManagementService(
            sp.GetRequiredService<IRulesStore>(),
            sp.GetRequiredService<RuleEngine>(),
            sp.GetRequiredService<IMessageStore>(),
            "acc-1",
            sp.GetRequiredService<ILogger<RuleManagementService>>()));
        services.AddSingleton<LanguageService>();
        services.AddSingleton<SearchService>(sp => new SearchService(
            sp.GetRequiredService<IMessageStore>(), "acc-1"));

        // —— 共享 ViewModel（MailHelper.ViewModels，MS2）——
        services.AddSingleton<RulesViewModel>();
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<SettingsService>(),
            sp.GetRequiredService<IAccountStore>(),
            onSyncIntervalChanged: _ => { /* MS6：周期同步热更新接线 */ },
            updates: sp.GetRequiredService<UpdateService>(),
            devMode: devMode,
            setAutostartAsync: enabled =>
            {
                var autostart = sp.GetRequiredService<IAutoStarter>();
                if (enabled)
                {
                    autostart.Enable();
                }
                else
                {
                    autostart.Disable();
                }

                return Task.CompletedTask;
            }));

        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<SyncCoordinator>(),
            sp.GetRequiredService<ClassificationService>(),
            sp.GetRequiredService<FeedbackService>(),
            sp.GetRequiredService<SearchService>(),
            sp.GetRequiredService<IMessageStore>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IBodyCache>(),
            sp.GetRequiredService<IAccountStore>(),
            sp.GetRequiredService<IMailProvider>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            devMode,
            sp.GetRequiredService<IMainThreadDispatcher>()));

        // —— UI（本 App 专有）——
        services.AddSingleton<ShellWindow>();
    }

    /// <summary>规则引擎装配：与 WPF Bootstrapper.LoadRuleEngine 同构（内置包 ∪ rules 表用户/反馈规则）。</summary>
    private static RuleEngine LoadRuleEngine(string dbPath)
    {
        RuleSet builtin = RuleSet.Empty;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "rules.builtin.json"),
            Path.Combine(AppContext.BaseDirectory, "Rules", "rules.builtin.json"),
        };
        foreach (var path in candidates)
        {
            if (RuleSetParser.TryParseFile(path, out var ruleSet, out _) && ruleSet is not null)
            {
                builtin = ruleSet;
                break;
            }
        }

        new RulePackMigrator(new MailRepository(dbPath), new AccountRepository(dbPath), new SettingsRepository(dbPath))
            .MigrateIfNeededAsync(builtin.Version ?? "0", "acc-1", CancellationToken.None)
            .GetAwaiter().GetResult();

        var storedRules = new RuleRepository(dbPath)
            .GetAllAsync(CancellationToken.None).GetAwaiter().GetResult()
            .Where(r => r.Enabled && r.Source is RuleSource.User or RuleSource.Feedback);
        var merged = new RuleSet(builtin.Version, builtin.Scoring,
            builtin.Rules.Concat(storedRules).ToList());
        return new RuleEngine(merged);
    }
}
