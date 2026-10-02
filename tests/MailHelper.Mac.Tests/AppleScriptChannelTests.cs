using FluentAssertions;
using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Sync.OutlookMac;
using Xunit;

namespace MailHelper.Mac.Tests;

/// <summary>OutlookMacMailProvider 全场景测试（docs/10 §12.3；假 osascript 驱动，全平台可跑；
/// 真实字典字段核验=检查点②，协议由本组测试锁定）。</summary>
public class AppleScriptChannelTests
{
    private static FakeAppleScriptRunner NewRunner() => new();

    private static OutlookMacMailProvider NewProvider(FakeAppleScriptRunner runner) =>
        new(runner, cacheAccountKey: "acc-1");

    // —— TestAsync 四类错误映射（docs/10 §6.5）——

    [Fact]
    public async Task TestAsync_Success_ReturnsOk_WithAccountAddress()
    {
        var runner = NewRunner();
        runner.Enqueue("1\u001Fstudent@my.cityu.edu.hk");
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task TestAsync_ZeroAccounts_MapsToMac002()
    {
        var runner = NewRunner();
        runner.Enqueue("0\u001F");
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(MacConnectionErrorCodes.NoAccount);
    }

    [Fact]
    public async Task TestAsync_TccDenied_MapsToMac003()
    {
        var runner = NewRunner();
        runner.EnqueueError(exitCode: 1, stderr: "execution error: Not authorized to send Apple events (-1743)");
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(MacConnectionErrorCodes.AutomationDenied);
    }

    [Fact]
    public async Task TestAsync_OutlookNotRunning_MapsToMac001()
    {
        var runner = NewRunner();
        runner.EnqueueError(exitCode: 1, stderr: "execution error: Microsoft Outlook got an error: Application isn't running. (-600)");
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.ErrorCode.Should().Be(MacConnectionErrorCodes.OutlookNotRunning);
    }

    [Fact]
    public async Task TestAsync_NewOutlookNotSupported_MapsToMac004()
    {
        var runner = NewRunner();
        runner.EnqueueError(exitCode: 1, stderr: "execution error: Microsoft Outlook got an error: Doesn't understand the message. (-1708)");
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.ErrorCode.Should().Be(MacConnectionErrorCodes.NewOutlookUnsupported);
    }

    [Fact]
    public async Task TestAsync_Timeout_MapsToMac005()
    {
        var runner = NewRunner();
        runner.EnqueueTimeout();
        var provider = NewProvider(runner);

        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(MacConnectionErrorCodes.QueryFailed);
    }

    // —— FetchDeltaAsync：解析→RemoteMessage 映射 + 参数构造 ——

    [Fact]
    public async Task FetchDelta_ParsesDelimitedRecords_IntoRemoteMessages()
    {
        var runner = NewRunner();
        // id,subject,fromName,fromAddress,epoch,hasAttach,isRead,preview
        runner.Enqueue(string.Join('\u001E',
            "m1\u001F学费缴纳最终提醒\u001F财务处\u001Ffinance@hku.hk\u001F1780000000\u001F1\u001F0\u001FYour tuition must be settled",
            "m2\u001F面试邀约\u001FHR Team\u001Fhr@bank.example.com\u001F1780003600\u001F0\u001F1\u001FWe invite you"));
        var provider = NewProvider(runner);

        var messages = new List<RemoteMessage>();
        await foreach (var m in provider.FetchDeltaAsync(null, pageSize: 100, CancellationToken.None))
        {
            messages.Add(m);
        }

        messages.Should().HaveCount(2);
        var first = messages[0];
        first.ProviderMessageId.Should().Be("m1");
        first.Subject.Should().Be("学费缴纳最终提醒");
        first.FromName.Should().Be("财务处");
        first.FromAddress.Should().Be("finance@hku.hk");
        first.ReceivedAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1780000000).UtcDateTime);
        first.HasAttachments.Should().BeTrue();
        first.IsRead.Should().BeFalse();
        first.Kind.Should().Be(ChangeKind.Added);
        first.BodyPreview.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task FetchDelta_PassesRelativeLookbackArgs_ToScript()
    {
        var runner = NewRunner();
        runner.Enqueue("");
        var provider = NewProvider(runner);

        await foreach (var _ in provider.FetchDeltaAsync(WatermarkCodec.Encode(1780000000), 50, CancellationToken.None))
        {
        }

        var call = runner.Calls.Should().ContainSingle().Subject;
        call.ScriptName.Should().Be("list_recent");
        var lookbackHours = double.Parse(call.Args[0]);
        // 水位 1780000000 + 24h 重叠窗口：lookback 必为正数且 ≤ 7*24 上限
        lookbackHours.Should().BeGreaterThan(0);
        lookbackHours.Should().BeLessThanOrEqualTo(24 * 7);
        call.Args[1].Should().Be("50");
    }

