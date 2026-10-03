# MailHelper S18 专项执行指令：Canvas 作业 DDL 自动识别 × 日程记录模块

> 版本：v1.0（2026-10-04）　|　前置：v0.7.0 已发布（Mac 阶段 MS0–MS10 合并，262 测试全绿）
> 性质：用户指定——Canvas 会定期把「Assignment Created / due: …」通知邮件发进学校邮箱，
> 应用应自动识别其中的作业截止时间（DDL），沉淀为一份可持续维护的「日程记录」，到期前提醒。
> 执行方式：主控代理以多身份流转自动执行（产品经理→AI 全栈工程师→测试工程师→交付经理），完成后用户晨间验收。

---

## 〇、纪律继承

与总控指令/S13/S14 完全一致：PROGRESS.md 唯一事实源；新增能力走 **CHG-015** 登记（02 章功能清单之外的演进）；
测试先行（TDD）；0 警 0 错（TreatWarningsAsErrors）；真实邮件数据不进 git；日志红线（主题截断 40 字符、只记发件人域名、不落正文）；
中文 XML 文档注释 + 文档交叉引用；Conventional Commit（中文描述）。

## 一、用户故事与验收标准（产品经理）

**US-18-1**：作为选课学生，我希望应用在每轮同步后自动从 Canvas 通知邮件（含每周摘要 Digest 与单封
Assignment Created 邮件）里识别出作业标题、课程、截止时间和原文链接，不用我手工抄日历。

**US-18-2**：作为学生，我希望有一个「日程」页：按 **已逾期 / 今天 / 未来 7 天 / 更远** 分组展示未完成作业，
每项可 **标记完成 / 忽略 / 恢复 / 在 Canvas 打开**；同一作业被 Canvas 反复提及（摘要周报重复、Due Date Changed）
时只保留一条并自动更新截止时间。

**US-18-3**：作为学生，我希望作业临近截止（默认 24h，可设置 1–168h）时收到系统 Toast 提醒；
同一 due 只提醒一次，due 变更后可再次提醒；可在设置里整体关闭。

**验收（AC）**：
- AC1：对截图样式的 Digest 邮件（多条 `Assignment Created - <标题>, <课程>` + `due: Oct 9 at 11:59pm` +
  `Click to view <链接>`），一次性解析出全部条目，字段完整。
- AC2：首次启用时对历史 Canvas 邮件**回填**（backfill），已入库的旧通知也能生成日程。
- AC3：同链合作业重复出现不产生重复条目；`due:` 变化时原条目被更新而非新增。
- AC4：单封 Assignment Created 邮件与 Digest 邮件解析结果一致。
- AC5：无法解析的 Canvas 邮件被安全跳过（不崩溃、不再重扫）。
- AC6：日程页分组/排序正确，操作（完成/忽略/恢复/打开链接）即时生效并持久化。
- AC7：到期前 N 小时 Toast 提醒恰一次；due 更新后重新具备提醒资格；开关与提前小时数在设置页可改。
- AC8：全部测试绿 + 新增用例覆盖解析/去重/提醒/仓储；双 UI（WPF + Avalonia）均可进入日程页。

## 二、设计决策（架构师 · ADR-006 摘要）

1. **触发点**：`SyncCoordinator.SyncRoundCompleted` 事件（通知同款接线）。提取不依赖分类结果——
   按发件人（`instructure.com` / `canvas.` 域）+ `messages.ddl_scanned_at_utc IS NULL` 选取候选，批 50，
   扫过即置位（无论是否解析出条目）。天然获得 AC2 回填能力。
2. **解析器为纯函数**：`CanvasDdlExtractor`（Core 层，零 IO、零正则超时风险用 100ms timeout 同 CLASS-001）。
   输入 subject + 正文文本（IBodyCache HTML → `TextNormalizer.Normalize`，缺失回退 BodyPreview），
   输出候选条目列表。行扫描为主：标题行 `(Assignment Created|Due Date Changed…)[-–:]\s*<rest>` →
   近邻 `due:` 行 → 近邻 `Click to view <link>`；`<rest>` 按课程代码正则（复用 `\b[A-Z]{2,4}\d{4}(W\d)?\b`）
   拆「标题, 课程」。日期解析支持 `Oct 9 at 11:59pm`、`Sep 28 at 5pm`、`Oct 9`（默认 23:59）、
   `10/9`、`10月9日`、带年份变体；**年份推断**：无年份取当前年，结果早于 now−45 天则 +1 年。
