using FluentAssertions;
using MailHelper.Infrastructure;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>S0 骨架冒烟：验证测试工程对基础设施层的引用连通（S4 起承载 WireMock 契约测试）。</summary>
public class SanityTests
{
    [Fact]
    public void InfrastructureLayerIsReferenced()
    {
        InfrastructureAssembly.LayerName.Should().Be("MailHelper.Infrastructure");
    }
}
