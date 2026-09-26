using FluentAssertions;
using MailHelper.Core.Domain;
using MailHelper.Core.Rules;
using Xunit;

namespace MailHelper.Core.Tests;

/// <summary>领域契约（04 章 §2.1 签名一致性）。</summary>
public class DomainContractTests
{
    [Fact]
    public void RemoteMessage_Roundtrip_AllFields()
    {
        var received = new DateTime(2026, 9, 26, 1, 2, 3, DateTimeKind.Utc);
        var message = new RemoteMessage(
            ProviderMessageId: "graph-id-1",
            InternetMessageId: "<abc@msg.hku.hk>",
            Subject: "学费提醒",
            FromName: "财务处",
            FromAddress: "finance@hku.hk",
            BodyPreview: "请于 9 月 30 日前缴清学费…",
            HtmlPath: "bodies/a1/abc.html",
            ReceivedAtUtc: received,
            HasAttachments: true,
            IsRead: false,
            Kind: ChangeKind.Updated);

        message.ProviderMessageId.Should().Be("graph-id-1");
        message.InternetMessageId.Should().Be("<abc@msg.hku.hk>");
        message.Subject.Should().Be("学费提醒");
        message.FromAddress.Should().Be("finance@hku.hk");
        message.BodyPreview.Should().StartWith("请于");
        message.HtmlPath.Should().Be("bodies/a1/abc.html");
        message.ReceivedAtUtc.Should().Be(received);
        message.HasAttachments.Should().BeTrue();
        message.IsRead.Should().BeFalse();
        message.Kind.Should().Be(ChangeKind.Updated);
    }

    [Fact]
    public void ClassifiedInput_AllowsNullBodyAndReceivedAt()
    {
        var input = new ClassifiedInput("subject", "name", "a@b.c", BodyText: null, ReceivedAtUtc: null);

        input.Subject.Should().Be("subject");
        input.BodyText.Should().BeNull();
        input.ReceivedAtUtc.Should().BeNull();
    }

    [Fact]
    public void ConnectionTestResult_Factories()
    {
        var ok = ConnectionTestResult.Ok();
        ok.IsSuccess.Should().BeTrue();
        ok.ErrorCode.Should().BeNull();

        var fail = ConnectionTestResult.Fail("AUTH-002", "需要管理员批准");
        fail.IsSuccess.Should().BeFalse();
        fail.ErrorCode.Should().Be("AUTH-002");
        fail.Message.Should().NotBeNull();
    }

    [Fact]
    public void RuleSet_Empty_AndDefaultWeight_Table()
    {
        RuleSet.Empty.Rules.Should().BeEmpty();
        RuleSet.Empty.Version.Should().BeNull();

        RuleSet.DefaultWeight(RuleKind.SenderAddress).Should().Be(10);
        RuleSet.DefaultWeight(RuleKind.SenderDomain).Should().Be(8);
        RuleSet.DefaultWeight(RuleKind.SubjectRegex).Should().Be(6);
        RuleSet.DefaultWeight(RuleKind.SubjectKeyword).Should().Be(3);
        var act = () => RuleSet.DefaultWeight((RuleKind)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
