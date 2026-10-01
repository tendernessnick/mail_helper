using System.IO;
using MailHelper.App.ViewModels;
using MailHelper.Core;
using MailHelper.App.Notifications;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Core.Services.Rules;
using MailHelper.Infrastructure.Auth;
using MailHelper.Infrastructure.Logging;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
using MailHelper.Infrastructure.SystemIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailHelper.App;

/// <summary>通用主机与依赖注入（总控指令四.1；03/04 章分层）。
/// 真实模式：TokenService(MSAL) + GraphMailProvider；DEV 模式（MAILHELPER_DEV=1）：假令牌 + 假邮件通道，
/// 存储与分类全真实——M1 出口「假令牌环境下登录/同步/分类/三栏浏览全链路」（07 章）。
/// 数据目录可用 MAILHELPER_DATA_DIR 覆盖（UI 冒烟测试用独立临时目录）。</summary>
internal static class Bootstrapper
{
    public static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(static (context, services) =>
            {
                var devMode = Environment.GetEnvironmentVariable("MAILHELPER_DEV") == "1";
                var dataDir = Environment.GetEnvironmentVariable("MAILHELPER_DATA_DIR")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MailHelper");
                Directory.CreateDirectory(dataDir);
                var dbPath = Path.Combine(dataDir, "mailhelper.db");

                MailDatabase.EnsureReady(dbPath); // 迁移/WAL/损坏重建（EX-07）
                services.AddSingleton(MailHelperLogging.CreateFileLoggerFactory(Path.Combine(dataDir, "logs")));
                services.AddLogging(); // 复用上方注册的 Serilog ILoggerFactory（04 §6）

                // —— 基础设施 ——
                services.AddSingleton(_ => new MailRepository(dbPath));
                services.AddSingleton<IMessageStore>(sp => sp.GetRequiredService<MailRepository>());
                services.AddSingleton(_ => new AccountRepository(dbPath));
                services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<AccountRepository>());
                services.AddSingleton(_ => new SettingsRepository(dbPath));
                services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsRepository>());
                services.AddSingleton(_ => new CategoryStore(dbPath));
                services.AddSingleton<ICategoryStore>(sp => sp.GetRequiredService<CategoryStore>()); // S14-C/CHG-012
                services.AddSingleton<IBodyCache>(_ => new BodyCacheStore(Path.Combine(dataDir, "bodies")));

                // —— 认证（DEV 假令牌 / 真实 MSAL：MAILHELPER_CLIENT_ID 注入即启用，检查点①）——
                var realClientId = Environment.GetEnvironmentVariable("MAILHELPER_CLIENT_ID");
                var tenantId = Environment.GetEnvironmentVariable("MAILHELPER_TENANT_ID");
                var forceImap = Environment.GetEnvironmentVariable("MAILHELPER_FORCE_IMAP") == "1"; // FR-02 预案 2 强制切换
                var useOutlook = Environment.GetEnvironmentVariable("MAILHELPER_CHANNEL") == "Outlook"; // CHG-011 桌面通道
                if (devMode)
                {
                    services.AddSingleton<ITokenProvider>(_ => new FakeTokenProvider
                    {
                        InteractiveResult = AuthResult.Ok(new AuthToken(
                            "dev-access-token", DateTimeOffset.UtcNow.AddHours(8), "dev-account",
                            "dev@connect.hku.hk", "dev-tenant", new[] { "Mail.Read" })),
                    });
                }
                else
                {
                    services.AddSingleton<ITokenProvider>(_ => new TokenService(
                        realClientId ?? TokenService.PlaceholderClientId, // 检查点①：经环境变量注入
                        Path.Combine(dataDir, "tokens"),
                        scopes: forceImap ? [ImapMailProvider.ImapScope] : null, // 预案 2：IMAP scope 交互登录
                        tenantId: tenantId,
                        redirectUri: tenantId is { Length: > 0 } ? "http://localhost" : null)); // 单租户验证：loopback 免协议注册
                }

                services.AddSingleton<AuthService>();

                // —— 规则与分类（内置 JSON + rules 表用户/反馈规则合并）——
                services.AddSingleton(_ => LoadRuleEngine(dbPath));
                services.AddSingleton<IClassifier>(sp => sp.GetRequiredService<RuleEngine>());

