using System.Text;
using MailHelper.Core;

namespace MailHelper.Core.Tests.Evaluation;

internal sealed record CorpusSample(string File, MailCategory Category, Importance Importance, string FromName, string FromAddress, string Subject, string Body);

/// <summary>极简 .eml 解析（D-42：合成语料为固定格式——UTF-8 无折叠头、无 MIME 多部分；
/// 引入真实邮件样本时升级 MailKit）。+ 语料目录/规则包定位（向上查找仓库根）。</summary>
internal static class CorpusLoader
{
    public static List<CorpusSample> Load(string corpusDir, string labelsFile)
    {
        var samples = new List<CorpusSample>();
        foreach (var line in File.ReadAllLines(Path.Combine(corpusDir, labelsFile)).Skip(1))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var parts = line.Split(',');
            var eml = ParseEml(Path.Combine(corpusDir, parts[0]));
            samples.Add(new CorpusSample(
                parts[0],
                Enum.Parse<MailCategory>(parts[1], ignoreCase: true),
                (Importance)int.Parse(parts[2]),
                eml.FromName, eml.FromAddress, eml.Subject, eml.Body));
        }

        return samples;
    }

    public static (string FromName, string FromAddress, string Subject, string Body) ParseEml(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        string? from = null, subject = null;
        var bodyStart = lines.Length;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                bodyStart = i + 1;
                break;
            }

            if (lines[i].StartsWith("From: ", StringComparison.Ordinal))
            {
                from = lines[i]["From: ".Length..];
            }
            else if (lines[i].StartsWith("Subject: ", StringComparison.Ordinal))
            {
                subject = lines[i]["Subject: ".Length..];
            }
        }

        var body = string.Join("\n", lines.Skip(bodyStart));
        var (name, address) = ParseAddress(from ?? string.Empty);
        return (name, address, subject ?? string.Empty, body);
    }

    private static (string Name, string Address) ParseAddress(string from)
    {
        var lt = from.LastIndexOf('<');
        var gt = from.LastIndexOf('>');
        if (lt < 0 || gt < lt)
        {
            return (from.Trim('"', ' '), string.Empty);
        }

        var address = from[(lt + 1)..gt].Trim();
        var name = from[..lt].Trim().Trim('"', ' ');
        return (name.Length == 0 ? address : name, address);
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MailHelper.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException($"未找到仓库根（MailHelper.sln），从 {AppContext.BaseDirectory} 向上查找");
    }

    public static string FindRepoPath(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException(
            $"未找到 {string.Join('/', relativeSegments)}（从 {AppContext.BaseDirectory} 向上查找）");
    }
}
