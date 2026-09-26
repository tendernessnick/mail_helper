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
        Enum.GetValues<MailCategory>().Should().BeEquivalentTo(new[]
        {
            MailCategory.Course,
            MailCategory.Career,
            MailCategory.Admin,
            MailCategory.Finance,
            MailCategory.Announce,
            MailCategory.Subscription,
            MailCategory.Other,
        });
    }

    [Fact]
    public void ChannelAndChangeKindsMatchSpecification()
    {
        Enum.GetValues<ChannelKind>().Should().BeEquivalentTo(new[] { ChannelKind.Graph, ChannelKind.Imap });
        Enum.GetValues<ChangeKind>().Should().BeEquivalentTo(new[]
        {
            ChangeKind.Added, ChangeKind.Updated, ChangeKind.Removed,
        });
    }
}
