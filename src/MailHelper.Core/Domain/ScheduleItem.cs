namespace MailHelper.Core.Domain;

/// <summary>日程条目（S18/CHG-015：从 Canvas 通知邮件识别的作业 DDL，ADR-006）。
/// due 语义：Canvas 邮件正文给出的截止时间为账户时区墙上时间，本应用按「本机时区墙上时间」解释
/// 并转为 UTC 瞬间存储（港校学生 Canvas 时区=本机时区；已知限制记录于 ADR-006）。</summary>
public sealed record ScheduleItem(
    string Id,
    string AccountId,
    string? MessageId,       // 最近一次提及该作业的邮件（来源可追溯；摘要周报会反复更新此指向）
    string Source,           // 来源标识，v1 恒为 "canvas"（扩展点：ICS/手工/API）
    string Title,            // 作业标题（如 "Week 5 assignment"）
    string? Course,          // 课程名（如 "IS6400 Business Data Analytics"）
    string? CourseCode,      // 课程代码（如 "IS6400"；正则同规则包 Course-Code）
    string? Link,            // Canvas 原文链接（作业/测验页）
    string DedupeKey,        // 去重键：canvas-a:{courseId}:{assignmentId} 或 canvas-t:{sha1 前 16}
    DateTime DueAtUtc,       // 截止时间（本机时区解释 → UTC；展示时 ToLocalTime）
    ScheduleItemStatus Status,
    long? RemindedDueAt,     // 已提醒时的 due（Unix 秒）；与当前 due 不同则恢复提醒资格
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
