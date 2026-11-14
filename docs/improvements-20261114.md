# MailHelper 0.10.0 优化与验收记录

日期：2026-11-14。目标：让大邮箱能完整浏览与准确搜索，让手动/周期同步走完整且可恢复的处理流程。

## 动手前的现状与备份

- 起点：`main` 与远端一致，提交 `12ec6901d15db7b5e335e743c2d28ba8818f7faa`，开发版本 0.9.0。
- 现有产品：本地经典版 Outlook 直读，规则分类、P0–P3、通知、搜索、分类改判、Canvas 模板 DDL；WPF 与 Avalonia 共用业务层。不是 Web 应用。
- 备份标签：`mailhelper-v0.9.0-backup-20261114`，已推送 GitHub。标签不匹配正式发版触发器。
- 离线完整历史：`F:/AICoding/_backups/mail_helper-before-improvements-20261114.bundle`，`git bundle verify` 通过。
- 原有未跟踪 `aktools_log.log` 保留，不纳入提交；用户实际邮件数据、登录状态及其他项目不在本次修改范围。
- 实施分支：`codex/mailhelper-improvements-20261114`。

## 产品与工程方案

按用户完成任务的顺序优化，保持现有桌面技术栈和本地数据方式。

| 优先级 | 问题 | 实施与验收目标 |
| --- | --- | --- |
| P0 | 首轮超过 100 封与同秒边界可能漏同步 | 来源按时间升序分页；分页游标与持久水位独立；同时间戳以 EntryID 排除已处理项 |
| P0 | 周期同步不分类，通知收到未分类 P2 对象 | 共用可等待的完成管线：分类全部 → 重读分类结果 → 通知 → 日程 → 保存断点 → 刷新 |
| P0 | 断点读取/仓储异常后永久 Syncing | 全轮捕获和释放同步门；保留失败前断点，允许下一次重试 |
| P1 | 收件箱/搜索只显示 200 封且无提示 | 每次扩展 200 封，额外查询一条判断是否有更多，摘要明确「已显示」 |
| P1 | 搜索范围、排序与快速操作不一致 | SQL 先过滤再排序/分页；请求取消+版本校验；保留分类/未读/待确认/排序交集 |
| P1 | 徽章不可信、低重要度错误入库 | 未读按全库各类别统计；标读失败回滚；P3=0 不让 EF 省略写入 |
| P1 | 小窗控件挤压、导航与键盘不一致 | 两行工具栏；结果/空态/加载状态；双端快捷键、导航、阅读竞态修复 |
| P1 | 检查更新 URL 错误、下载不验证 | GitHub API 查版；优先资产 digest、兼容 SHA256SUMS.txt；完整校验后才允许安装 |

页面保留既有视觉语言。没有增加外部 AI 服务或把用户邮件发往云端。分页采用逐步扩展前缀查询：解决目前不可达的历史列表，同时保持刷新与新邮件排序一致；超大邮箱的游标分页可以后续单独评估。

## 关键实现决策（CHG-017）

- `InboxQuery` 追加可选 `Offset`，`SearchQuery` 追加可选 `Scope`，现有调用保持兼容，不改数据库结构。
- `SyncCoordinator.ProcessRoundAsync` 是可等待的业务收尾入口；分类失败不推进成功断点。通知与日程分别隔离失败，避免辅助功能故障阻断邮件可用性。
- `SyncCompletionService` 分批处理全部未分类和 Canvas 候选；通知使用从仓储重新读取的邮件，人工分类保持优先。
- 原 `SyncRoundCompleted` 保留，触发时分类和日程已完成；用来刷新收件箱和日程页。
- `IMailProvider.CanResumeFromCheckpoint` 允许来源判断断点有效性；Outlook 使用 `inbox-v2`。旧断点触发一次全量回填，按首次历史轮静默处理，避免把旧版本遗漏的邮件当作新邮件弹窗；upsert 保留人工分类。
- `PendingUpdate` 增加 SHA256；平台安装策略保持不变。仅已验证的本进程下载包可安装，安装前再验一次。
- P3 映射修复不追溯修改现有人工分类记录；不猜测哪些历史记录受过默认值影响。

