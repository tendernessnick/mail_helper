using FluentAssertions;
using MailHelper.Infrastructure.SystemIntegration;
using Microsoft.Win32;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>开机自启（FR-03/US-10，TC-020）：HKCU Run 键写/删同步（测试用独立子键，不污染真实注册表）。</summary>
public class AutostartServiceTests : IDisposable
{
    private const string TestRunKey = @"Software\MailHelper-Tests\Run";

    public AutostartServiceTests() => CleanKey();

    public void Dispose() => CleanKey();

    private static void CleanKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\MailHelper-Tests", writable: true);
        key?.DeleteSubKeyTree("Run", throwOnMissingSubKey: false);
    }

    [Fact]
    public void EnableDisable_SyncsRunKey()
    {
        var service = new AutostartService(TestRunKey, "MailHelper-Tests");

        service.IsEnabled.Should().BeFalse(); // 默认关（04 §7 app.autostart=false）

        service.Enable();
        service.IsEnabled.Should().BeTrue();
        using (var key = Registry.CurrentUser.OpenSubKey(TestRunKey))
        {
            key?.GetValueNames().Should().Contain("MailHelper-Tests"); // TC-020：Run 键出现
        }

        service.Disable();
        service.IsEnabled.Should().BeFalse();
        using (var key = Registry.CurrentUser.OpenSubKey(TestRunKey))
        {
            key?.GetValueNames().Should().NotContain("MailHelper-Tests"); // TC-020：Run 键移除
        }
    }

    [Fact]
    public void Enable_Twice_IsIdempotent()
    {
        var service = new AutostartService(TestRunKey, "MailHelper-Tests");
        service.Enable();
        service.Enable();
        service.IsEnabled.Should().BeTrue();
    }
}
