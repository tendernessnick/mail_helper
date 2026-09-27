using System.Diagnostics;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailHelper.Core.Services;

/// <summary>同步编排（04 章 §2.2 签名）：定时触发、全量/增量流程、限流退避与状态机
/// Idle → Syncing → (Idle | Offline | Error | ReauthRequired)；批 100 入库并触发 BatchSynced 进度；
/// deltaLink 仅在整轮完成后持久化（EX-05：中断后按旧断点续传，upsert 幂等保证无重复）。</summary>
public sealed class SyncCoordinator
{
    private readonly IMailProvider _provider;
    private readonly IMessageStore _messageStore;
    private readonly IAccountStore _accountStore;
    private readonly string _accountId;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly int _batchSize;
    private readonly object _gate = new();
    private DateTime _lastOfflineLogUtc = DateTime.MinValue;

    public SyncCoordinator(
        IMailProvider provider,
        IMessageStore messageStore,
        IAccountStore accountStore,
        string accountId,
        ILogger<SyncCoordinator>? logger = null,
        int batchSize = 100)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _messageStore = messageStore ?? throw new ArgumentNullException(nameof(messageStore));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _logger = logger ?? NullLogger<SyncCoordinator>.Instance;
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
    }

    public SyncState State { get; private set; } = SyncState.Idle;

    public event EventHandler<SyncStateChangedEventArgs>? StateChanged;

    public event EventHandler<BatchSyncedEventArgs>? BatchSynced;

    /// <summary>整轮成功后触发一次（CHG-010：04 §8.1 newMails 载体；失败/取消轮不触发）。</summary>
    public event EventHandler<SyncRoundCompletedEventArgs>? SyncRoundCompleted;

    public async Task SyncNowAsync(bool fullIfNoLink = true, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (State == SyncState.Syncing)
            {
                return; // 与周期触发重叠时跳过本轮
            }

            State = SyncState.Syncing;
        }

        StateChanged?.Invoke(this, new SyncStateChangedEventArgs(SyncState.Syncing));

        var checkpoint = await _accountStore.GetCheckpointAsync(_accountId, ct);
        if (checkpoint?.DeltaLink is null && !fullIfNoLink)
        {
            RaiseStateChanged(SyncState.Idle);
            return;
        }

        var sw = Stopwatch.StartNew();
        var pageIndex = 0;
        var added = 0;
        var removed = 0;
        var wasInitialRound = checkpoint is null; // 首轮判定（D-50）：同步前无断点
        var roundMails = new List<MailMessage>(_batchSize);
        var buffer = new List<MailMessage>(_batchSize);

        async Task FlushAsync()
        {
            var inserted = await _messageStore.UpsertRangeAsync(_accountId, buffer, ct);
            var removedInBatch = buffer.Count(m => m.IsDeletedRemote);
            added += inserted;
            removed += removedInBatch;
            pageIndex++;
            roundMails.AddRange(buffer); // CHG-010：整轮 newMails 累积
            RaiseBatchSynced(new BatchSyncedEventArgs(pageIndex, inserted, buffer.Count - inserted, removedInBatch));
            buffer.Clear();
        }

        try
        {
            _logger.LogInformation(
                "sync.started incremental={Incremental} batch_size={BatchSize}",
                checkpoint?.DeltaLink is not null, _batchSize);

            await foreach (var remote in _provider.FetchDeltaAsync(checkpoint?.DeltaLink, _batchSize, ct))
            {
                buffer.Add(RemoteMessageMapper.ToStored(remote, _accountId));
                if (buffer.Count >= _batchSize)
                {
                    await FlushAsync();
                }
            }

            if (buffer.Count > 0)
            {
                await FlushAsync();
            }

            var newLink = await _provider.CompleteAsync();
            await _accountStore.SaveCheckpointAsync(
                new SyncCheckpoint(_accountId, newLink ?? checkpoint?.DeltaLink, checkpoint?.ImapUidWatermark,
                    DateTime.UtcNow, "ok"),
                ct); // 断点仅在整轮成功后推进（FR-04 AC2 / EX-05）

            _logger.LogInformation(
                "sync.completed pages={Pages} latency_ms={LatencyMs} added={Added} updated={Updated} removed={Removed}",
                pageIndex, sw.ElapsedMilliseconds, added, added, removed);
            SyncRoundCompleted?.Invoke(this,
                new SyncRoundCompletedEventArgs(roundMails, wasInitialRound, sw.ElapsedMilliseconds));
            RaiseStateChanged(SyncState.Idle);
        }
        catch (MailProviderException ex)
        {
            if (buffer.Count > 0)
            {
                try
                {
                    await FlushAsync(); // EX-05：已从远端接收的部分尽力入库（下轮按旧断点续传，幂等无重复）
                }
                catch (Exception flushError) when (flushError is not OperationCanceledException
                    || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(flushError, "sync.partial_flush_failed");
                }
            }

            await SaveFailureStatusAsync(checkpoint, ex, ct);
            switch (ex.ErrorCode)
            {
                case "SYNC-001":
                    LogThrottledOffline(ex);
                    RaiseStateChanged(SyncState.Offline, ex.ErrorCode, ex.Message);
                    break;
                case "AUTH-003": // EX-02：改密/吊销 → 需重新登录
                    _logger.LogWarning("sync.reauth_required err_code=AUTH-003");
                    RaiseStateChanged(SyncState.ReauthRequired, ex.ErrorCode, ex.Message);
                    break;
                default:
                    _logger.LogWarning("sync.failed err_code={ErrorCode} message={Message}", ex.ErrorCode, ex.Message);
                    RaiseStateChanged(SyncState.Error, ex.ErrorCode, ex.Message);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RaiseStateChanged(SyncState.Idle); // FR-05 取消：断点不推进，下轮续传
        }
    }

    public async Task RunPeriodicAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break; // 取消 = 正常退出（托盘退出/服务停止）
            }

            try
            {
                await SyncNowAsync(ct: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "sync.unexpected_failure"); // 长稳（NFR-05）：单轮异常不终止主循环
            }
        }
    }

    private async Task SaveFailureStatusAsync(SyncCheckpoint? previous, MailProviderException ex, CancellationToken ct) =>
        await _accountStore.SaveCheckpointAsync(
            new SyncCheckpoint(_accountId, previous?.DeltaLink, previous?.ImapUidWatermark, DateTime.UtcNow, ex.ErrorCode),
            ct); // 失败仅记录 last_sync_status；断点不推进

    private void LogThrottledOffline(MailProviderException ex)
    {
        if (DateTime.UtcNow - _lastOfflineLogUtc >= TimeSpan.FromMinutes(1)) // SYNC-001 频率限制 1/min（D-35）
        {
            _logger.LogInformation("sync.offline err_code={ErrorCode}", ex.ErrorCode);
            _lastOfflineLogUtc = DateTime.UtcNow;
        }
    }

    private void RaiseStateChanged(SyncState state, string? errorCode = null, string? message = null)
    {
        lock (_gate)
        {
            State = state;
        }

        StateChanged?.Invoke(this, new SyncStateChangedEventArgs(state, errorCode, message));
    }

    private void RaiseBatchSynced(BatchSyncedEventArgs args) => BatchSynced?.Invoke(this, args);
}
