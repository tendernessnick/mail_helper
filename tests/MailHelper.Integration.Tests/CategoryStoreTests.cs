using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Rules;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>S14-C 自定义类别仓储（CHG-012）：内置七类静态不可改删；自定义 CRUD；
/// 删除级联=该类邮件归「其他」+ 指向它的规则删除（事务）。</summary>
public class CategoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-catstore-" + Guid.NewGuid().ToString("N"));
    private readonly CategoryStore _store;
    private readonly MailRepository _mails;
    private readonly RuleRepository _rules;

    public CategoryStoreTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(dbPath);
        _store = new CategoryStore(dbPath);
        _mails = new MailRepository(dbPath);
        _rules = new RuleRepository(dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private CategoryDefinition Custom(string id = "custom-intern", string label = "实习申请") =>
        new(id, label, "🎯", "#C2417D", Sort: 8, IsBuiltin: false);

    private async Task SeedAsync()
    {
        await _store.UpsertAsync(Custom(), CancellationToken.None);
        await new AccountRepository(Path.Combine(_dir, "mh.db")).UpsertAccountAsync(new Account(
            "acc-1", "s@my.cityu.edu.hk", "测试", "t1", ChannelKind.OutlookDesktop, null,
            AccountStatus.Active, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        await _mails.UpsertRangeAsync("acc-1",
        [
            new MailMessage("m1", "acc-1", "<m1@im>", "s", "f", "f@x.hk", "p", null,
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), false, false,
                "custom-intern", Importance.P2, 0.9, "rule", null, null, false),
            new MailMessage("m2", "acc-1", "<m2@im>", "s", "f", "f@x.hk", "p", null,
                new DateTime(2026, 10, 1, 0, 1, 0, DateTimeKind.Utc), false, false,
                CategoryIds.Course, Importance.P2, 0.9, "rule", null, null, false),
        ], CancellationToken.None);
        await _rules.UpsertAsync(new ClassifyRule(
            Guid.NewGuid(), "r-intern", RuleKind.SubjectKeyword, "intern",
            "custom-intern", null, 5, 100, true, RuleSource.User), CancellationToken.None);
        await _rules.UpsertAsync(new ClassifyRule(
            Guid.NewGuid(), "r-course", RuleKind.SubjectKeyword, "quiz",
            CategoryIds.Course, null, 5, 100, true, RuleSource.User), CancellationToken.None);
    }

    [Fact]
    public async Task UpsertThenGetAll_ReturnsCustomOnly_BuiltinIsStatic()
    {
        await _store.UpsertAsync(Custom(), CancellationToken.None);

        var all = await _store.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle(c => c.Id == "custom-intern");
        all.Should().NotContain(c => c.Id == CategoryIds.Course); // 内置不落库
        CategoryIds.All.Should().HaveCount(7); // 内置恒七类
    }

    [Fact]
    public async Task Upsert_BuiltinId_IsRejected()
    {
        var act = () => _store.UpsertAsync(
            Custom(id: CategoryIds.Course, label: "冒充课程"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Delete_BuiltinId_IsRejected()
    {
        var act = () => _store.DeleteAsync(CategoryIds.Other, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Delete_CascadesMessagesToOther_AndDropsPointingRules()
    {
        await SeedAsync();

        var migrated = await _store.DeleteAsync("custom-intern", CancellationToken.None);

        migrated.Should().Be(1); // m1 归其他，m2（course）不动
        (await _mails.GetByIdAsync("m1", CancellationToken.None))!.Category.Should().Be(CategoryIds.Other);
        (await _mails.GetByIdAsync("m2", CancellationToken.None))!.Category.Should().Be(CategoryIds.Course);
        (await _rules.GetAllAsync(CancellationToken.None)).Should().NotContain(r => r.Category == "custom-intern");
        (await _rules.GetAllAsync(CancellationToken.None)).Should().Contain(r => r.Category == CategoryIds.Course);
        (await _store.GetAllAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
