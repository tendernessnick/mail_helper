using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Notifications;
using MailHelper.Infrastructure.Sync.OutlookMac;
using Xunit;

namespace MailHelper.Mac.Tests;

/// <summary>通知发送器契约（MS6；docs/10 §5.2）：参数构造 + 静默失败；真实弹窗=检查点②。</summary>
public class OsascriptToastSenderTests
{
    private sealed class RecordingRunner : IAppleScriptRunner
    {
        public (string Script, IReadOnlyList<string> Args)? LastCall { get; private set; }
        public bool ThrowTimeout { get; set; }

        public Task<AppleScriptResult> RunAsync(
            string scriptName, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
        {
            if (ThrowTimeout)
            {
                throw new OsascriptTimeoutException(scriptName, timeout);
            }

            LastCall = (scriptName, args);
            return Task.FromResult(new AppleScriptResult(0, string.Empty, string.Empty));
        }
    }

    [Fact]
    public async Task SendAsync_PassesTitleAndBody_ToNotifyScript()
    {
        var runner = new RecordingRunner();
        var sender = new OsascriptToastSender(runner);

        await sender.SendAsync(new ToastNotification("学费缴纳最终提醒", "须于 30 Sep 前缴清", "mailhelper://message/m1"), CancellationToken.None);

        runner.LastCall.Should().NotBeNull();
        runner.LastCall!.Value.Script.Should().Be("notify");
        runner.LastCall.Value.Args[0].Should().Be("学费缴纳最终提醒");
        runner.LastCall.Value.Args[1].Should().Be("须于 30 Sep 前缴清");
    }

    [Fact]
    public async Task SendAsync_OnTimeout_FailsSilently()
    {
        var runner = new RecordingRunner { ThrowTimeout = true };
        var sender = new OsascriptToastSender(runner);

        var act = () => sender.SendAsync(new ToastNotification("t", "b", null), CancellationToken.None);

        await act.Should().NotThrowAsync("通知失败静默降级（不影响同步）");
    }
}
