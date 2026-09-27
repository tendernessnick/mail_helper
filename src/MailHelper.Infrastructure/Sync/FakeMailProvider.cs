using MailHelper.Core;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;

namespace MailHelper.Infrastructure.Sync;

/// <summary>测试用 IMailProvider 假实现（驱动 SyncCoordinator 状态机；Services.Tests/调试宿主共用）。</summary>
public sealed class FakeMailProvider : IMailProvider
{
    private string? _completedLink;

    public ChannelKind Kind => ChannelKind.Graph;

    /// <summary>逐页输出（FetchDeltaAsync 从头枚举全部页）。</summary>
    public List<IReadOnlyList<RemoteMessage>> Pages { get; } = new();

    /// <summary>CompleteAsync 返回的断点（模拟 Graph 的 deltaLink）。</summary>
    public string? CompletedLink { get => _completedLink; set => _completedLink = value; }

    /// <summary>最近一次 FetchDeltaAsync 收到的断点（断点续传断言用）。</summary>
    public string? ReceivedDeltaLink { get; private set; }

    public int FetchCalls { get; private set; }

    /// <summary>产出全部页后抛出的异常（模拟同步中途失败，EX-TC-05 断点续传语义：部分数据已 yield）。</summary>
    public Exception? ThrowAfterPages { get; set; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async IAsyncEnumerable<RemoteMessage> FetchDeltaAsync(string? deltaLink, int pageSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedDeltaLink = deltaLink;
        FetchCalls++;

        foreach (var page in Pages)
        {
            foreach (var message in page)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return message;
            }
        }

        if (ThrowAfterPages is { } failure)
        {
            throw failure;
        }
    }

    public Task<string?> CompleteAsync() => Task.FromResult(CompletedLink);

    public Task<ConnectionTestResult> TestAsync() => Task.FromResult(ConnectionTestResult.Ok());
}
