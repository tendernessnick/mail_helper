namespace MailHelper.Infrastructure.Sync;

/// <summary>Graph HTTP 策略参数（04 章 §4.2 退避矩阵；测试注入短延迟）。
/// 429 优先按 Retry-After 头等待（无头/超上限时退化为 BackoffDelays）；
/// 5xx/网络异常按 BackoffDelays（默认 2s/8s/30s）指数退避；均最多重试 3 次。</summary>
public sealed record GraphHttpOptions
{
    public static readonly TimeSpan[] DefaultBackoffs =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(30),
    };

    public GraphHttpOptions()
    {
        BackoffDelays = DefaultBackoffs;
    }

    public IReadOnlyList<TimeSpan> BackoffDelays { get; init; }

    public int MaxRetries => BackoffDelays.Count;

    /// <summary>单封正文拉取大小上限（EX-06：>5MB 截断，预览已由 bodyPreview 承担，此处限制读入内存量）。</summary>
    public long MaxBodyBytes { get; init; } = 5 * 1024 * 1024;
}
