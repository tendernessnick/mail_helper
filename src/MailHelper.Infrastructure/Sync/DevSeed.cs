using MailHelper.Core;
using MailHelper.Core.Domain;
using MailHelper.Infrastructure.Sync;

namespace MailHelper.Infrastructure.Sync;

/// <summary>DEV 模式种子数据（MS3 起双 UI 共用）（M1 出口：假令牌环境下全链路演示；20 封覆盖 7 类别 × P0-P3 × 中英文）。</summary>
public static class DevSeed
{
    public static FakeMailProvider BuildMailProvider()
    {
        var provider = new FakeMailProvider
        {
            CompletedLink = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=dev-seed",
        };

        var page1 = new List<RemoteMessage>
        {
            R("d01", "FINAL REMINDER: Tuition Fee Payment", "Finance Office", "finance@hku.hk",
                "Your tuition for Semester A must be settled by 30 Sep.", CategoryIds.Finance, Importance.P0),
            R("d02", "面试邀约 Interview Invitation", "HR Team", "hr@bank.example.com",
                "We invite you to a first-round interview next week.", CategoryIds.Career, Importance.P1),
            R("d03", "学生签证续期指引", "Registry", "registry@hku.hk",
                "请提前准备学生签证续期材料。", CategoryIds.Admin, Importance.P1),
            R("d04", "CS1012 Assignment 3 released", "Moodle HKU", "noreply@moodle.hku.hk",
                "Assignment 3 has been released.", CategoryIds.Course, Importance.P2),
            R("d05", "Weekly newsletter #42", "Campus Deals", "deals@shop.example.com",
                "Your weekly deals digest. Unsubscribe anytime.", CategoryIds.Subscription, Importance.P3),
            R("d06", "讲座系列：人工智能前沿", "University Comms", "comms@hku.hk",
                "本场讲座面向全校师生开放。", CategoryIds.Announce, Importance.P2),
            R("d07", "学费账单已生成", "Finance Office", "finance@hku.hk",
                "本学期学费账单已生成，请完成缴费。", CategoryIds.Finance, Importance.P1),
            R("d08", "Lab session 5 rescheduled", "Moodle CUHK", "noreply@moodle.cuhk.edu.hk",
                "Lab session moves to Friday.", CategoryIds.Course, Importance.P2),
            R("d09", "Career Fair 2026 Registration", "Careers Centre", "careers@hku.hk",
                "Meet employers hiring interns.", CategoryIds.Career, Importance.P1),
            R("d10", "嗨，周末爬山吗", "Alex Wong", "alexw@example.com",
                "这周末去爬山吗？", CategoryIds.Other, Importance.P2),
        };
        var page2 = new List<RemoteMessage>
        {
            R("d11", "Library notice: item due tomorrow", "Library", "library@cuhk.edu.hk",
                "A borrowed item is due.", CategoryIds.Admin, Importance.P2),
            R("d12", "Payment due for Semester B", "Bursary", "bursary@cuhk.edu.hk",
                "Second installment approaching.", CategoryIds.Finance, Importance.P1),
            R("d13", "期中考试安排通知", "Moodle HKU", "noreply@moodle.hku.hk",
                "期中考试将于第 10 周进行。", CategoryIds.Course, Importance.P1),
            R("d14", "Best student deals", "Campus Deals", "deals@shop.example.com",
                "Special deals for students.", CategoryIds.Subscription, Importance.P3),
            R("d15", "校园公告：校车时刻调整", "Faculty Office", "faculty@cuhk.edu.hk",
                "下周起校车时刻表调整。", CategoryIds.Announce, Importance.P2),
            R("d16", "Orientation week schedule", "Student Affairs", "sao@ust.hk",
                "Orientation schedule attached.", CategoryIds.Admin, Importance.P1),
            R("d17", "简历工作坊报名确认", "Careers Centre", "careers@hku.hk",
                "席位已确认。", CategoryIds.Career, Importance.P2),
            R("d18", "Overdue: Outstanding fee notice", "Finance Office", "finance@hku.hk",
                "Outstanding balance on your account.", CategoryIds.Finance, Importance.P0),
            R("d19", "文件已收到", "王同学", "classmate@example.com",
                "好的，谢谢！", CategoryIds.Other, Importance.P3),
            R("d20", "活动预告：心理健康周", "University Comms", "comms@hku.hk",
                "活动安排已发布。", CategoryIds.Announce, Importance.P3),
        };
        provider.Pages.Add(page1);
        provider.Pages.Add(page2);
        return provider;
    }

    private static RemoteMessage R(string id, string subject, string fromName, string fromAddress,
        string preview, string category, Importance importance) => new(
        id, $"<{id}@dev>", subject, fromName, fromAddress, preview, null,
        new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 6000)),
        HasAttachments: false, IsRead: false, ChangeKind.Added, "ck-" + id);
}