## 验证

测试使用独立临时 SQLite 与匿名种子，不连接或修改用户实际 Outlook 邮箱。

| 检查 | 结果 |
| --- | --- |
| Release 完整构建 | 0 错误，3 个既有 Avalonia AVLN3001 提示 |
| Core / Services / Integration（不含 UI） | 106 / 109 / 82 项通过 |
| Mac 契约 / 共享 ViewModels / Avalonia headless | 37 / 13 / 14 项通过 |
| Windows WPF 真窗口冒烟 | 1 项通过；正文必须由 WebView2 UIA Document 暴露所选邮件实际文本，再截图 |
| 合计 | 362 项通过，0 失败 |
| Core 行覆盖（仓库 union 门禁） | 94.24%（687/729），要求 ≥80% |
| 万封邮件搜索性能 | 现有 20 词 P95 <500ms 门禁通过，不外推到所有电脑 |
| 平台边界 / diff whitespace | 通过，Core / Core.Services 无平台依赖违规 |

命令：`dotnet build MailHelper.sln -c Release`；`dotnet test MailHelper.sln -c Release --filter "FullyQualifiedName!~UiSmokeTests" --collect:"XPlat Code Coverage" --settings coverlet.runsettings`；UI 冒烟独占运行 `dotnet test tests/MailHelper.Integration.Tests -c Release --filter FullyQualifiedName~UiSmokeTests`；以及 `tools/check-platform-boundary.ps1`、`tools/coverage-gate.ps1`。

本机结果：各测试工程 `TestResults/final-improvements.trx`、UI 测试 TRX；匿名真实界面截图 `artifacts/screens/s6-inbox.png` 与 Avalonia `artifacts/screens/avalonia/inbox-min-width.png`。直接电脑控制工具在本次 Windows 窗口出现应用归属识别错误，未据此宣布手动检查通过；正文阅读以仓库 FlaUI 回归的实际 UIA 文本断言和最终截图为证据。

新增用例覆盖：405/421 封继续加载、选中保留、搜索筛选组合、慢请求覆盖、已读失败恢复、自定义未读、SQL 稀疏过滤、P0–P3 往返、237 封历史、205 封同秒、24 小时回看、异常同步重试、分类超过 200 封、日程超过 50 封、首次静默、更新哈希/大小/取消/安装复验和双端交互。

## 已知边界

- 本机验证 Windows；Avalonia 在 Windows 上编译与 headless 交互测试，macOS 原生安装/系统通知/Outlook AppleScript 需 macOS 环境验证，交给远端 macOS CI 覆盖构建测试。
- Outlook 分页通过合成来源测试与 COM 来源代码核查；本轮没有读取实际学校邮箱，不宣称已经完成真实 Outlook 端到端验收。
- 远端删除/移动感知仍是原有路线图项。升级旧 Outlook 断点的第一次同步会全量回填，大邮箱耗时会比平常增量更长。
- Canvas 仍只识别模板通知，时区沿用本机解释；极旧的无年份通知与非法日期容错仍有改善空间。
- 通知发送失败不会阻断本轮同步；本轮没有新增持久化的通知重试队列。
- 既有三个 Avalonia AVLN3001 构造器提示保留，未通过禁用告警隐藏。

## 发布与回退

本轮提交推送 `main`，现有 Actions 会创建 beta 预发布，不打正式版本标签，不改变旧安装用户的稳定更新源。

首次推送后，远端报告原 `macos-14` 镜像不可用。依据 [GitHub 官方镜像清单](https://github.com/actions/runner-images)，改为 ARM `macos-15` / Intel `macos-15-intel`，分别在原生架构验证和打包；Release 同时等待 Windows 和 Mac 测试通过。首次失败运行保留记录，后续通过新的提交运行验证。

查看旧版：`git show mailhelper-v0.9.0-backup-20261114:README.md`。需要回退时，对本轮提交使用 `git revert <本轮提交>` 再推送，保留共享历史。离线恢复可用 bundle 克隆到新目录，不覆盖当前工作区。
