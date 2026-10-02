using FluentAssertions;
using MailHelper.Core;
using Xunit;

namespace MailHelper.Core.Tests;

/// <summary>S0 骨架冒烟：校验领域枚举与 04 章 §2.1 的签名一致性（测试先行基线，S1 起扩展 R-01~R-07）。</summary>
public class SanityTests
{
    [Fact]
    public void ImportanceValuesMatchSpecification()
    {
        // 04 章 §2.1：数值越大越重要；P3=0..P0=3（与 DDL importance INTEGER 0..3 对应）
        ((int)Importance.P3).Should().Be(0);
        ((int)Importance.P2).Should().Be(1);
        ((int)Importance.P1).Should().Be(2);
        ((int)Importance.P0).Should().Be(3);
    }

    [Fact]
    public void MailCategoryHasSevenValues()
    {
        CategoryIds.All.Should().BeEquivalentTo(new[]
        {
            CategoryIds.Course,
            CategoryIds.Career,
            CategoryIds.Admin,
            CategoryIds.Finance,
            CategoryIds.Announce,
            CategoryIds.Subscription,
            CategoryIds.Other,
        });
    }

    [Fact]
    public void ChannelAndChangeKindsMatchSpecification()
    {
        Enum.GetValues<ChannelKind>().Should().BeEquivalentTo(
            new[] { ChannelKind.Graph, ChannelKind.Imap, ChannelKind.OutlookDesktop, ChannelKind.OutlookMac }); // CHG-011 桌面通道 + CHG-014 Mac 通道（只追加不重排，存量库兼容）
        Enum.GetValues<ChangeKind>().Should().BeEquivalentTo(new[]
        {
            ChangeKind.Added, ChangeKind.Updated, ChangeKind.Removed,
        });
    }
}
