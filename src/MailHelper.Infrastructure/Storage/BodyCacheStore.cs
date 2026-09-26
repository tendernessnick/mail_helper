using System.Security.Cryptography;
using System.Text;
using MailHelper.Core.Abstractions;

namespace MailHelper.Infrastructure.Storage;

/// <summary>正文磁盘缓存（IBodyCache 实现；03 章 §6：bodies\{accountId}\{sha1(html)}.html，LRU 清理）。
/// LRU 依据文件 LastWriteTimeUtc（读/写时显式 touch），清理在每次保存后同步执行（D-18）。</summary>
public sealed class BodyCacheStore : IBodyCache
{
    private readonly string _rootDir;
    private readonly long _limitBytes;
    private readonly object _gate = new();

    public BodyCacheStore(string rootDir, long limitBytes = 2L * 1024 * 1024 * 1024)
    {
        _rootDir = Path.GetFullPath(rootDir);
        _limitBytes = limitBytes;
        Directory.CreateDirectory(_rootDir);
    }

    public Task<string> SaveAsync(string accountId, string html, CancellationToken ct)
    {
        ValidateAccountId(accountId);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
            var relative = $"{accountId}/{sha1}.html";
            var fullPath = ResolvePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, html);
            File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);
            CleanupOverLimit();
            return Task.FromResult(relative);
        }
    }

    public Task<string?> ReadAsync(string relativePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var fullPath = ResolvePath(relativePath);
            if (!File.Exists(fullPath))
            {
                return Task.FromResult<string?>(null);
            }

            var html = File.ReadAllText(fullPath);
            File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow); // touch：刚读过的内容在 LRU 中视为最新
            return Task.FromResult<string?>(html);
        }
    }

    private string ResolvePath(string relative)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_rootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("非法的缓存相对路径", nameof(relative)); // 目录穿越防护（SEC-04 意识）
        }

        return fullPath;
    }

    private void CleanupOverLimit()
    {
        var files = Directory.EnumerateFiles(_rootDir, "*.html", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= _limitBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private static void ValidateAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)
            || accountId.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
        {
            throw new ArgumentException("accountId 仅允许字母、数字、-、_", nameof(accountId));
        }
    }
}
