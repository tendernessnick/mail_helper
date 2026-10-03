using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MailHelper.Core.Schedule;

/// <summary>解析出的单个 DDL 候选（S18/CHG-015）。DueLocal 为 Canvas 邮件给出的本机时区墙上时间
/// （Kind=Unspecified），UTC 换算由 ScheduleService 负责（便于时区无关的纯函数测试）。</summary>
public sealed record DdlCandidate(
    string Title,
    string? Course,
    string? CourseCode,
    string? Link,
    string DedupeKey,
    DateTime DueLocal);

/// <summary>Canvas 通知邮件 DDL 解析器（S18/CHG-015，ADR-006；纯函数零 IO）。
/// 识别「Assignment/Quiz Created / Due Date Changed – <标题>, <课程>」标题行 + 近邻「due: <时间>」行 +
/// 「Click to view <链接>」，周报摘要（多条并列）与单封通知同构处理。
/// 正则 100ms 超时同 CLASS-001；标题行缺失 due 时不生成候选（评分/成绩类通知自然过滤）。</summary>
public sealed partial class CanvasDdlExtractor
{
    /// <summary>来源标识（ScheduleItem.Source）。</summary>
    public const string SourceCanvas = "canvas";

    /// <summary>年份推断回看窗口：解析结果早于 now−45 天视为「去年同月」（跨年学期摘要），+1 年。</summary>
    public const int YearRolloverDays = 45;

    /// <summary>候选条目单封上限（异常正文防御）。</summary>
    public const int MaxCandidatesPerMail = 50;

