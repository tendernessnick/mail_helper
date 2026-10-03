using System.IO;
using MailHelper.ViewModels;
using MailHelper.Core;
using MailHelper.App.Notifications;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailHelper.App;

/// <summary>通用主机与依赖注入（总控指令四.1；03/04 章分层）。
/// 真实模式：唯一通道 = 本机经典版 Outlook 登录态（OutlookDesktopMailProvider，COM 直读，CHG-013）；
/// DEV 模式（MAILHELPER_DEV=1）：假邮件通道 + 种子数据，存储与分类全真实。
/// 数据目录可用 MAILHELPER_DATA_DIR 覆盖（UI 冒烟测试用独立临时目录）。
/// 平台件经接口装配（MS1，docs/10 §4）：IAppPaths / ISingleInstanceLock / IAutoStarter / IUpdateInstaller
/// 的 Windows 实现注册于此；macOS 版（Avalonia）以同接口换实现。</summary>
internal static class Bootstrapper
{
    public static IHost BuildHost(ISingleInstanceLock singleInstanceLock)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) => RegisterServices(context, services, singleInstanceLock))
            .Build();
    }

    private static void RegisterServices(HostBuilderContext context, IServiceCollection services, ISingleInstanceLock singleInstanceLock)
    {
        var devMode = Environment.GetEnvironmentVariable("MAILHELPER_DEV") == "1";
        var paths = new WindowsAppPaths(Environment.GetEnvironmentVariable("MAILHELPER_DATA_DIR"));
        Directory.CreateDirectory(paths.DataDir);

        MailDatabase.EnsureReady(paths.DbPath); // 迁移/WAL/损坏重建（EX-07）
        services.AddSingleton(MailHelperLogging.CreateFileLoggerFactory(paths.LogsDir));
        services.AddLogging(); // 复用上方注册的 Serilog ILoggerFactory（04 §6）

        // —— 平台件（MS1 接口 + MS2 主线程调度器：Windows 实现注册于此，Avalonia 换实现）——
        services.AddSingleton<IAppPaths>(_ => paths); // 显式接口类型：否则注册为具体类型，GetRequiredService<IAppPaths> 会炸
        services.AddSingleton(singleInstanceLock);
        services.AddSingleton<IAutoStarter>(_ => new AutostartService(
            @"Software\Microsoft\Windows\CurrentVersion\Run", "MailHelper"));
        services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>();
        services.AddSingleton<IMainThreadDispatcher>(_ => WpfMainThreadDispatcher.CreateForCurrentThread()); // MS2：UI 线程构造（BuildHost 在 OnStartup 调用）；显式接口类型同上

        // —— 基础设施 ——
        services.AddSingleton(_ => new MailRepository(paths.DbPath));
        services.AddSingleton<IMessageStore>(sp => sp.GetRequiredService<MailRepository>());
        services.AddSingleton(_ => new AccountRepository(paths.DbPath));
        services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<AccountRepository>());
        services.AddSingleton(_ => new SettingsRepository(paths.DbPath));
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsRepository>());
        services.AddSingleton(_ => new CategoryStore(paths.DbPath));
        services.AddSingleton<ICategoryStore>(sp => sp.GetRequiredService<CategoryStore>()); // S14-C/CHG-012
        services.AddSingleton<IBodyCache>(_ => new BodyCacheStore(paths.BodiesDir));

        // —— 通道（CHG-013 唯一通道：本机经典版 Outlook 登录态，COM 直读；DEV 假通道带种子数据）——
        services.AddSingleton<IMailProvider>(sp => devMode
            ? DevSeed.BuildMailProvider()
            : new OutlookDesktopMailProvider(
                new OutlookComMailSource(),
                sp.GetRequiredService<IBodyCache>(),
                cacheAccountKey: "acc-1",
                sp.GetRequiredService<ILogger<OutlookDesktopMailProvider>>()));

        // —— 规则与分类（内置 JSON + rules 表用户/反馈规则合并）——
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

        // —— 通知（FR-14：04 §8 决策 + 04 §4 Toast 实现）——
        services.AddSingleton(_ => new NotificationRepository(paths.DbPath));
        services.AddSingleton<INotificationStore>(sp => sp.GetRequiredService<NotificationRepository>());
        services.AddSingleton<IToastSender>(_ => new ToastSender());
        services.AddSingleton<NotificationService>();

        // —— 反馈闭环（FR-11）——
        services.AddSingleton(_ => new FeedbackRepository(paths.DbPath));
        services.AddSingleton<IFeedbackStore>(sp => sp.GetRequiredService<FeedbackRepository>());
        services.AddSingleton(_ => new RuleRepository(paths.DbPath));
        services.AddSingleton<IRulesStore>(sp => sp.GetRequiredService<RuleRepository>());
        services.AddSingleton<FeedbackService>();

        // —— 日程（S18/CHG-015：Canvas DDL 识别 + 提醒；与 Avalonia 版逐项一致）——
        services.AddSingleton(_ => new ScheduleRepository(paths.DbPath));
        services.AddSingleton<IScheduleStore>(sp => sp.GetRequiredService<ScheduleRepository>());
        services.AddSingleton(sp => new ScheduleService(
            sp.GetRequiredService<IMessageStore>(),
            sp.GetRequiredService<IScheduleStore>(),
            sp.GetRequiredService<IBodyCache>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IToastSender>(),
            "acc-1",
            sp.GetRequiredService<ILogger<ScheduleService>>()));

        // —— 设置 / 规则管理 / 语言 / 更新（S9：FR-03/04/10、NFR-12；S15：08 §4.3）——
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
            sp.GetRequiredService<IMessageStore>(), "acc-1")); // MOD-09（S10）

        // —— 页面视图模型（05 §2 Shell 导航）——
        services.AddSingleton<RulesViewModel>();
        services.AddSingleton<ScheduleViewModel>(); // S18/CHG-015 日程页
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<SettingsService>(),
            sp.GetRequiredService<IAccountStore>(),
            onSyncIntervalChanged: RestartPeriodicSync,
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
                    },
                    writeCrashLog: App.WriteCrashLog));

        // —— UI ——
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
        services.AddSingleton<MainWindow>();
        services.AddSingleton<RulesPage>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<SchedulePage>(); // S18/CHG-015 日程页
    }

    /// <summary>FR-04 周期同步热更新：设置页改间隔后重启循环（App 持 CTS）。</summary>
    internal static void RestartPeriodicSync(int intervalMinutes) => PeriodicSyncChanged?.Invoke(intervalMinutes);

    internal static event Action<int>? PeriodicSyncChanged;

    /// <summary>规则引擎装配：内置规则包（随包 JSON，缺失/损坏回退空集，09 §2）∪ rules 表用户/反馈规则
    /// （FR-11：反馈规则重启后持续生效）。Id 全局唯一不冲突；同发件人规则并存时反馈 ×1.5 权重胜出。</summary>
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

        // S13-B：内置包版本变化→rule 来源重分类 + 断点清空（全量重拉回填修复期缺失的发件地址）
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
