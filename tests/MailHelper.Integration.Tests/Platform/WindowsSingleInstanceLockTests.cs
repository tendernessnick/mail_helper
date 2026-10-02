using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.SystemIntegration;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>ISingleInstanceLock Windows 实现契约（docs/10 §4 P-03；MS4 Mac UDS 实现以等价用例镜像本组）。
/// 唤起链路（管道 SHOW→窗口重现）由 UiSmokeTests 端到端覆盖，此处锁语义单测。
/// 每个用例注入唯一 mutex/pipe 名称（构造参数可注入，同 AutostartService runKeyPath 先例）：
/// 既不与 UiSmoke（拉起真实 App 抢全局互斥）互为死锁对手，也不受用户本机运行中的安装版干扰。</summary>
public class WindowsSingleInstanceLockTests
{
    private static (string MutexName, string PipeName) UniqueNames() =>
        ($"Local\\mh-test-{Guid.NewGuid():N}", $"mh-test-pipe-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstAcquire_Succeeds_SecondFails_DisposeReleases()
    {
        var (mutexName, pipeName) = UniqueNames();
        var first = new WindowsSingleInstanceLock(mutexName, pipeName);
        using (first)
        {
            first.TryAcquireFirst().Should().BeTrue();

            // 第二实例语义=独立进程：Windows 命名互斥体同线程可重入（WaitOne 递归计数假成功），
            // 必须以另一线程模拟真实跨进程竞争
            using var second = new WindowsSingleInstanceLock(mutexName, pipeName);
            (await Task.Run(() => second.TryAcquireFirst())).Should().BeFalse("互斥被首实例持有（跨线程视角）");
        }

        // 释放后同名可再次成为首实例（等价原静态类 StopListening 语义）
        using var third = new WindowsSingleInstanceLock(mutexName, pipeName);
        third.TryAcquireFirst().Should().BeTrue();
    }

    [Fact]
    public void NotifyRunningInstance_WithoutListener_DoesNotThrow()
    {
        // 首实例管道未就绪：静默无操作（原实现 catch IOException/TimeoutException 语义）
        var (mutexName, pipeName) = UniqueNames();
        using var lone = new WindowsSingleInstanceLock(mutexName, pipeName);
        lone.TryAcquireFirst().Should().BeTrue();
        var act = () => lone.NotifyRunningInstance();
        act.Should().NotThrow();
    }

    [Fact]
    public void StartListening_InvokesActivate_OnShowSignal()
    {
        var (mutexName, pipeName) = UniqueNames();
        var first = new WindowsSingleInstanceLock(mutexName, pipeName);
        using (first)
        {
            first.TryAcquireFirst().Should().BeTrue();

            var activated = new ManualResetEventSlim(false);
            first.StartListening(() => activated.Set());

            using var second = new WindowsSingleInstanceLock(mutexName, pipeName);
            second.NotifyRunningInstance();

            activated.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("二次启动的 SHOW 应唤起首实例回调");
        }
    }
}