    private static readonly Regex TitleLineRegex = new(
        @"(?:Assignment|Quiz)\s+(?:Created|Due\s+Date\s+Changed)|Due\s+Date\s+Changed",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>标题分隔：「- / – / ：」后跟正文；与 05 版摘要样式「Assignment Created - X, Y」对齐。</summary>
    private static readonly Regex TitleSeparatorRegex = new(
        @"[-–—:]\s*", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>due 行（无锚点）：段落内首个 "due:" 引导的同行内容；兼容行首与标题行内联两种排版。</summary>
    private static readonly Regex DueLineRegex = new(
        @"due\s*:\s*(?<due>[^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly Regex LinkRegex = new(
        @"https?://[^\s<>""')\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>Canvas 资源链接（作业/测验）——去重键的稳定来源（D-70）。</summary>
    private static readonly Regex CanvasResourceRegex = new(
        @"/courses/(?<cid>\d+)/(?<kind>assignments|quizzes)/(?<aid>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>港校课程代码（与规则包 Course-Code 同源，docs/04 §5.3）。</summary>
    private static readonly Regex CourseCodeRegex = new(
        @"\b[A-Z]{2,4}\d{4}(W\d)?\b", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4, ["may"] = 5, ["jun"] = 6, ["june"] = 6,
        ["jul"] = 7, ["july"] = 7, ["aug"] = 8, ["august"] = 8, ["sep"] = 9, ["sept"] = 9,
        ["september"] = 9, ["oct"] = 10, ["october"] = 10, ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["december"] = 12,
    };

    /// <summary>解析一封邮件正文中的全部 DDL 候选（去重键批内去重；顺序保持正文出现序）。</summary>
    public IReadOnlyList<DdlCandidate> Parse(string? bodyText, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return [];
        }

        var text = bodyText;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<DdlCandidate>();
        foreach (var match in TitleLineRegex.Matches(text).Cast<Match>())
        {
            if (candidates.Count >= MaxCandidatesPerMail)
            {
                break;
            }

            var candidate = TryBuildCandidate(text, match, nowUtc);
            if (candidate is { } built && seenKeys.Add(built.DedupeKey))
            {
                candidates.Add(built); // 正文出现序（批内按去重键去重）
            }
        }

        return candidates;
    }

    /// <summary>单条候选装配：拆标题/课程 → 近邻 due 行 → 近邻 Canvas 链接 → 去重键。缺 due 行返回 null。</summary>
    private DdlCandidate? TryBuildCandidate(string text, Match titleMatch, DateTime nowUtc)
    {
        var rest = ExtractRest(text, titleMatch);
        if (string.IsNullOrWhiteSpace(rest)
            || rest.TrimStart().StartsWith("due", StringComparison.OrdinalIgnoreCase))
        {
            return null; // 标题缺失（关键词行紧邻 due 行）的变体：不生成脏条目
        }

        var (title, course, courseCode) = SplitTitleCourse(rest);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // 在标题行之后、下一个标题行之前的窗口内找 due 与链接（周报摘要条目相邻不串扰）
        var windowEnd = FindNextTitleIndex(text, titleMatch.Index + titleMatch.Length);
        var segment = text.Substring(
            titleMatch.Index + titleMatch.Length,
            Math.Min(windowEnd - titleMatch.Index - titleMatch.Length, 1200));

        var dueMatch = DueLineRegex.Match(segment);
        if (!dueMatch.Success)
        {
            return null; // 无截止时间不生成条目（成绩/讨论类通知自然过滤）
        }

        var due = ParseDueText(dueMatch.Groups["due"].Value, nowUtc);
        if (due is null)
        {
            return null;
        }

        var link = LinkRegex.Match(segment) is { Success: true } lm ? lm.Value.TrimEnd('.', ',', ';') : null;
        return new DdlCandidate(
            title, course, courseCode, link,
            BuildDedupeKey(link, title, course, due.Value), due.Value);
    }

    /// <summary>标题正文：同行时取首个「- / – / :」之后的部分；关键词独占一行时取下一行（去可选分隔符）。</summary>
    private static string ExtractRest(string text, Match titleMatch)
    {
        var lineEnd = text.IndexOf('\n', titleMatch.Index + titleMatch.Length);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[(titleMatch.Index + titleMatch.Length)..lineEnd].Trim();
        var sep = TitleSeparatorRegex.Match(line);
        if (sep.Success)
        {
            return line[(sep.Index + sep.Length)..].Trim();
        }

        if (line.Length == 0)
        {
            // 正文独占下一行的排版变体（"Assignment Created\nWeek 5 assignment, …"）
            var nextEnd = text.IndexOf('\n', lineEnd + 1);
            var next = text[(lineEnd + 1)..(nextEnd < 0 ? text.Length : nextEnd)].Trim();
            var sep2 = TitleSeparatorRegex.Match(next);
            return sep2.Success ? next[(sep2.Index + sep2.Length)..].Trim() : next;
        }

        return line;
    }

    /// <summary>下一个标题关键词位置（窗口边界；找不到则文末）。</summary>
    private static int FindNextTitleIndex(string text, int start)
    {
        var next = TitleLineRegex.Match(text[start..]);
        return next.Success ? start + next.Index : text.Length;
    }

    /// <summary>按最后一个逗号拆「标题, 课程」：课程段含课程代码才拆分（防标题内逗号误切，D-71）。</summary>
    private (string Title, string? Course, string? CourseCode) SplitTitleCourse(string rest)
    {
        var code = CourseCodeRegex.Match(rest);
        var courseCode = code.Success ? code.Value : null;
        var lastComma = rest.LastIndexOfAny([',', '，']);
        if (lastComma > 0 && lastComma < rest.Length - 1)
        {
            var coursePart = rest[(lastComma + 1)..].Trim();
            if (coursePart.Length <= 80 && CourseCodeRegex.IsMatch(coursePart))
            {
                return (rest[..lastComma].Trim(), coursePart, courseCode);
            }
        }

        return (rest.Trim(), null, courseCode);
    }

    /// <summary>截止时间解析（S18）：支持 "Oct 9 at 11:59pm"、"September 28 at 5pm"、"Oct 9, 2026"、
    /// "10/9"、"10月9日 23:59"；无时间默认 23:59；无年份按当前年、早于 now−45 天滚动 +1 年。
    /// 解析失败返回 null（该条目跳过，SCHED-001 由调用方计数）。</summary>
    public DateTime? ParseDueText(string raw, DateTime nowUtc)
    {
        var value = raw.Trim().TrimEnd('.', '。');
        var nowLocal = nowUtc.ToLocalTime();

        // 英文月名：Oct 9 [at 11:59pm] [, 2026]
        var m = Regex.Match(value,
            @"^(?<mon>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:\s*,?\s*(?<year>\d{4}))?"
            + @"(?:\s+(?:at|by|@)\s*(?<time>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (m.Success && Months.TryGetValue(m.Groups["mon"].Value, out var month))
        {
            var day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
            var year = m.Groups["year"].Success
                ? int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture)
                : nowLocal.Year;
            var time = ParseTimeOfDay(m.Groups["time"].Success ? m.Groups["time"].Value : null);
            var due = new DateTime(year, month, day, time.Hours, time.Minutes, 0, DateTimeKind.Unspecified);
            if (!m.Groups["year"].Success && due < nowLocal.AddDays(-YearRolloverDays))
            {
                due = due.AddYears(1); // 跨年学期：无年份的旧日期滚到下一年（ADR-006）
            }

            return due;
        }

        // 数字式：10/9 [at 11:59pm] [/2026]（月/日，港校惯用）
        m = Regex.Match(value,
            @"^(?<mon>\d{1,2})/(?<day>\d{1,2})(?:/(?<year>\d{4}))?(?:\s+(?:at|by)\s*(?<time>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (m.Success)
        {
            var numMonth = int.Parse(m.Groups["mon"].Value, CultureInfo.InvariantCulture);
            var numDay = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
            if (numMonth is >= 1 and <= 12 && numDay is >= 1 and <= 31)
            {
                var year = m.Groups["year"].Success
                    ? int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture)
                    : nowLocal.Year;
                var time = ParseTimeOfDay(m.Groups["time"].Success ? m.Groups["time"].Value : null);
                var due = new DateTime(year, numMonth, numDay, time.Hours, time.Minutes, 0, DateTimeKind.Unspecified);
                if (!m.Groups["year"].Success && due < nowLocal.AddDays(-YearRolloverDays))
                {
                    due = due.AddYears(1);
                }

                return due;
            }
        }

        // 中文式：10月9日 [23:59]
        m = Regex.Match(value,
            @"^(?<mon>\d{1,2})月(?<day>\d{1,2})日(?:\s*(?<time>\d{1,2}[:：]\d{2})?)?$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (m.Success)
        {
            var cnMonth = int.Parse(m.Groups["mon"].Value, CultureInfo.InvariantCulture);
            var cnDay = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
            var time = ParseTimeOfDay(
                m.Groups["time"].Success ? m.Groups["time"].Value.Replace('：', ':') : null);
            return new DateTime(nowLocal.Year, cnMonth, cnDay, time.Hours, time.Minutes, 0, DateTimeKind.Unspecified);
        }

        return null;
    }

    /// <summary>时间解析："11:59pm"、"5pm"、"5:30 am"、"19:00"、"noon"→12:00、"midnight"→00:00、缺省 23:59。</summary>
    private static (int Hours, int Minutes) ParseTimeOfDay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (23, 59);
        }

        var value = raw.Trim().ToLowerInvariant();
        if (value.Contains("noon"))
        {
            return (12, 0);
        }

        if (value.Contains("midnight"))
        {
            return (0, 0);
        }

        var m = Regex.Match(value, @"^(?<h>\d{1,2})(?:[:.](?<min>\d{2}))?\s*(?<ampm>am|pm)?$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!m.Success)
        {
            return (23, 59);
        }

        var hours = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minutes = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups["ampm"].Value == "pm" && hours < 12)
        {
            hours += 12;
        }
        else if (m.Groups["ampm"].Value == "am" && hours == 12)
        {
            hours = 0;
        }

        return (hours, minutes);
    }

    /// <summary>去重键（D-70）：Canvas 资源链接取 course/assignment 复合 id；无链接退化 sha1(标题|课程|due) 前 16 位。</summary>
    public static string BuildDedupeKey(string? link, string title, string? course, DateTime dueLocal)
    {
        if (!string.IsNullOrEmpty(link))
        {
            var m = CanvasResourceRegex.Match(link);
            if (m.Success)
            {
                return $"canvas-a:{m.Groups["cid"].Value}:{m.Groups["aid"].Value}";
            }
        }

        var payload = $"{title}|{course}|{dueLocal:yyyy-MM-dd HH:mm}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant()[..16];
        return $"canvas-t:{hash}";
    }
}
