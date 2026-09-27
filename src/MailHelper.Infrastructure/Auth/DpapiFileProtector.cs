using System.Security.Cryptography;
using System.Text;

namespace MailHelper.Infrastructure.Auth;

/// <summary>DPAPI 文件保护器（09 章 §3：CurrentUser + AdditionalEntropy；SEC-01）。
/// 解密失败（文件损坏/熵不符）→ 删除文件并返回 null（视为空缓存，不崩溃）。</summary>
public sealed class DpapiFileProtector
{
    private readonly byte[] _entropy;

    public DpapiFileProtector(byte[] entropy) => _entropy = entropy;

    public byte[]? ReadFileBytes(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 令牌缓存仅支持 Windows（产品目标平台）");
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), _entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            TryDelete(path); // 损坏/熵不符 → 清除坏文件，按空缓存处理
            return null;
        }
    }

    public void WriteFileBytes(string path, byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 令牌缓存仅支持 Windows（产品目标平台）");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.CurrentUser));
    }

    public string? ReadString(string path) =>
        ReadFileBytes(path) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    public void WriteString(string path, string value) => WriteFileBytes(path, Encoding.UTF8.GetBytes(value));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
