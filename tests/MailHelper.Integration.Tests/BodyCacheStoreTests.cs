using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MailHelper.Infrastructure.Storage;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>正文磁盘缓存 + LRU 清理（03 章 §6；cache.body_limit_mb 默认 2048）。</summary>
public class BodyCacheStoreTests : TempDirTestBase
{
    private string Root => Path.Combine(Dir, "bodies");

    private static string Pad(char c) => new(c, 1000); // 每个文件约 1000 字节

    private string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task Save_Read_Roundtrip_UsesSha1FileName()
    {
        var cache = new BodyCacheStore(Root);
        const string html = "<html><body>学费提醒正文</body></html>";

        var relative = await cache.SaveAsync("acc-1", html, CancellationToken.None);

        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
        relative.Should().Be($"acc-1/{sha1}.html"); // 03 §6 路径规范 {accountId}/{sha1(html)}.html
        File.Exists(PathOf(relative)).Should().BeTrue();

        (await cache.ReadAsync(relative, CancellationToken.None)).Should().Be(html);
        (await cache.ReadAsync("acc-1/0000000000000000000000000000000000000000.html", CancellationToken.None))
            .Should().BeNull(); // 缺失返回 null
    }

    [Fact]
    public async Task LruCleanup_RemovesOldestBeyondLimit()
    {
        var cache = new BodyCacheStore(Root, limitBytes: 1500);

        var r1 = await cache.SaveAsync("a", Pad('1') + "-one", CancellationToken.None);
        var r2 = await cache.SaveAsync("a", Pad('2') + "-two", CancellationToken.None);
        var r3 = await cache.SaveAsync("a", Pad('3') + "-three", CancellationToken.None);

        (await cache.ReadAsync(r3, CancellationToken.None)).Should().NotBeNull(); // 最新保留
        (await cache.ReadAsync(r1, CancellationToken.None)).Should().BeNull();    // 超限淘汰最旧
        (await cache.ReadAsync(r2, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Read_TouchesRecency_SurvivesCleanup()
    {
        var cache = new BodyCacheStore(Root, limitBytes: 2200);

        var r1 = await cache.SaveAsync("a", Pad('1') + "-one", CancellationToken.None);
        var r2 = await cache.SaveAsync("a", Pad('2') + "-two", CancellationToken.None);

        // 人为把 r1 调旧、r2 保持新，然后读 r1（touch 应使其重新变为最新）
        File.SetLastWriteTimeUtc(PathOf(r1), DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(PathOf(r2), DateTime.UtcNow.AddHours(-1));
        (await cache.ReadAsync(r1, CancellationToken.None)).Should().NotBeNull();

        var r3 = await cache.SaveAsync("a", Pad('3') + "-three", CancellationToken.None); // 触发清理

        (await cache.ReadAsync(r1, CancellationToken.None)).Should().NotBeNull(); // 刚被读过 → 存活
        (await cache.ReadAsync(r2, CancellationToken.None)).Should().BeNull();    // 最旧被淘汰
    }
}
