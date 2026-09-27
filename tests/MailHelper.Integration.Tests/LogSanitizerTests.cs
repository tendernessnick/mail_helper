using FluentAssertions;
using MailHelper.Infrastructure.Logging;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>日志脱敏红线（04 章 §6 / 09 章 §5）：主题截断 40 + 指纹；发件人仅域名。</summary>
public class LogSanitizerTests
{
    [Fact]
    public void Subject_TruncatesTo40WithStableFingerprint()
    {
        var longSubject = new string('主', 50);
        var result = LogSanitizer.Subject(longSubject);

        result.Should().StartWith(new string('主', 40));
        result.Should().EndWith("#" + LogSanitizer.Fingerprint8(longSubject));
        result.Should().HaveLength(40 + 1 + 8);
        LogSanitizer.Subject(longSubject).Should().Be(result); // 指纹稳定
    }

    [Fact]
    public void Subject_ShortSubject_KeptWithFingerprint()
    {
        var result = LogSanitizer.Subject("学费缴纳最终提醒");
        result.Should().StartWith("学费缴纳最终提醒#");
        result.Should().HaveLength("学费缴纳最终提醒".Length + 1 + 8);
    }

    [Fact]
    public void Subject_NullOrWhitespace_ReturnsPlaceholder()
    {
        LogSanitizer.Subject(null).Should().Be("(empty)");
        LogSanitizer.Subject("   ").Should().Be("(empty)");
    }

    [Fact]
    public void Sender_KeepsDomainOnly()
    {
        LogSanitizer.Sender("dean@ust.hk").Should().Be("ust.hk");
        LogSanitizer.Sender("no-reply@moodle.hku.hk").Should().Be("moodle.hku.hk");
    }

    [Fact]
    public void Sender_Invalid_ReturnsPlaceholder()
    {
        LogSanitizer.Sender(null).Should().Be("(unknown)");
        LogSanitizer.Sender("not-an-address").Should().Be("(unknown)");
    }

    [Fact]
    public void Fingerprint8_IsDeterministicHex()
    {
        var first = LogSanitizer.Fingerprint8("abc");
        first.Should().HaveLength(8).And.MatchRegex("^[0-9a-f]{8}$");
        LogSanitizer.Fingerprint8("abc").Should().Be(first);
        LogSanitizer.Fingerprint8("abd").Should().NotBe(first);
    }
}
