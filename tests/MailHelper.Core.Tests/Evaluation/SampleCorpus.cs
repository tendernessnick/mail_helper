using System.Globalization;
using System.Text;
using MailHelper.Core;

namespace MailHelper.Core.Tests.Evaluation;

/// <summary>合成样本语料生成器（06 章 §2：7 类别 × P0–P3 × 中英文；含 R-01~R-07 边界样例与 ReDoS 探针）。
/// 确定性种子（默认 20260926）：同种子重生成逐字节一致（防语料漂移）。
/// 干扰项（正文注入他类关键词）仅施加于带「规则内发件人锚点」的样本：无锚点的单关键词样本加干扰会被挤入模糊区，
/// 造成不可控误分——锚点保证主类证据显著领先（干扰 +3 分不改变 top1/gap 判定）。</summary>
internal static class SampleCorpus
{
    // "course"→"Course"：语料文件名历史约定（首字母大写，防生成漂移）
    private static string Capitalized(string id) => char.ToUpperInvariant(id[0]) + id[1..];

    public const int TotalCount = 600;
    public const int Seed = 20260926;

    private sealed record Spec(string FileName, string Category, Importance Importance, string FromName, string FromAddress, string Subject, string Body);

    public static void Generate(string targetDir, int seed = Seed)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var old in Directory.EnumerateFiles(targetDir, "*.*"))
        {
            File.Delete(old);
        }

        var rng = new Random(seed);
        var specs = new List<Spec>(TotalCount + 7);
        AddQuota(specs, rng, CategoryIds.Course, 135, p0Rate: 0.06);
        AddQuota(specs, rng, CategoryIds.Career, 90, p0Rate: 0.0);
        AddQuota(specs, rng, CategoryIds.Admin, 95, p0Rate: 0.15);
        AddQuota(specs, rng, CategoryIds.Finance, 60, p0Rate: 0.30);
        AddQuota(specs, rng, CategoryIds.Announce, 70, p0Rate: 0.0);
        AddQuota(specs, rng, CategoryIds.Subscription, 100, p0Rate: 0.0);
        AddQuota(specs, rng, CategoryIds.Other, 50, p0Rate: 0.0);
        AddBoundarySamples(specs);

        var labels = new List<(string File, string Category, Importance Importance)>();
        var seq = 0;
        foreach (var spec in specs)
        {
            var fileName = spec.FileName.Length > 0
                ? spec.FileName
                : $"{++seq:0000}-{Capitalized(spec.Category)}-{(int)spec.Importance}.eml"; // 文件名段保持历史首字母大写（防语料漂移）
            File.WriteAllText(Path.Combine(targetDir, fileName), ToEml(spec, rng), new UTF8Encoding(false));
            labels.Add((fileName, spec.Category, spec.Importance));
        }

        // 60/40 划分（06 §2：训练/调参 60%，评估 40% 只测不改）；R 样例强制入评估集
        var quotaLabels = labels.Take(TotalCount).ToList();
        for (var i = quotaLabels.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (quotaLabels[i], quotaLabels[j]) = (quotaLabels[j], quotaLabels[i]);
        }

        var trainFiles = quotaLabels.Take((int)(TotalCount * 0.6)).Select(l => l.File).ToHashSet();
        WriteLabels(Path.Combine(targetDir, "labels-train.csv"), labels.Where(l => trainFiles.Contains(l.File)));
        WriteLabels(Path.Combine(targetDir, "labels-eval.csv"), labels.Where(l => !trainFiles.Contains(l.File)));
    }

    private static void WriteLabels(string path, IEnumerable<(string File, string Category, Importance Importance)> rows)
    {
        var sb = new StringBuilder("file,category,importance\n");
        foreach (var row in rows.OrderBy(r => r.File, StringComparer.Ordinal))
        {
            sb.Append(row.File).Append(',').Append(row.Category.ToString().ToLowerInvariant()).Append(',').Append((int)row.Importance).Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string ToEml(Spec spec, Random rng)
    {
        var date = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(rng.Next(0, 56))
            .AddHours(rng.Next(8, 20))
            .AddMinutes(rng.Next(0, 60));
        var sb = new StringBuilder();
        sb.Append("From: ").Append(spec.FromName).Append(" <").Append(spec.FromAddress).Append(">\n");
        sb.Append("To: Student <s123456@connect.hku.hk>\n");
        sb.Append("Subject: ").Append(spec.Subject).Append('\n');
        // RFC1123 星期/月名为文化敏感（zh-CN 生成中文星期名，en-US 重生成即漂移）：锁定 Invariant
        sb.Append("Date: ").Append(date.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Content-Type: text/plain; charset=utf-8\n");
        sb.Append("MIME-Version: 1.0\n\n");
        sb.Append(spec.Body);
        return sb.ToString();
    }

    private static void AddQuota(List<Spec> specs, Random rng, string category, int count, double p0Rate)
    {
        for (var i = 0; i < count; i++)
        {
            specs.Add(NextSample(rng, category, p0Rate));
        }
    }

    private static Spec NextSample(Random rng, string category, double p0Rate)
    {
        var isP0 = rng.NextDouble() < p0Rate;
        var (subject, body, fromName, fromAddress, _) = Compose(rng, category, isP0);
        var importance = isP0 ? Importance.P0 : PickImportance(rng, category);
        return new Spec(string.Empty, category, importance, fromName, fromAddress, subject, body);
    }

    private static (string Subject, string Body, string FromName, string FromAddress, bool Anchored) Compose(
        Random rng, string category, bool isP0)
    {
        var templates = Templates[category];
        var (subjectTemplate, bodyTemplate) = templates[rng.Next(templates.Length)];
        var number = rng.Next(1, 99).ToString();
        var subject = subjectTemplate.Replace("{0}", number);
        var body = bodyTemplate.Replace("{0}", number);

        var senders = Senders[category];
        var (fromName, fromAddress) = senders[rng.Next(senders.Length)];
        var anchored = IsAnchored(fromAddress);

        if (isP0 && P0Prefixes.TryGetValue(category, out var prefixes))
        {
            subject = prefixes[rng.Next(prefixes.Length)] + subject;
        }

        if (anchored && rng.NextDouble() < 0.35)
        {
            body += "\n\nP.S. " + InterferenceSnippets[rng.Next(InterferenceSnippets.Length)];
        }

        if (rng.NextDouble() < 0.15)
        {
            body += "\n\nFrom: someone@hku.hk\nSent: Mon, 07 Sep 2026 09:00:00 +0000\n> 此前讨论的详细内容见下（引用块）。\n> Regards.";
        }

        if (rng.NextDouble() < 0.10)
        {
            body += "\n\n--\nDean Office / Faculty Office";
        }

        return (subject, body, fromName, fromAddress, anchored);
    }

    private static bool IsAnchored(string address) =>
        address.Contains("moodle", StringComparison.OrdinalIgnoreCase)
        || address.Contains("canvas", StringComparison.OrdinalIgnoreCase)
        || address.Contains("blackboard", StringComparison.OrdinalIgnoreCase)
        || address is "no-reply@instructure.com" or "careers@hku.hk" or "registry@hku.hk" or "finance@hku.hk";

    private static Importance PickImportance(Random rng, string category) => category switch
    {
        CategoryIds.Career => rng.NextDouble() < 0.40 ? Importance.P1 : Importance.P2,
        CategoryIds.Finance => rng.NextDouble() < 0.20 ? Importance.P1 : Importance.P2,
        CategoryIds.Admin => rng.NextDouble() < 0.25 ? Importance.P1 : rng.NextDouble() < 0.15 ? Importance.P3 : Importance.P2,
        CategoryIds.Course => rng.NextDouble() < 0.15 ? Importance.P1 : Importance.P2,
        CategoryIds.Announce => rng.NextDouble() < 0.20 ? Importance.P3 : Importance.P2,
        CategoryIds.Subscription => Importance.P3,
        _ => Importance.P2,
    };

    // P0 前缀按类别对齐：只含本类别关键词，避免把样本主题拼成两类别竞争（分差不足落入模糊区）
    private static readonly Dictionary<string, string[]> P0Prefixes = new()
    {
        [CategoryIds.Finance] = new[]
        {
            "FINAL REMINDER: ", "Final Reminder - ", "Overdue: ", "【紧急】学费缴费截止 ", "缴费逾期提醒 ",
        },
        [CategoryIds.Admin] = new[]
        {
            "Final Reminder - ", "Overdue: ", "最后提醒：签证材料递交截止 ", "【紧急】签证材料递交截止 ",
        },
        [CategoryIds.Course] = new[]
        {
            "Final Reminder - ", "Overdue: ", "【紧急】作业提交截止 ",
        },
    };

    private static readonly string[] InterferenceSnippets =
    {
        "Check our latest newsletter for campus stories.",
        "The library notice board also lists this event.",
        "Subscribe to the department mailing list for updates.",
        "Career fair details are attached in the newsletter.",
    };

    private static readonly Dictionary<string, (string FromName, string FromAddress)[]> Senders = new()
    {
        [CategoryIds.Course] = new[]
        {
            ("Moodle HKU", "noreply@moodle.hku.hk"),
            ("Canvas Notification", "no-reply@instructure.com"),
            ("Moodle CUHK", "noreply@moodle.cuhk.edu.hk"),
            ("Prof. Chan", "cchan@eee.hku.hk"),
            ("Teaching Team", "tteam@ust.hk"),
        },
        [CategoryIds.Career] = new[]
        {
            ("Careers Centre", "careers@hku.hk"),
            ("CEDS", "ceds@cuhk.edu.hk"),
            ("HR Team", "hr@bank.example.com"),
        },
        [CategoryIds.Admin] = new[]
        {
            ("Registry", "registry@hku.hk"),
            ("Student Affairs Office", "sao@ust.hk"),
            ("Library", "library@cuhk.edu.hk"),
            ("Immigration Liaison", "visa@hku.hk"),
        },
        [CategoryIds.Finance] = new[]
        {
            ("Finance Office", "finance@hku.hk"),
            ("Bursary", "bursary@cuhk.edu.hk"),
        },
        [CategoryIds.Announce] = new[]
        {
            ("University Communications", "comms@hku.hk"),
            ("Faculty Office", "faculty@cuhk.edu.hk"),
        },
        [CategoryIds.Subscription] = new[]
        {
            ("Campus Deals", "deals@shop.example.com"),
            ("Alumni E-News", "enews@alumni.example.com"),
        },
        [CategoryIds.Other] = new[]
        {
            ("Alex Wong", "alexw@example.com"),
            ("王同学", "classmate@example.com"),
            ("Helpdesk", "noanswer@random.example"),
        },
    };

    private static readonly Dictionary<string, (string Subject, string Body)[]> Templates = new()
    {
        [CategoryIds.Course] = new (string Subject, string Body)[]
        {
            ("CS1012 Assignment {0} released", "Assignment {0} has been released on the course page. Submit before the cut-off shown on Moodle."),
            ("Quiz {0} results are available", "Your Quiz {0} score is now visible in the gradebook. Please review the feedback."),
            ("Lecture notes for week {0}", "Lecture notes and coursework slides for week {0} are uploaded."),
            ("Midterm arrangement for CS{0}", "The midterm exam will be held in week {0}. Seating plan attached."),
            ("Lab session {0} rescheduled", "Lab session {0} moves to another room on Friday afternoon."),
            ("数据结构 第{0}周课件已上传", "本周课件与作业说明已更新，请自行下载复习。"),
            ("作业{0}已发布", "作业{0}请在课程平台提交，注意平台内显示的提交时间。"),
            ("测验成绩已公布", "本学期第{0}次测验成绩已在系统公布，可查看批注。"),
            ("期中考试安排通知", "期中考试将于第{0}周进行，请提前确认考场与座位。"),
            ("实验课分组通知", "第{0}次实验课分组名单已出，请按时到场。"),
        },
        [CategoryIds.Career] = new (string Subject, string Body)[]
        {
            ("Summer Internship Program {0} Open", "Applications for the summer internship intake {0} are now open. Submit your resume early."),
            ("Interview Invitation - Ref {0}", "We are pleased to invite you to a first-round interview. Kindly confirm your availability."),
            ("Career Fair {0} Registration", "Join the career fair and meet employers hiring for full-time and internship roles."),
            ("CV Workshop for Finalists", "Bring your resume for a hands-on CV workshop with alumni reviewers."),
            ("Job Opening: Analyst (Ref {0})", "A graduate analyst job opening matching your profile was just posted."),
            ("暑期实习计划{0}开放申请", "暑期实习项目现开放申请，请尽快投递简历。"),
            ("面试邀约：第一轮技术面", "恭喜进入下一轮面试，请确认可用时间段。"),
            ("招聘会{0}报名开启", "本次招聘会含多家雇主，欢迎携带简历现场交流。"),
            ("简历工作坊报名确认", "简历工作坊席位已确认，请准时参加。"),
        },
        [CategoryIds.Admin] = new (string Subject, string Body)[]
        {
            ("Student visa extension guidance", "Please prepare documents for your student visa extension before the term starts."),
            ("Library notice: item {0} due", "A library item borrowed on your account is approaching its return time."),
            ("Course registration for Semester {0}", "Course registration for Semester {0} opens next Monday at 9am."),
            ("Orientation week schedule", "The orientation week schedule for new students is attached."),
            ("Student affairs circular {0}", "Circular {0} from the Student Affairs Office regarding campus services."),
            ("IANG application briefing", "A briefing session on the IANG arrangement will be held for graduating students."),
            ("学生签证续期指引", "请提前准备学生签证续期材料，详见附件清单。"),
            ("图书馆通知：图书即将到期", "您借阅的图书即将到归还时间，请及时处理。"),
            ("选课注册常见问题", "选课注册通道即将开启，请提前核对选课志愿。"),
            ("迎新周活动安排", "迎新周各项活动安排已发布，请查收。"),
            ("学生事务处通告第{0}号", "关于校园服务时间调整的事务处通告。"),
        },
        [CategoryIds.Finance] = new (string Subject, string Body)[]
        {
            ("Tuition Fee Invoice {0}", "Your tuition fee invoice {0} is ready. Kindly settle the payment per the schedule."),
            ("Payment due for Semester {0}", "This is a reminder that the semester {0} installment is approaching its payment window."),
            ("Outstanding fee notice", "Our record shows an outstanding fee balance on your student account."),
            ("Installment plan confirmation", "Your tuition installment plan has been confirmed for the academic year."),
            ("学费账单{0}已生成", "本学期学费账单已生成，请按说明完成缴费。"),
            ("缴费提醒：第二期分期", "第二期学费分期即将进入缴费窗口，请留意账户状态。"),
            ("账户余额通知", "您的学生账户存在待缴费用，详见账单明细。"),
        },
        [CategoryIds.Announce] = new (string Subject, string Body)[]
        {
            ("Distinguished lecture series {0}: Frontiers of AI", "The lecture series continues with a talk on AI frontiers. All are welcome."),
            ("Campus announcement: shuttle bus adjustment", "The campus shuttle bus timetable will be adjusted from next week."),
            ("Seminar series {0}: academic writing", "The seminar series on academic writing is open for registration."),
            ("Campus event: wellness week", "Wellness week activities including workshops and walks are scheduled."),
            ("校园公告：校车时刻调整", "下周起校车时刻表调整，请查阅新版安排。"),
            ("讲座系列：人工智能前沿", "本场讲座面向全校师生开放，无需报名。"),
            ("活动预告：心理健康周", "心理健康周活动安排已发布。"),
        },
        [CategoryIds.Subscription] = new (string Subject, string Body)[]
        {
            ("Weekly newsletter #{0}", "Your weekly newsletter with campus stories. You can unsubscribe at any time."),
            ("Best student deals this week", "Special deal selection for students. Subscribe for weekly promotions."),
            ("Alumni e-news {0}", "The alumni e-news digest for this month. Manage your preferences or unsubscribe from this e-news."),
            ("Student union mailing list", "You are subscribed to the student union mailing list."),
            ("Final reminder to renew your membership", "This is a gentle reminder to renew your club membership. Unsubscribe if you no longer wish to hear from us."),
            ("学生电子报第{0}期", "本期电子报精选校园资讯，可随时退订。"),
            ("本周优惠精选", "为学生精选的本周优惠，订阅后每周收到推广。"),
        },
        [CategoryIds.Other] = new (string Subject, string Body)[]
        {
            ("Thanks for the notes", "Thanks for sharing the notes, really helpful!"),
            ("Lunch on Friday?", "Shall we grab lunch on Friday at the canteen?"),
            ("Re: quick question about the lab", "Answering your question about the lab equipment usage."),
            ("周末爬山吗", "这周末去爬山吗？天气看起来不错。"),
            ("文件已收到", "好的，文件已收到，谢谢！"),
        },
    };

    private static void AddBoundarySamples(List<Spec> specs)
    {
        specs.Add(new Spec("r01-finance-p0.eml", CategoryIds.Finance, Importance.P0, "Finance Office", "finance@hku.hk",
            "FINAL REMINDER: Tuition Fee Payment",
            "Your tuition for Semester A must be settled by 30 September. Overdue payments incur a surcharge."));
        specs.Add(new Spec("r02-career-p1.eml", CategoryIds.Career, Importance.P1, "HR Team", "hr@bank.example.com",
            "面试邀约 Interview Invitation - Summer Intern",
            "We would like to invite you to an interview for the summer internship programme next week."));
        specs.Add(new Spec("r03-course-p2.eml", CategoryIds.Course, Importance.P2, "Moodle HKU", "noreply@moodle.hku.hk",
            "课件更新 CS1012 Week 3 slides",
            "Course slides for week 3 have been updated on Moodle."));
        specs.Add(new Spec("r04-subscription-p3.eml", CategoryIds.Subscription, Importance.P3, "Campus Deals", "deals@shop.example.com",
            "Student Newsletter - September",
            "Click here to read the September newsletter, or unsubscribe from this list."));
        specs.Add(new Spec("r05-other-p2.eml", CategoryIds.Other, Importance.P2, "Stranger", "stranger@nowhere.example",
            "嗨", "随便聊聊，无关键词。"));
        specs.Add(new Spec("r06-course-p2.eml", CategoryIds.Course, Importance.P2, "Moodle HKU", "noreply@moodle.hku.hk",
            "Your course timetable is ready",
            "Your course timetable is ready.\n\nFrom: office@hku.hk\nSent: Mon, 07 Sep 2026 09:00:00 +0000\n> deadline tomorrow for old drafts"));
        specs.Add(new Spec("r07-other-p2.eml", CategoryIds.Other, Importance.P2, "Probe", "probe@example.com",
            new string('a', 60) + "!",
            "ReDoS probe subject with no category keywords."));
    }
}