3. **时间约定**：Canvas 邮件 due 视为本机时区墙上时间（港校学生 Canvas 账户时区=本机时区），
   转 Unix 秒落库（D-23：时间列一律 long），展示 `ToLocaleTime`。时区歧义在 ADR-006 记录并列为已知限制。
4. **去重键**：链接含 `/courses/{cid}/(assignments|quizzes)/{aid}` → `canvas-a:{cid}:{aid}`；
   否则 `canvas-t:{sha1(title|course|due) 前 16}`。upsert 语义：同键存在且 due 不同 → 更新
   due/message/updated_at 并清提醒标记；due 相同 → no-op。
5. **存储**：新表 `schedule_items`（id/account_id/message_id/source/dedupe_key 唯一/title/course/course_code/
   link/due_at_utc/status/reminded_due_at/created_at_utc/updated_at_utc）+ `messages` 加列
   `ddl_scanned_at_utc INTEGER NULL`；EF 迁移 `AddScheduleModule`。抽象 `IScheduleStore` 进 Core/Abstractions。
6. **提醒**：`ScheduleService.CheckRemindersAsync`：open 且 due ∈ [now, now+N h] 且 `reminded_due_at ≠ due`
   → `IToastSender.SendAsync`（无 launch 深链，v1 纯文本）→ 写 reminded 标记。设置键：
   `schedule.enabled`（默认 true）、`schedule.reminder_enabled`（默认 true）、`schedule.reminder_hours`（默认 24，钳 1–168）。
7. **UI**：顶部导航第 4 tab「日程」（收件箱/日程/规则/设置）。共享 `ScheduleViewModel`（MailHelper.ViewModels，
   订阅 SyncRoundCompleted 自动刷新）+ Avalonia `SchedulePage` + WPF `SchedulePage`；
   分组徽章配色沿用 DesignTokens；操作按钮行内直排。设置页新增「日程与提醒」区块（双 UI）。
8. **日志/错误码**：`schedule.extracted scanned=N items=M updated=K`；解析异常 SCHED-001（单封失败不影响批次）、
   存储异常 SCHED-002；不落主题正文（红线）。

## 三、任务分解

- **S18-A Core**：`Domain/ScheduleItem.cs`（record + `ScheduleItemStatus { Open, Done, Ignored }`）、
  `Abstractions/IScheduleStore`、`Schedule/CanvasDdlExtractor`（含 `DdlCandidate`、`DdlParseResult`）。
- **S18-B Infrastructure**：`ScheduleItemEntity` + `MessageEntity.ddl_scanned_at_utc` + 迁移
  `AddScheduleModule` + `ScheduleRepository`（GetCandidates/MarkScanned/UpsertByDedupeKey/GetAll/SetStatus/
  DeleteOlderThan）。
- **S18-C Core.Services**：`Schedule/ScheduleService`（ExtractPendingAsync/CheckRemindersAsync/SetStatusAsync/
  GetUpcomingAsync/CleanupAsync，`TimeProvider` 注入以便测试）+ `SettingsService` 三键。
- **S18-D 接线**：双 App 在 `SyncRoundCompleted` 处提取+提醒；双 Bootstrapper DI 注册；
  `MailDatabase.EnsureReady` 无需改动（迁移自动应用）。
- **S18-E UI**：`ScheduleViewModel`（分组视图模型、命令、自动刷新）+ MainViewModel 导航扩展
  （IsScheduleView/ShowScheduleCommand，视图索引重排 0收件箱/1日程/2规则/3设置）+ Avalonia 页/导航 +
  WPF 页/导航 + 双设置页区块。
- **S18-F 测试**：Core 解析单测（≥15 用例：Digest 多条/单封/日期格式族/年份滚转/无链接/quiz 变体/换行污染）、
  Services 单测（回填/去重/更新/提醒一次/开关关闭/清理）、Integration 仓储（迁移建表/唯一键/扫描置位）、
  双 UI 冒烟（日程页可导航）。
- **S18-G 门禁与登记**：全量 `dotnet test` 绿、0 警 0 错、docs/03 ADR-006 + docs/04 新签名小节 +
  CHANGELOG（Unreleased）+ PROGRESS.md S18 条目、Conventional Commit。

## 四、范围外（列入优化建议，不在本项实现）

非 Canvas 课程邮件的 DDL 抽取（教授邮件正文自然语言截止时间）；日程 .ics 导出/日历订阅；
多阈值提醒链（7 天/1 天/1 小时）；Canvas API 直连（绕邮件）；时区显式配置项；待办同步到系统日历。
