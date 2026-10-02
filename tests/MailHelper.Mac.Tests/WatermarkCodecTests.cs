using FluentAssertions;
using MailHelper.Infrastructure.Sync.OutlookMac;
using Xunit;

namespace MailHelper.Mac.Tests;

/// <summary>增量水位编解码（docs/10 §6.3：otm:1:&lt;epoch&gt;；不可解析→首次全量）。</summary>
public class WatermarkCodecTests
{
    [Fact]
    public void Encode_TryParse_RoundTrips()
    {
        var link = WatermarkCodec.Encode(1780000000);
        link.Should().Be("otm:1:1780000000");
        WatermarkCodec.TryParse(link, out var epoch).Should().BeTrue();
        epoch.Should().Be(1780000000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("outlook://inbox?received=123")]
    [InlineData("otm:2:123")]
    [InlineData("otm:1:abc")]
    [InlineData("otm:1:")]
    public void TryParse_InvalidOrForeignLinks_ReturnFalse(string? link)
    {
        WatermarkCodec.TryParse(link, out _).Should().BeFalse("不可解析水位应降级首次全量（防御解析）");
    }
}
