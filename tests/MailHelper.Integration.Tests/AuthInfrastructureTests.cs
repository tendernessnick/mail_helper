using System.Security.Cryptography;
using FluentAssertions;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace MailHelper.Integration.Tests;

/// <summary>认证基础设施：DPAPI 文件保护 + MSAL 异常→错误码映射（04 §5 AUTH 段）。</summary>
public class AuthInfrastructureTests : TempDirTestBase
{
    private string CachePath => Path.Combine(Dir, "msal.cache.bin");

    private static byte[] Entropy(string value) => System.Text.Encoding.UTF8.GetBytes(value);

    [Fact]
    public void Dpapi_WriteThenRead_Roundtrips_AndIsEncryptedAtRest()
    {
        var protector = new DpapiFileProtector(Entropy("MailHelper.TokenCache.v1"));

        protector.WriteString(CachePath, "token-secret-payload");

        File.Exists(CachePath).Should().BeTrue();
        // 明文不落盘（SEC-01）：Latin1 逐字节映射后做子串检查（等价字节序列）
        System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(CachePath))
            .Should().NotContain("token-secret");
        protector.ReadString(CachePath).Should().Be("token-secret-payload");
    }

    [Fact]
    public void Dpapi_CorruptFile_ReadsAsNull()
    {
        var protector = new DpapiFileProtector(Entropy("MailHelper.TokenCache.v1"));
        File.WriteAllBytes(CachePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        protector.ReadString(CachePath).Should().BeNull(); // 视为空缓存，不抛异常
    }

    [Fact]
    public void Dpapi_WrongEntropy_ReadsAsNull()
    {
        var writer = new DpapiFileProtector(Entropy("entropy-A"));
        writer.WriteString(CachePath, "secret");

        var reader = new DpapiFileProtector(Entropy("entropy-B"));

        reader.ReadString(CachePath).Should().BeNull(); // 熵不符不可解（拷贝/篡改场景）
    }

    [Fact]
    public void Mapper_OperationCanceled_MapsToAuth001()
    {
        AuthErrorMapper.MapException(new OperationCanceledException())
            .Should().Be(AuthErrorCodes.Cancelled);
    }

    [Theory]
    [InlineData("65001", "AADSTS65001: The user or administrator has not consented to use the application.")]
    [InlineData("500021", "AADSTS500021: No tenant-identifying information found.")]
    public void Mapper_AdminConsentAadsts_MapsToAuth002(string errorCode, string message)
    {
        var exception = new MsalServiceException(errorCode, message);

        AuthErrorMapper.MapException(exception).Should().Be(AuthErrorCodes.AdminConsentRequired);
    }

    [Fact]
    public void Mapper_UiRequired_MapsToAuth003()
    {
        var exception = new MsalUiRequiredException("no_tokens", "No refresh token found");

        AuthErrorMapper.MapException(exception).Should().Be(AuthErrorCodes.ReauthRequired);
    }

    [Fact]
    public void Mapper_UnlistedException_MapsToAuth003()
    {
        AuthErrorMapper.MapException(new InvalidOperationException("boom"))
            .Should().Be(AuthErrorCodes.ReauthRequired); // D-28：未列举异常统一按需重新登录处理
    }
}
