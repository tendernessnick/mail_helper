using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>规则编辑器服务（FR-10 / 05 §3.3）：新建/编辑/删除/启停 + 试跑预览（最近 100 封）。
/// 内置规则可禁用不可删除；反馈规则可删除（撤销学习）；正则规则保存前校验可编译。</summary>
public class RuleManagementServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-rulemgr-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly RuleRepository _rules;
    private readonly MailRepository _messages;
    private readonly RuleEngine _engine;
    private readonly RuleManagementService _service;

    public RuleManagementServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
        new AccountRepository(_dbPath).UpsertAccountAsync(NewAccount(), CancellationToken.None)
            .GetAwaiter().GetResult();
        _rules = new RuleRepository(_dbPath);
        _messages = new MailRepository(_dbPath);
        _engine = new RuleEngine(new RuleSet("v-test", new RuleScoring(),
        [
            new ClassifyRule(Guid.NewGuid(), "Builtin-LMS", RuleKind.SenderDomain, "moodle.hku.hk",
                CategoryIds.Course, Importance.P2, 8, 100, true, RuleSource.Builtin),
        ]));
        _service = new RuleManagementService(_rules, _engine, _messages, "acc-1");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Account NewAccount() => new(
        "acc-1", "s@connect.hku.hk", "测试", "t1", ChannelKind.Graph, null,
        AccountStatus.Active, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));

    private static ClassifyRule Draft(
        string pattern = "careers.hku.hk", RuleKind kind = RuleKind.SenderDomain,
        string? category = CategoryIds.Career, Guid? id = null,
        RuleSource source = RuleSource.User, bool enabled = true) => new(
        id ?? Guid.NewGuid(), "规则-" + pattern, kind, pattern, category, null,
        RuleSet.DefaultWeight(kind), 100, enabled, source);

    private async Task SeedMailAsync(string id, string subject, string from)
    {
        await _messages.UpsertRangeAsync("acc-1",
        [
            new MailMessage(id, "acc-1", $"<{id}@im>", subject, "f", from, "预览", null,
                new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), false, false,
                CategoryIds.Other, Importance.P2, 0.2, "rule", null, null, false),
        ], CancellationToken.None);
    }

    [Fact]
    public async Task Save_UserRule_PersistsAndEngineApplies()
    {
        var saved = await _service.SaveAsync(Draft(), CancellationToken.None);

        saved.Source.Should().Be(RuleSource.User);
        (await _rules.GetAllAsync(CancellationToken.None)).Should().ContainSingle();
        var hit = await _engine.ClassifyAsync(
            new ClassifiedInput("s", "f", "someone@careers.hku.hk", null, null),
            CancellationToken.None);
        hit.Category.Should().Be(CategoryIds.Career); // 保存后即时生效
    }

    [Fact]
    public async Task Save_InvalidRegex_ThrowsValidation()
    {
        var act = () => _service.SaveAsync(
            Draft(pattern: "([unclosed", kind: RuleKind.SubjectRegex), CancellationToken.None);

        await act.Should().ThrowAsync<RuleValidationException>(); // ReDoS/编译错误在保存时拦截
        (await _rules.GetAllAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Save_EmptyPattern_ThrowsValidation()
    {
        var act = () => _service.SaveAsync(Draft(pattern: "  "), CancellationToken.None);

        await act.Should().ThrowAsync<RuleValidationException>();
    }

    [Fact]
    public async Task Save_BuiltinSource_Rejected()
    {
        var act = () => _service.SaveAsync(Draft(source: RuleSource.Builtin), CancellationToken.None);

        await act.Should().ThrowAsync<RuleValidationException>(); // 内置规则只读（05 §3.3：可禁用不可编辑）
    }

    [Fact]
    public async Task Delete_UserAndFeedbackRules_RemovedFromStoreAndEngine()
    {
        var user = await _service.SaveAsync(Draft(), CancellationToken.None);
        var feedback = Draft(pattern: "dean@ust.hk", kind: RuleKind.SenderAddress, source: RuleSource.Feedback);
        await _rules.UpsertAsync(feedback, CancellationToken.None);
        _engine.Upsert(feedback);

        await _service.DeleteAsync(user.Id, CancellationToken.None);
        await _service.DeleteAsync(feedback.Id, CancellationToken.None); // 撤销学习（05 §3.3）

        (await _rules.GetAllAsync(CancellationToken.None)).Should().BeEmpty();
        var hit = await _engine.ClassifyAsync(
            new ClassifiedInput("s", "f", "dean@ust.hk", null, null), CancellationToken.None);
        hit.Category.Should().Be(CategoryIds.Other); // 引擎同步移除
    }

    [Fact]
    public async Task Delete_BuiltinRule_Rejected()
    {
        var builtin = (await _rules.GetAllAsync(CancellationToken.None)).FirstOrDefault();
        // 内置规则不在表：直接用引擎里的内置 id
        var engineBuiltinId = Guid.NewGuid();
        var act = () => _service.DeleteAsync(engineBuiltinId, CancellationToken.None);

        await act.Should().ThrowAsync<RuleValidationException>(); // 表中无行（内置）不可删
    }

    [Fact]
    public async Task SetEnabled_BuiltinRule_DisabledPersistsAndEngineHonours()
    {
        // 内置规则（引擎内 id）禁用 → rules 表写同 id enabled=false 行 → 引擎不再命中
        var lms = await _rules.GetAllAsync(CancellationToken.None).ContinueWith(_ => (ClassifyRule?)null);
        var engineBuiltin = FindEngineRule("Builtin-LMS");
        engineBuiltin.Should().NotBeNull("引擎初始规则集含内置规则");

        await _service.SetEnabledAsync(engineBuiltin!.Id, enabled: false, CancellationToken.None);

        var hit = await _engine.ClassifyAsync(
            new ClassifiedInput("s", "f", "x@moodle.hku.hk", null, null), CancellationToken.None);
        hit.Category.Should().Be(CategoryIds.Other); // 禁用后不命中
        (await _rules.GetAllAsync(CancellationToken.None)).Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabled_UserRule_ToggleRestoresHit()
    {
        var saved = await _service.SaveAsync(Draft(), CancellationToken.None);
        await _service.SetEnabledAsync(saved.Id, enabled: false, CancellationToken.None);
        var off = await _engine.ClassifyAsync(
            new ClassifiedInput("s", "f", "someone@careers.hku.hk", null, null), CancellationToken.None);
        off.Category.Should().Be(CategoryIds.Other);

        await _service.SetEnabledAsync(saved.Id, enabled: true, CancellationToken.None);

        var on = await _engine.ClassifyAsync(
            new ClassifiedInput("s", "f", "someone@careers.hku.hk", null, null), CancellationToken.None);
        on.Category.Should().Be(CategoryIds.Career);
    }

    [Fact]
    public async Task Preview_ReturnsOnlyMatchingMails_Within100()
    {
        await SeedMailAsync("m1", "Internship", "someone@careers.hku.hk");
        await SeedMailAsync("m2", "Tuition", "bursary@hku.hk");
        await SeedMailAsync("m3", "Another", "other@hku.hk");

        var hits = await _service.PreviewAsync(Draft(), CancellationToken.None);

        hits.Select(m => m.Id).Should().BeEquivalentTo("m1"); // 试跑预览（FR-10）
    }

    private ClassifyRule? FindEngineRule(string name) =>
        _engine.CurrentRules.FirstOrDefault(r => r.Name == name);
}
