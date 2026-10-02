using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.SystemIntegration;

/// <summary>IAppPaths macOS 实现（docs/10 §4 P-02）：~/Library/Application Support/MailHelper。
/// 路径为纯托管构造（不调 OS API）→ 契约测试全平台可跑，真实行为由 CI macos job 兜底；
/// overrideDataDir 承载 MAILHELPER_DATA_DIR 覆盖语义（与 Windows 实现一致，测试隔离依赖它）。</summary>
public sealed class MacAppPaths(string? overrideDataDir = null) : IAppPaths
{
    public string DataDir { get; } = overrideDataDir
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "MailHelper");

    public string LogsDir => Path.Combine(DataDir, "logs");

    public string BodiesDir => Path.Combine(DataDir, "bodies");

    public string DbPath => Path.Combine(DataDir, "mailhelper.db");

    public string GetTempFilePath(string fileName) => Path.Combine(Path.GetTempPath(), fileName);
}
