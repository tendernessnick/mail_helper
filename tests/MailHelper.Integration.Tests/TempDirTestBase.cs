using MailHelper.Core;
using MailHelper.Core.Domain;
using Microsoft.Data.Sqlite;

namespace MailHelper.Integration.Tests;

/// <summary>临时目录测试基类：每测试类独立目录 + 退出时释放 SQLite 连接池（保证临时目录可删）。</summary>
public abstract class TempDirTestBase : IDisposable
{
    protected string Dir { get; } = Path.Combine(Path.GetTempPath(), "mh-store-" + Guid.NewGuid().ToString("N"));

    protected string DbPath => Path.Combine(Dir, "mailhelper.db");

    protected TempDirTestBase() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected static Account NewAccount(string id = "acc-1") => new(
        id, $"{id}@connect.hku.hk", "测试账户", "tenant-1", ChannelKind.Graph,
        AzureClientId: null, Status: AccountStatus.Active,
        CreatedAtUtc: new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));

    protected static MailMessage NewMessage(
        string id,
        string accountId = "acc-1",
        string subject = "Hello",
        string from = "someone@hku.hk",
        string? preview = null,
        DateTime? received = null,
        bool isRead = false,
        string fromName = "Sender") => new(
        id, accountId, $"<{id}@im.example>", subject, fromName, from,
        preview ?? $"preview of {id}", BodyPath: null,
        received ?? new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc),
        HasAttachments: false, IsRead: isRead,
        Category: CategoryIds.Other, Importance: Importance.P2,
        Confidence: null, ClassifiedBy: "rule", ClassifiedAtUtc: null,
        RemoteChangeKey: "ck-1", IsDeletedRemote: false);
}
