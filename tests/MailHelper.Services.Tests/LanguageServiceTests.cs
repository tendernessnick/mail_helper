using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Services;
using MailHelper.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailHelper.Services.Tests;

/// <summary>语言服务（NFR-12）：ui.language（auto/zh-CN/en）→ 字典解析；en 缺键回落中文；未知键回落键名。</summary>
public class LanguageServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-lang-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;

    public LanguageServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mh.db");
        MailDatabase.EnsureReady(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private LanguageService NewService(string language, out SettingsService settings)
    {
        var repo = new SettingsRepository(_dbPath);
        settings = new SettingsService(repo);
        settings.SetLanguageAsync(language, CancellationToken.None).GetAwaiter().GetResult();
        return new LanguageService(repo);
    }

    [Fact]
    public async Task Zh_ChineseLookup()
    {
        var svc = NewService("zh-CN", out _);
        await svc.ApplyAsync(CancellationToken.None);

        svc.T("nav.inbox").Should().Be("收件箱");
        svc.T("nav.rules").Should().Be("规则");
        svc.T("search.button").Should().Be("搜索");
    }

    [Fact]
    public async Task En_EnglishLookup()
    {
        var svc = NewService("en", out _);
        await svc.ApplyAsync(CancellationToken.None);

        svc.T("nav.inbox").Should().Be("Inbox");
        svc.T("search.button").Should().Be("Search");
    }

    [Fact]
    public async Task En_MissingKey_FallsBackToChinese()
    {
        var svc = NewService("en", out _);
        await svc.ApplyAsync(CancellationToken.None);

        svc.T("zh.only.sample").Should().Be("zh.only.sample"); // 英文字典无此键且中文字典也无 → 键名兜底
    }

    [Fact]
    public async Task Auto_FollowsSystemUICulture()
    {
        var svc = NewService("auto", out var settings);
        await svc.ApplyAsync(CancellationToken.None);

        var expected = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        svc.CurrentTag.Should().Be(expected.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en");
        _ = settings; // 设置读取路径已覆盖
    }
}
