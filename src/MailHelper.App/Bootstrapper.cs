using System.IO;
using MailHelper.App.ViewModels;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Auth;
using MailHelper.Infrastructure.Logging;
using MailHelper.Infrastructure.Storage;
using MailHelper.Infrastructure.Sync;
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
                services.AddSingleton<IBodyCache>(_ => new BodyCacheStore(Path.Combine(dataDir, "bodies")));

                // —— 认证（DEV 假令牌 / 真实 MSAL）——
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
                    services.AddSingleton<ITokenProvider>(_ => new TokenService(TokenService.PlaceholderClientId,
                        Path.Combine(dataDir, "tokens"))); // 检查点①：ClientId 待配置
                }

                services.AddSingleton<AuthService>();

                // —— 规则与分类 ——
                services.AddSingleton(_ => LoadRuleEngine());
                services.AddSingleton<IClassifier>(sp => sp.GetRequiredService<RuleEngine>());

                // —— 同步（DEV 假通道带种子数据 / 真实 Graph REST）——
                if (devMode)
                {
                    services.AddSingleton<IMailProvider>(_ => DevSeed.BuildMailProvider());
                }
                else
                {
                    services.AddSingleton<IMailProvider>(sp => new GraphMailProvider(
                        sp.GetRequiredService<ITokenProvider>(),
                        options: new GraphHttpOptions()));
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

                // —— UI ——
                services.AddSingleton(sp => new MainViewModel(
                    sp.GetRequiredService<AuthService>(),
                    sp.GetRequiredService<SyncCoordinator>(),
                    sp.GetRequiredService<ClassificationService>(),
                    sp.GetRequiredService<IMessageStore>(),
                    sp.GetRequiredService<ISettingsStore>(),
                    sp.GetRequiredService<IBodyCache>(),
                    sp.GetRequiredService<IAccountStore>(),
                    sp.GetRequiredService<ILogger<MainViewModel>>(),
                    devMode));
                services.AddSingleton<MainWindow>();
            })
            .Build();

    /// <summary>预置规则包加载：随包文件优先，缺失/损坏回退空规则集（09 §2 回退策略）。</summary>
    private static RuleEngine LoadRuleEngine()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "rules.builtin.json"),
            Path.Combine(AppContext.BaseDirectory, "Rules", "rules.builtin.json"),
        };
        foreach (var path in candidates)
        {
            if (RuleSetParser.TryParseFile(path, out var ruleSet, out _) && ruleSet is not null)
            {
                return new RuleEngine(ruleSet);
            }
        }

        return new RuleEngine(RuleSet.Empty);
    }
}