                // —— 同步（DEV 假通道带种子数据 / 真实 Graph REST 或 IMAP 兜底，按账户通道 FR-02）——
                if (devMode)
                {
                    services.AddSingleton<IMailProvider>(_ => DevSeed.BuildMailProvider());
                }
                else if (useOutlook)
                {
                    // CHG-011 落地路径调整：复用经典 Outlook 本机登录态（COM），绕开租户 OAuth 同意限制
                    services.AddSingleton<IMailProvider>(sp => new OutlookDesktopMailProvider(
                        new OutlookComMailSource(),
                        sp.GetRequiredService<IBodyCache>(),
                        cacheAccountKey: "acc-1",
                        sp.GetRequiredService<ILogger<OutlookDesktopMailProvider>>()));
                }
                else if (forceImap)
                {
                    // FR-02 AC1 预案 2 强制通道：绕过账户记录直接走 IMAP（诊断/验证用）
                    services.AddSingleton<IMailProvider>(sp => new ImapMailProvider(
                        sp.GetRequiredService<ITokenProvider>(),
                        () => new ImapKitClientAdapter(),
                        Environment.GetEnvironmentVariable("MAILHELPER_IMAP_USER")
                            ?? throw new InvalidOperationException("IMAP 通道需 MAILHELPER_IMAP_USER 指定登录邮箱"),
                        sp.GetRequiredService<ILogger<ImapMailProvider>>()));
                }
                else
                {
                    var channel = new AccountRepository(dbPath)
                        .FindAllAsync(CancellationToken.None).GetAwaiter().GetResult()
                        .FirstOrDefault()?.Channel ?? ChannelKind.Graph;
                    if (channel == ChannelKind.Imap)
                    {
                        // FR-02 AC1 预案 2：Graph 被拒时切换 IMAP XOAUTH2（MailKit，04 §4.3）
                        services.AddSingleton<IMailProvider>(sp =>
                        {
                            var accountEmail = new AccountRepository(dbPath)
                                .FindAllAsync(CancellationToken.None).GetAwaiter().GetResult()
                                .First(a => a.Channel == ChannelKind.Imap).Email;
                            var imapTokens = new TokenService(
                                realClientId ?? TokenService.PlaceholderClientId,
                                Path.Combine(dataDir, "tokens"),
                                [ImapMailProvider.ImapScope],
                                tenantId: tenantId,
                                redirectUri: tenantId is { Length: > 0 } ? "http://localhost" : null);
                            return new ImapMailProvider(
                                imapTokens,
                                () => new ImapKitClientAdapter(),
                                accountEmail,
                                sp.GetRequiredService<ILogger<ImapMailProvider>>());
                        });
                    }
                    else
                    {
                        services.AddSingleton<IMailProvider>(sp => new GraphMailProvider(
                            sp.GetRequiredService<ITokenProvider>(),
                            options: new GraphHttpOptions()));
                    }
                }

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
                services.AddSingleton(_ => new NotificationRepository(dbPath));
                services.AddSingleton<INotificationStore>(sp => sp.GetRequiredService<NotificationRepository>());
                services.AddSingleton<IToastSender>(_ => new ToastSender());
                services.AddSingleton<NotificationService>();

                // —— 反馈闭环（FR-11）——
                services.AddSingleton(_ => new FeedbackRepository(dbPath));
                services.AddSingleton<IFeedbackStore>(sp => sp.GetRequiredService<FeedbackRepository>());
                services.AddSingleton(_ => new RuleRepository(dbPath));
                services.AddSingleton<IRulesStore>(sp => sp.GetRequiredService<RuleRepository>());
                services.AddSingleton<FeedbackService>();

                // —— 设置 / 规则管理 / 语言 / 更新（S9：FR-03/04/10、NFR-12；S15：08 §4.3）——
                services.AddSingleton<SettingsService>();
                services.AddSingleton<Updates.UpdateService>();
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
                services.AddSingleton(sp => new SettingsViewModel(
                    sp.GetRequiredService<SettingsService>(),
                    sp.GetRequiredService<AuthService>(),
                    sp.GetRequiredService<IAccountStore>(),
                    onSyncIntervalChanged: RestartPeriodicSync,
                    updates: sp.GetRequiredService<Updates.UpdateService>(),
                    setAutostartAsync: enabled =>
                    {
                        var autostart = new AutostartService(
                            @"Software\Microsoft\Windows\CurrentVersion\Run", "MailHelper");
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

                // —— UI ——
                services.AddSingleton(sp => new MainViewModel(
                    sp.GetRequiredService<AuthService>(),
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
                    devMode));
                services.AddSingleton<MainWindow>();
                services.AddSingleton<RulesPage>();
                services.AddSingleton<SettingsPage>();
            })
            .Build();

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
