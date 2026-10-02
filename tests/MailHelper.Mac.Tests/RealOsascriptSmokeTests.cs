using FluentAssertions;
using MailHelper.Infrastructure.Sync.OutlookMac;
using Xunit;

namespace MailHelper.Mac.Tests;

/// <summary>真实 osascript 冒烟（CI macos job 真机执行；docs/10 §10「无 Outlook 环境验证错误路径」）。
/// 非 macOS 运行时静默跳过（守卫返回，零新依赖）；断言宽松=错误路径结构化可达即可。</summary>
public class RealOsascriptSmokeTests
{
    [Fact]
    public async Task Probe_WithoutOutlook_ReturnsStructuredFailure()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return; // 守卫跳过（Windows 本机无 osascript）；CI macos job 真机执行本用例
        }

        var provider = new OutlookMacMailProvider(new OsascriptScriptRunner(), cacheAccountKey: "acc-1");
        var result = await provider.TestAsync();

        result.IsSuccess.Should().BeFalse("CI mac 机器无 Outlook，应得结构化失败而非异常穿透");
        result.ErrorCode.Should().StartWith("MAC-");
    }
}
