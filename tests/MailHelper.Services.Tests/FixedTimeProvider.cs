namespace MailHelper.Services.Tests;

/// <summary>固定时钟（勿扰时段可测）：.NET 8 TimeProvider 抽象的测试替身，零新增包。</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
