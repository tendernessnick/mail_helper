using System.IO;
using FluentAssertions;
using MailHelper.Core.Abstractions;
using MailHelper.Infrastructure.SystemIntegration;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>IAppPaths Windows 实现契约（docs/10 §4 P-02；MS4 Mac 实现以等价用例镜像本组）。
/// 等价性锚点：默认数据目录与迁移前硬编码（Bootstrapper.cs / App.xaml.cs）逐字节一致。</summary>
public class WindowsAppPathsTests
{
    [Fact]
    public void Default_DataDir_MatchesLegacyHardcodedPath()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MailHelper");
        new WindowsAppPaths().DataDir.Should().Be(expected);
    }

    [Fact]
    public void OverrideDataDir_IsRespectedForAllDerivedPaths()
    {
        var paths = new WindowsAppPaths(@"D:\custom\data");
        paths.DataDir.Should().Be(@"D:\custom\data");
        Path.GetDirectoryName(paths.LogsDir).Should().Be(paths.DataDir);
        Path.GetFileName(paths.LogsDir).Should().Be("logs");
        Path.GetDirectoryName(paths.BodiesDir).Should().Be(paths.DataDir);
        Path.GetFileName(paths.BodiesDir).Should().Be("bodies");
        Path.GetDirectoryName(paths.DbPath).Should().Be(paths.DataDir);
        Path.GetFileName(paths.DbPath).Should().Be("mailhelper.db");
    }

    [Fact]
    public void GetTempFilePath_IsUnderSystemTemp()
    {
        IAppPaths paths = new WindowsAppPaths();
        var p = paths.GetTempFilePath("probe.txt");
        Path.GetFileName(p).Should().Be("probe.txt");
        Path.GetDirectoryName(p).Should().Be(Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
    }
}