    [Fact]
    public async Task FetchDelta_EmptyOutput_YieldsNothing()
    {
        var runner = NewRunner();
        runner.Enqueue("");
        var provider = NewProvider(runner);

        var messages = new List<RemoteMessage>();
        await foreach (var m in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
        {
            messages.Add(m);
        }

        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchDelta_FormatDrift_MalformedRecords_AreSkippedNotThrown()
    {
        var runner = NewRunner();
        runner.Enqueue(string.Join('\u001E',
            "bad-record-without-separators",
            "m1\u001F主题\u001F发件人\u001Fa@b.c\u001F1780000000\u001F0\u001F0\u001Fpreview"));
        var provider = NewProvider(runner);

        var messages = new List<RemoteMessage>();
        await foreach (var m in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
        {
            messages.Add(m);
        }

        messages.Should().ContainSingle("畸形记录应跳过，不抛原始异常（docs/10 §6.1 防御解析）");
        messages[0].ProviderMessageId.Should().Be("m1");
    }

    [Fact]
    public async Task FetchDelta_SeparatorPollution_IsDefended()
    {
        // 防御语义：分隔符污染在尾部字段→Split 上限吸收，记录可用；污染在中间字段→时间字段错位→整条跳过。
        // （值内 0x1E/0x1F 的剥离由脚本侧 strip() 负责，docs/10 §6.2；解析器只兜底不抛异常）
        var runner = NewRunner();
        runner.Enqueue(string.Join('\u001E',
            "m1\u001F主题\u001F发件人\u001Fa@b.c\u001F1780000000\u001F0\u001F0\u001Fpreview\u001Fwith\u001Fseparators",
            "m2\u001F含\u001F分隔符的主题\u001F发件人\u001Fa@b.c\u001F1780000001\u001F0\u001F0\u001Fp"));
        var provider = NewProvider(runner);

        var messages = new List<RemoteMessage>();
        await foreach (var m in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
        {
            messages.Add(m);
        }

        messages.Should().ContainSingle();
        messages[0].ProviderMessageId.Should().Be("m1");
        messages[0].Subject.Should().Be("主题").And.NotContain("\u001F");
    }

    // —— 水位推进（FR-04 AC2 断点语义）——

    [Fact]
    public async Task CompleteAsync_AdvancesWatermark_ToMaxSeenEpoch()
    {
        var runner = NewRunner();
        runner.Enqueue(string.Join('\u001E',
            "m1\u001Fa\u001Ff\u001Fa@b.c\u001F1780000000\u001F0\u001F0\u001Fp",
            "m2\u001Fb\u001Ff\u001Fa@b.c\u001F1780003600\u001F0\u001F0\u001Fp"));
        var provider = NewProvider(runner);

        await foreach (var _ in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
        {
        }

        var link = await provider.CompleteAsync();
        WatermarkCodec.TryParse(link, out var epoch).Should().BeTrue();
        epoch.Should().Be(1780003600, "水位应前移至本轮最大 receivedEpoch");
    }

    [Fact]
    public async Task CompleteAsync_NoMessages_ReturnsNull()
    {
        var runner = NewRunner();
        runner.Enqueue("");
        var provider = NewProvider(runner);

        await foreach (var _ in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
        {
        }

        (await provider.CompleteAsync()).Should().BeNull("空轮不产生新断点（水位保持由上层持久化）");
    }

    [Fact]
    public async Task FetchDelta_ScriptFailure_ThrowsProviderException()
    {
        var runner = NewRunner();
        runner.EnqueueError(exitCode: 1, stderr: "execution error: Not authorized to send Apple events (-1743)");
        var provider = NewProvider(runner);

        var act = async () =>
        {
            await foreach (var _ in provider.FetchDeltaAsync(null, 100, CancellationToken.None))
            {
            }
        };
        await act.Should().ThrowAsync<MacChannelException>()
            .Where(e => e.ErrorCode == MacConnectionErrorCodes.AutomationDenied);
    }

    // —— GetAccountAddressAsync ——

    [Fact]
    public async Task GetAccountAddress_ReturnsAddress_FromProbe()
    {
        var runner = NewRunner();
        runner.Enqueue("1\u001Fstudent@my.cityu.edu.hk");
        var provider = NewProvider(runner);

        var address = await provider.GetAccountAddressAsync(CancellationToken.None);

        address.Should().Be("student@my.cityu.edu.hk");
    }

    [Fact]
    public async Task GetAccountAddress_OnFailure_ReturnsNull()
    {
        var runner = NewRunner();
        runner.EnqueueError(exitCode: 1, stderr: "execution error: (-1743)");
        var provider = NewProvider(runner);

        var address = await provider.GetAccountAddressAsync(CancellationToken.None);

        address.Should().BeNull();
    }
}
