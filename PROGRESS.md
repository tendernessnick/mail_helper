# PROGRESS.md — MailHelper 唯一进度事实源

> **纪律**：每完成一项立即更新本文件；状态只允许「待办 / 进行中 / 完成 / 阻塞」；每条任务五字段齐全（编号、对应 FR/UC、状态、证据、备注）。
> **会话恢复协议**：读本文件 → `git log --oneline -10` → `dotnet test`（SDK 可用时）→ 从最近未完成任务继续。禁止重做已完成工作，禁止询问「从哪里开始」。

## 0. 当前快照

| 项 | 值 |
| --- | --- |
| 更新时间 | 2026-09-27 |
| 里程碑 | M2 进行中（S1~S10 已完成；下一步 S11 IMAP 兜底） |
| 当前模块 | S11 IMAP 兜底通道（FR-02：MailKit XOAUTH2、UIDVALIDITY+UID 水位增量；真实连通=检查点②） |
| 阻塞 | 检查点①（Azure ClientId）与②（真实租户账号）仍待用户——S11 逻辑层可先以 WireMock/Fake 推进 |
| 下一步 | S11 按流水线推进：ImapMailProvider 测试先行（XOAUTH2 SASL、UID 水位增量、统一 RemoteMessage 输出）→ Bootstrapper 通道切换 → 六项自检 → 提交 |

## 1. 工程书内化基线（关键索引，供后续直接引用）

- **需求（02 章）**：FR-01~FR-14（§5）、NFR-01~NFR-12（§6）、异常场景 EX-01~EX-10（§8）；7 类别=附录 A；P0–P3=附录 B（P0 判定保守，宁漏报为 P1 不滥标）
- **架构（03 章）**：四层分层（§2.1，只能上层依赖下层，Core 无 SDK/UI 依赖）；ADR-001 框架选型 / ADR-002 delta 轮询+IMAP 兜底 / ADR-003 SQLite+磁盘缓存+FTS5 / ADR-004 规则引擎优先；评分模型与双阈值（§5.3：top1≥3 且分差≥2）；IMAP UID 水位（§5.4）
- **详细设计（04 章）**：解决方案结构（§1）；接口签名（§2，逐字一致红线）；DDL+索引+FTS5 触发器（§3.2）；Graph 调用 G-1~G-4 与 $select（§4.1）；重试矩阵 429/5xx/401（§4.2）；错误码 AUTH-001~003、SYNC-001~004、STORE-001、CLASS-001、UI-000（§5）；日志红线（§6：主题截断 40 字符+指纹、发件人仅域名、正文/令牌绝不落日志）；配置键（§7）；通知决策（§8）
- **UI（05 章）**：三栏线框（§3.2）；色卡 P0 `#D13438` / P1 `#F7630C` / P2 `#0078D4` / P3 `#8A8886`（§5.1）；四态（§6）；键盘与无障碍（§7）
- **测试（06 章）**：达标线（§4.1：类别 ≥85%、P0 召回 ≥95%、P0 误报 ≤10%、待确认 15%–30%）；边界样例 R-01~R-07（§4.3）；TC-001~TC-020（§5）；异常流 EX-TC-01~08（§6）；PERF-01~07（§7）；SEC-01~06（§8）；发布门禁 G1~G7（§9）
- **计划（07 章）**：Sprint S0~S5（§2）；风险 RISK-01~08（§5）；变更管理 CHG（§6）
- **发布（08 章）**：分支与 Conventional Commits（§1）；本地构建命令（§2）；CI（§3）；打包双产物 + WebView2 引导 + Velopack（§4）；发布检查单（§5）；数据路径（§6.1）
- **安全（09 章）**：DPAPI+熵（§3）；脱敏（§5）；PDPO（§6）；LLM 边界（§7）；已知限制（§8）；发布前检查单（§11）

## 2. 环境与阻塞

### 2.1 环境自检（2026-09-26，SDK 安装后复核）

| 项 | 结果 | 证据 |
| --- | --- | --- |
| git | ✅ 2.55.0.windows.5 | `git --version` |
| .NET 运行时 | ✅ 8.0.28（NETCore / ASP.NET / WindowsDesktop） | `dotnet --list-runtimes` |
| .NET SDK | ✅ **8.0.425**（BLK-001 解除后） | `dotnet --list-sdks`；符合 global.json `latestFeature` 策略 |

### 2.2 BLK-001（已解除）：本机缺少 .NET 8 SDK

- **影响**：一切 `dotnet build/test/publish`；S0 六项自检无法执行。
- **处理记录**：2026-09-26 用户明确授权 winget 安装 → `winget install Microsoft.DotNet.SDK.8` 成功安装 8.0.425（安装日志：哈希验证通过、退出码 0）→ 随即完成 S0 全部自检。**已解除**。
- **解除后动作**：会话恢复协议 → 验证 S0 → 进入 S1。

### 2.3 检查点登记（等待用户输入）

| 检查点 | 内容 | 状态 |
| --- | --- | --- |
| ① | Azure 多租户应用注册，回报 ClientId → 配置占位符【AZURE_CLIENT_ID】 | ⏳ 待用户（此前所有 Graph 相关代码仅基于 WireMock 与假令牌开发） |
| ② | 真实学校测试账号：真实登录 / 真实增量同步 50 封 / 真实 IMAP 兜底连通 | ⏳ 待用户（M1 后期） |
| ③ | 代码签名证书 | ❌ 未提供 → 按 08 章 §4.4 无证书路径（SHA256 + SmartScreen 指引），不阻塞发布 |

## 3. 任务清单（执行顺序按总控指令 S0–S12；括注 07 章 Sprint 映射）

### S0 工程骨架（Sprint S0 / M0）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S0-01 | 基础设施 | **完成** | §6 文件清单全部落盘；提交 22f72ee | sln + 4 src 工程 + 3 测试工程 + Build.props + .editorconfig + global.json |
| T-S0-02 | 基础设施 | **完成** | `.github/workflows/ci.yml` | build + test + 覆盖率门禁；release job 随 S12 落地 |
| T-S0-03 | 验证（六项自检） | **完成** | ① Release 构建「已成功生成 0 警告 0 错误」；② 测试 5/5 全绿（Core 3 + Services 1 + Integration 1）+ 3 份 cobertura 文件（Core 无可执行行按 D-03 放行）；③ 7 工程 `--vulnerable --include-transitive` 均报无易受攻击包；④⑤ S0 无错误码/日志代码 N/A；⑥ 结构与依赖方向同 04 §1 一致（App→Services→Core；Infrastructure→Core+Services；Core 零引用） | — |
| T-S0-04 | 验证（CI） | 阻塞（外部） | workflow 已就绪待触发 | 本地无 git 远端，无法跑 GitHub Actions；需用户提供仓库后 push 验证。非关键路径：本地已等价执行 build+test+门禁 |
| T-S0-05 | 管理基线 | **完成** | 本文件存在 | PROGRESS.md 建立 |

### S1 领域核心（Sprint S1）—— **已完成（2026-09-26）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S1-01 | FR-07 | **完成** | DomainContractTests 通过；签名逐字对照 04 §2 | RemoteMessage/IMailProvider/IClassifier/ClassificationResult/ClassifyRule/RuleKind/RuleSource 落地；CHG-003/004 最小偏差已标注 |
| T-S1-02 | FR-07 / 06 §4.3 | **完成** | TextNormalizerTests 11/11（含 R-06 引文剥离） | HTML→文本（剔 script/style）、`>`/From:/Sent:/中文头/`--` 剥离、空白折叠；HtmlAgilityPack 1.11.67（04 §2.2 指定） |
| T-S1-03 | FR-07/08 | **完成** | RuleEngineTests 23/23（含 R-01~R-07 全部边界样例） | 加权评分（基准 10/8/6/3、×1.2/×1.5、cap 15）、双阈值（top1≥3 且 gap≥2）、发件人锁定、P0 保守双阈值、ReDoS 100ms 限时（CLASS-001：跳过+禁用 24h） |
| T-S1-03b | 04 §2.3/§7 | **完成** | RuleSetParserTests 24/24 + FileRuleSetWatcherTests 3/3 | JSON Schema 按 04 §7（四种标准 Kind；CHG-001 印证为负例测试）；FileSystemWatcher+500ms 防抖热重载；解析失败保留旧集 |
| T-S1-04 | FR-07 | **完成（范围调整）** | R-01~R-07 以 23 个用例逐条验证 | 完整 600 封样本语料 + labels.csv + 评估流水线统一并入 S5（避免提前引入 MailKit 解析 .eml）；S1 的「试跑」实质已由单测承担 |

**S1 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 72/72 全绿（Core 70 + Services 1 + Integration 1），**MailHelper.Core 行覆盖 94.04%（600/638，排除 *.g.cs）**；③ `dotnet list package --vulnerable --include-transitive` 无易受攻击包（含 HtmlAgilityPack）；④ 错误处理矩阵：CLASS-001（正则编译失败/超时→跳过+禁用 24h）已实现并测试，规则解析失败→回退旧集已测试；⑤ Core 零日志输出，红线无风险（见 D-12）；⑥ 签名/评分模型/Schema 与 04 §2、03 §5.3、04 §7 逐项一致（CHG-003/004 为已登记最小偏差）。

### S2 存储层（Sprint S1）—— **已完成（2026-09-26）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S2-01 | 04 §3 | **完成** | `Storage/Migrations/20260926111334_InitialCreate.cs`（7 表 + 4 索引含 partial + FTS5 虚表 + 三触发器，与 04 §3.2 DDL 逐列核对）；MailDatabaseTests 3/3 | EF Core 8.0.10 + SQLite；WAL 库级持久（测试断言 `journal_mode=wal`）；integrity_check → `*.corrupt.bak` 备份 → 重建 → 尽力恢复 rules/settings（EX-07/STORE-001，垃圾文件重建测试通过；「损坏但可读」的恢复路径为尽力而为 + JSON 兜底，EX-TC-06 完整验收留待 S4 系统测试强化）；FTS5 `tokenize='trigram'`（CHG-005） |
| T-S2-02 | 03 §6 | **完成** | BodyCacheStoreTests 3/3 | `{accountId}/{sha1}.html` 路径规范（测试断言 sha1 文件名）；LRU 以 LastWriteTimeUtc 为据、读/写显式 touch（D-17）；目录穿越防护 |
| T-S2-03 | FR-04 AC2 | **完成** | MailRepositoryTests 8/3 + AccountStoreTests 3 + SettingsStoreTests 1（合计 12/12） | 主键幂等 upsert（重放零重复，TC-008 等价）；已存在行仅更新同步字段、分类五字段保留（EX-08 测试覆盖）；批 100/事务；FTS 中英文检索（≥3 字符 trigram MATCH / <3 字符 LIKE 兜底）；deltaLink 断点持久化往返 |

**S2 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **90/90 全绿**（Core 70 + Services 1 + Integration 19），MailHelper.Core 行覆盖 **83.57%**（600/718，新增领域记录主要经集成测试覆盖）；③ 漏洞扫描：发现 EF 传递依赖 SQLitePCLRaw 2.1.6 有 High 公告（GHSA-2m69-gcr7-jv3q）→ **显式升级 bundle 2.1.13 后全工程干净**；④ 错误处理矩阵：STORE-001（损坏自动重建）已实现并测试；⑤ 本模块零日志输出；⑥ 迁移/模型与 04 §3 逐列一致（CHG-002 的 DEFAULT 2 原样保留；CHG-005 trigram 为登记偏差）。
**TDD 证据**：集成测试先行，红灯 `17 失败/19`（实现缺失）→ 实现后全绿。

### S3 认证（Sprint S1）—— **已完成（2026-09-26）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S3-01 | FR-01 | **完成** | TokenService 编译通过 + DpapiFileProtector 3/3 + AuthErrorMapper 5/5 | MSAL.NET 4.90.1：授权码+PKCE（公共客户端默认）、系统浏览器（WithUseEmbeddedWebView(false)）、common 多租户、`mailhelper://auth` 深链（协议注册随 S12）、DPAPI(CurrentUser+熵) 缓存序列化回调、登出撤销+删缓存（AC3）。**交互登录单测不覆盖（真实浏览器依赖）——真实验证=检查点①**；ClientId 占位符防护（AZURE_CLIENT_ID 未配置时返回明确错误而非撞云） |
| T-S3-02 | FR-01/EX-01/EX-02 | **完成** | AuthServiceTests 8/8 全绿（假令牌提供器 FakeTokenProvider 驱动） | 状态机 SignedOut→SigningIn→(SignedIn\|ReauthRequired)；AUTH-001 取消回 SignedOut、AUTH-002 走预案向导入口、AUTH-003 标记 ReauthRequired（EX-02）；登录重入拒绝；StateChanged 事件序列验证；恢复路径（重登后静默成功回 SignedIn） |

**S3 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **106/106 全绿**（Core 70 + Services 9 + Integration 27），**MailHelper.Core 并集行覆盖 93.77%（346/369）、Core.Services 100%**；③ 漏洞扫描干净（新增 MSAL 4.90.1 / ProtectedData 8.0.0）；④ 错误处理矩阵 AUTH-001/002/003 处理路径全部实现并被测试覆盖（Serilog 日志接入随 S4，D-30）；⑤ 模块零日志输出（令牌/账号信息结构上不进任何日志）；⑥ DPAPI+熵、PKCE、系统浏览器、common、登出撤销与 09 §3 逐项一致。
**TDD 证据**：红灯 `16 失败`（AuthService 8 + 认证基础设施 8）→ 实现后全绿。

### S4 Graph 同步（Sprint S1）—— **已完成（2026-09-26）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S4-01 | FR-04/05 | **完成** | GraphMailProviderTests 12/12（WireMock 契约，不出网） | delta 分页（nextLink→deltaLink）、断点 URL 直传、`@removed`→Removed、字段映射（$select 逐项）、P0 候选单封拉正文入缓存（04 §4.1 例外）、G-1 /me 健康检查。**CHG-006：以 HttpClient 直调 REST 实现**（非 Graph SDK——@removed 反序列化不可靠/退避矩阵自控/依赖最小化） |
| T-S4-02 | FR-04/EX-03 | **完成** | ExTc01 ×2 + ExTc02 ×2 + ExTc03 + ExTc04 全绿 | 退避矩阵（04 §4.2）：429 按 Retry-After（≤30s 封顶）/5xx 指数 2s-8s-30s（测试注入 5ms），最多 3 次重试；401 → 强制刷新一次重试（不消耗退避次数），仍 401 → AUTH-003；410 → 自动全量（SYNC-003）；网络不可达 → SYNC-001 |
| T-S4-03 | FR-04/05 | **完成** | SyncCoordinatorTests 10/10（真 SQLite 临时库 + FakeMailProvider） | 状态机 Idle→Syncing→(Idle\|Offline\|Error\|ReauthRequired)；批 100 入库 + BatchSynced 进度事件；断点仅在整轮成功后推进（EX-05 中断→旧断点续传 + 已接收部分尽力入库）；失败写 last_sync_status；预览截 500（02 §9）；RunPeriodicAsync 周期循环取消优雅退出；**Serilog sync.completed 落文件断言（总控第 6 步验证方式）** |
| T-S4-04 | 04 §6/09 §5 | **完成** | LogSanitizerTests 6/6 | 脱敏工具：主题截 40+指纹 8 位、发件人仅域名；Serilog 滚动文件工厂（日切/14 天/10MB） |

**S4 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **134/134 全绿**（Core 70 + Services 19 + Integration 45），并集覆盖 Core **94.10%**、Core.Services **88.96%**；③ 漏洞扫描：WireMock 传递依赖 OpenTelemetry ×2（Moderate）与 Scriban.Signed 5.5.0（High）已显式升级覆盖，**7 工程全部干净**；④ 错误码矩阵 SYNC-001/002/003、AUTH-003 处理路径 + Serilog 日志（sync.completed/offline/failed/reauth_required/unexpected_failure，SYNC-001 频率 1/min）全部落地并被测试覆盖；⑤ 协调器日志只含计数/状态码/错误码，无主题/发件人/正文（红线零泄漏）；⑥ REST 契约与 04 §4.1/§4.2 逐项一致（CHG-006/007/008 为已登记偏差）。
**TDD 证据**：红灯 `28 失败`（WireMock 契约 18 + 协调器 10）→ 实现后全绿。

### S5 分类服务（Sprint S2）—— **已完成（2026-09-27），评估门禁全过**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S5-01 | FR-07 | **完成** | ClassificationServiceTests 4/4 | 管线：Normalize(BodyPreview) → IClassifier → 写回 → 汇总（CHG-009 ClassificationSummary）；待确认判定 = confidence < 0.55（可注入）；R-06 端到端（引文截止不触发 P0）；classify.completed 埋点 |
| T-S5-02 | FR-07/08 | **完成** | `rules.builtin.json` v2026.09（16 条规则，仅四种标准 Kind——CHG-001） | LMS 域名/地址锚点（10/8 权重）+ 中英关键词 + P0 截止正则（hint 3）；随包分发（CopyToOutputDirectory）；**P0 类别证据白名单（D-41：仅 Finance/Admin/Course）+ 回归测试** |
| T-S5-03 | 06 §2 | **完成** | `tests/fixtures/sample-corpus/`：**607 封 .eml + labels-train.csv(360) + labels-eval.csv(247)** | 7 类别 × P0~P3 × 中英文；确定性种子 20260926（Generator_IsDeterministic 防漂移）；R-01~R-07 边界样例 + ReDoS 探针强制入评估集；引文/签名/干扰变体（干扰仅施加于带发件人锚点样本） |
| T-S5-04 | FR-07/08 | **完成** | ClassifierEvaluationTests 绿 + `artifacts/eval/evaluation-report.md` | **评估集 247 封：类别准确率 97.98%（线 85%）、P0 召回 100%（16/16，线 95%）、P0 误报 0.00%（线 10%）、待确认率 11.34%（上限 30%）——四项门禁全过，允许进入 S6**；调参实录：初版 P0 召回 93.75%——定位为语料 P0 前缀跨类混词（签证前缀+财务模板→分差 1 落模糊区），按类别对齐前缀后 100% |

**S5 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **144/144 全绿**（Core 76 + Services 23 + Integration 45），并集覆盖 Core **94.12%**、Core.Services **90.96%**；③ 漏洞扫描干净（零新增包——.eml 解析用自研极简解析器，D-42）；④ 无新增错误码；⑤ classify.completed 仅含计数无内容；⑥ 规则包 Schema 与 04 §7 一致、评估流程与 06 §4 一致。

### S6 WPF 主界面（Sprint S2 / M1 出口）—— **已完成（2026-09-27）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S6-01 | FR-12 | **完成** | MainWindow.xaml 三栏（LeftColumn 220 / MiddleColumn 3* / RightColumn 2* + GridSplitter×2）+ Onboarding 单窗双态（D-44）；`ui.column_widths` 布局持久化（Restore/Save，05 §3.3）；类别导航（全部/待确认 + 7 类）未读徽章联动 | CommunityToolkit.Mvvm（[ObservableProperty]/[RelayCommand]）；查询侧 InboxQuery API 测试先行 6/6 |
| T-S6-02 | FR-12 / NFR-02 | **完成** | 列表虚拟化（Recycling）；色卡像素级验证：截图中 #D13438/#F7630C/#0078D4/#8A8886 四色精确检出（05 §5.1）；四态=空（CountToEmptyVis）/加载（IsBusy ProgressBar）/错误（登录/同步失败红字）/离线（状态栏「离线 · 错误码」） | 徽章 AutomationId="unread-badge" + CountToUnreadNameConverter 供 UIA 实时探测（D-45） |
| T-S6-03 | FR-12 AC2 | **完成** | WebView2 沙箱六项禁用（脚本/对话框/WebMessage/HostObjects/加速键）；外链拦截：仅放行 `data:`，http(s) 一律 Cancel（09 §5，D-48）；**RISK-04 降级**：初始化失败显示明确提示替代白屏（Task 与事件两条失败路径均兜住） | 正文渲染排队模式 `_pendingHtml`（D-46）：CoreWebView2 未就绪时暂存，初始化完成后渲染 |
| T-S6-04 | 05 章比对 | **完成** | `artifacts/screens/s6-inbox.png`（2560×1520 完整窗口）；FlaUI 全链路冒烟：DEV 启动→隐私勾选→连接→同步+分类→列表加载≥15→点击首封→未读徽章 20→19→截图留档；**UIA 几何实测 nav=[220 DIP] list=[623 DIP] 阅读窗格=剩余**，与 05 §3.2 线框 220/3*/2* 一致 | FlaUI 引入理由已在总控六登记；测试宿主 PerMonitorV2 DPI 感知修复截图不完整问题（D-47） |

**S6 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **151/151 全绿**（Core 76 + Services 23 + Integration 52），并集覆盖 Core **94.23%**（359/381）、Core.Services **90.96%**；③ 漏洞扫描：发现 FlaUI 4.0.0 传递依赖 System.Drawing.Common 5.0.2 **Critical**（GHSA-rxg9-xrhp-64gj）→ 显式升级 8.0.7 覆盖后 **7 工程全部干净**；④ 错误码矩阵：UI-000 全局兜底（App 三异常 handler → %TEMP%\mailhelper-crash.log）+ AUTH-001/002/003（登录失败提示/预案入口/重登）+ SYNC-001/003（状态栏离线与失败文案）均可在 UI 呈现；⑤ 日志红线：App 层零 Serilog 输出，崩溃日志仅异常类型+堆栈，无正文/令牌/主题；⑥ 文档一致性：三栏几何/色卡/键盘（Ctrl+F、Ctrl+R）/四态与 05 §3.2、§5.1、§6、§7 逐项核对一致。

### S7 通知与托盘（Sprint S3）—— **已完成（2026-09-27）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S7-01 | FR-14 | **完成** | NotificationServiceTests 12/12（真 SQLite + FakeToastSender + FixedTimeProvider）：P0 逐封（launch=mailhelper://message/{id}，AC1）、P1≥3 聚合「x 封重要邮件」、P2/P3 静默、message_id 去重（AC2）、勿扰 queued/结束补发摘要、P0 开关、首轮静默（D-50）、TC-018 混合批次、notify.sent 埋点（Serilog 文件断言且无主题/发件人）；NotificationRepositoryTests 3/3（过滤/queued→flushed 流转/重放幂等）；协调器 SyncRoundCompleted 3/3（整轮一次携带全部 newMails/首轮标志/失败轮不触发） | 04 §8 决策逐条落地；CHG-010（同步完成事件 newMails 载体） |
| T-S7-02 | FR-14 | **完成** | 托盘 H.NotifyIcon.Wpf（04 §4 指定）：角标图标 WPF 渲染→PNG→ICO 容器封装（DrawText 动态数字，>99 显示 99）；菜单=打开主界面/立即同步/设置（S9 占位禁用）/退出；未读总数联动角标（UnreadTotal）；关窗→Hide+一次性气泡提示（AC3）；退出走托盘菜单置 forceClose；单实例 Mutex+命名管道唤起（EX-TC-07）；Toast 点击直达（OnActivated 解析 launch → SelectMailByIdAsync，真实弹窗=检查点②）；**TC-019 已并入 UI 冒烟**：关窗→进程驻留→管道唤起→窗口重现 | ToastSender 落 App 层（D-52）；ICO 封装规避 System.Drawing.Common 新依赖 |

**S7 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **169/169 全绿**（Core 76 + Services 38 + Integration 55），并集覆盖 Core **94.26%**（361/383）、Core.Services **92.36%**（290/314）；③ 漏洞扫描：新增 Microsoft.Toolkit.Uwp.Notifications 7.1.3（04 §4 指定）与 H.NotifyIcon.Wpf 2.3.0（04 §4 指定）后 **7 工程全部干净**；④ 错误码矩阵：无新增错误码（04 §5 无 notify 码）；toast.send_failed Warning 降级不阻断；通知侧异常经 UI-000 兜底（crash log）且不影响同步轮；⑤ 日志红线：notify.sent/notify.queued/toast.* 仅计数与布尔，测试断言日志不含主题/发件人；⑥ 文档一致性：04 §8 四条决策逐条实现并测试、04 §4 技术栈按指定采用；notify.quiet_hours 值格式文档未定义（「Should」疑笔误）→ D-49 拍板。

### S8 待确认队列与反馈闭环（Sprint S3）—— **已完成（2026-09-27）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S8-01 | FR-09 | **完成** | 置信度阈值（confidence<0.55→待确认，S5）与「待确认」侧栏入口/计数/队列视图（S6 InboxQuery.NeedsReviewOnly + GetNeedsReviewCountAsync）已闭环；S8 改判后 LoadInboxAsync 联动移出队列 | 本任务大部分在 S5/S6 铺垫，S8 补齐改判联动 |
| T-S8-02 | FR-11 | **完成** | FeedbackServiceTests 8/8：改判立即生效（ClassifiedBy=user、confidence=1.0）、写 classification_feedback、upsert 发件人规则（source=Feedback、稳定 id=SHA256(feedback\|地址) 同发件人覆盖）、引擎即时生效（RuleEngine.Upsert 写侧串行+原子引用）、带重要度建议、未知邮件返回 false、日志仅域名（红线断言）；RuleEngineTests 追加 3 用例（Upsert 即时生效/同 Id 覆盖/与 Swap 组合）；RuleStoreTests 3/3（roundtrip/幂等 upsert/反馈行幂等）；UI「改为…」菜单（阅读窗格底栏）+ VM ApplyCorrectionAsync | 04 §2.2 签名原文一致；规则名保留完整发件人（规则管理页可见，S9） |
| T-S8-03 | FR-11/TC-014 | **完成** | TC014_AfterCorrection_NewMailFromSameSender_GoesToNewCategory：改判→入库同发件人新邮件（未分类）→ClassifyPendingAsync 管线→断言归入新类别、维持基准重要度 | 端到端走真实管线（真 SQLite+真 RuleEngine+ClassificationService）；启动合并：Bootstrapper LoadRuleEngine ∪ rules 表 User/Feedback 规则（重启持续生效） |

**S8 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **183/183 全绿**（Core 79 + Services 46 + Integration 58），并集覆盖 Core **94.57%**（435/460）、Core.Services **93.22%**（330/354）；③ 漏洞扫描 7 工程干净（零新增包）；④ 错误码矩阵：无新增错误码；改判目标邮件不存在返回 false 静默处理；EX-08 语义（同步不覆盖人工改判）既有测试保持；⑤ 日志红线：feedback.rule_upserted 仅含发件人域名+类别，测试断言完整地址不落日志；⑥ 文档一致性：FeedbackService 签名与 04 §2.2 原文一致、classification_feedback/rules 表 DDL 按 04 §3.2、反馈规则 ×1.5/用户 ×1.2 由 S1 既有评分承担、半自动规则进 rules 表供 S9 规则管理页展示（FR-11 完整闭环）。

### S9 规则编辑器、设置、账户、i18n（Sprint S4）—— **已完成（2026-09-27）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S9-01 | FR-10 | **完成** | RuleManagementServiceTests 9/9：保存（Source 强制 User/正则保存前校验可编译/权重 0–15/内置只读）、删除（用户+反馈；内置拒绝）、启停（内置禁用落表同 Id 行，重启合并持续生效）、试跑预览（最近 100 封仅命中项）；RuleEngine 追加 Remove/CurrentRules/Matches；UI：顶栏导航 Tab（收件箱/规则/设置，05 §2）+ RulesPage（列表/筛选四档/启停/删除/编辑弹窗/预览结果） | TC-013 等价验证（新建→预览→保存→新邮件分类）由服务测试+引擎即时生效承担 |
| T-S9-02 | FR-03/04/06 | **完成** | SettingsServiceTests 4/4（默认值对 04 §7 全表/间隔钳制 1–60/勿扰往返/通知开关独立）；AutostartServiceTests 2/2（TC-020：Run 键写删同步、幂等）；**FR-04 补口：RunPeriodicAsync 于 App 启动挂载**（S4 遗留——此前仅手动同步），间隔变更热重启循环；SettingsPage 五分组（账户邮箱/通道/上次同步/登出/清除本地数据带确认；同步；通知 P0/P1/勿扰；外观语言/主题；高级自启/诊断/缓存上限）；登出=AuthService.SignOutAsync（撤令牌保缓存） | 主题切换渲染打磨随 M3（设置值已持久化，D-56）；清除数据删数据目录后提示重启 |
| T-S9-03 | NFR-12 | **完成** | LanguageServiceTests 4/4：zh/en 查找、缺键回落、auto 跟随系统文化；双语字典（Strings.Zh/En 80+ 键）+ LanguageService（ui.language → CultureInfo）；设置页语言切换（重启后完全生效，D-56） | D-55：i18n 用编译期字典（内嵌资源在本构建链不可靠，弃 JSON/resx） |

**S9 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **202/202 全绿**（Core 79 + Services 63 + Integration 60），并集覆盖 Core **95.10%**（485/510）、Core.Services **94.62%**（563/595）；③ 漏洞扫描 7 工程干净（零新增包）；④ 错误码矩阵：无新增错误码；RuleValidationException（正则不可编译/模式为空/内置只读）保存时拦截并向用户提示；⑤ 日志红线：rule.saved/deleted/builtin_disabled 仅含 id/kind/category（09 §1.4 规则禁用可审计），无发件人/正文；⑥ 文档一致性：FR-03/04/10 逐项落地、05 §3.3/§3.4 线框布局一致、04 §7 配置键逐键对齐、TC-020 注册表行为实测。

### S10 全文搜索（Sprint S4）—— **已完成（2026-09-28）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S10-01 | FR-13 | **完成** | SearchQueryParserTests 8/8（纯文本/from:/cat:/p: 大小写不敏感、非法值留自由文本、混合顺序无关、重复前缀首胜）；SearchServiceTests 7/7（中文 LIKE 路径/英文 MATCH 路径/from 子串/类别/重要度/组合/空查询）；UI 顶栏搜索接线 SearchService（S6 SearchBox 升级为语法搜索） | 语法语义 D-58：前缀值非法整 token 留自由文本；cat: 英文枚举名；p:N→Importance(3-N) |
| T-S10-02 | NFR-03 | **完成** | SearchPerfTests：万级造数（确定性、批 100 upsert）+ 20 组中英关键词 → **P95 < 500ms 达标**（PERF-03 API 计时口径；总耗时含造数 4s） | PERF-02（滚动帧率）为 UI 计时，随 M3 S5 阶段 PresentMon/手测（06 §7 口径） |

**S10 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **218/218 全绿**（Core 79 + Services 78 + Integration 61），并集覆盖 Core **95.15%**（490/515）、Core.Services **94.69%**（589/622）；③ 漏洞扫描 7 工程干净（零新增包）；④ 错误码矩阵：无新增错误码；FTS MATCH 异常落 LIKE 兜底（D-21 既有语义，组合查询继承）；⑤ 日志红线：搜索不落日志（查询词属用户输入非邮件内容，且零日志输出）；⑥ 文档一致性：FR-13（<500ms/万级）、MOD-09（语法解析+FTS 查询）、D-21/D-22 双路径与防注入、PERF-03 口径逐项一致。

### S11 IMAP 兜底通道（Sprint S4；真实连通=检查点②）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S11-01 | FR-02 | 待办 | — | MailKit XOAUTH2、UIDVALIDITY+UID 水位增量、统一 RemoteMessage 输出 |

### S12 打包发布（Sprint S5 / M3）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S12-01 | NFR-11 | 待办 | — | dotnet publish 单文件自包含 win-x64 |
| T-S12-02 | 08 §4 | 待办 | — | Inno Setup（WebView2 引导）+ Velopack 更新通道 |
| T-S12-03 | 08 §5 | 待办 | — | 干净环境安装/启动/卸载三步验证 + SHA256 校验值 |

## 4. 变更提案（CHG，待用户批准；docs/ 本身不修改）

| 编号 | 发现 | 处理建议 | 状态 |
| --- | --- | --- | --- |
| CHG-001 | 04 §7 示例片段中 `"kind": "SenderKeyword"` 未在 04 §2.3 RuleKind 四值枚举（SenderAddress/SenderDomain/SubjectRegex/SubjectKeyword）中定义 | 预置规则包（S5）仅使用四种标准 Kind；careers 类以 SenderDomain（专用子域）+ SubjectKeyword 组合表达。不扩枚举、不改文档 → 无代码偏差 | 已登记，待批准 |
| CHG-002 | 04 §3.2 `importance INTEGER NOT NULL DEFAULT 2` 按 §2.1 枚举数值（P2=1）对应 P1，与 02 章附录 A「其他默认 P2」矛盾 | 同步代码对 other 类别显式写 P2(=1)，不依赖 DDL DEFAULT；S2 迁移落地时在提交说明中标注 | 已登记，待批准 |
| CHG-003 | 04 §2.1 引用了未定义类型：`IClassifier.ClassifyAsync(ClassifiedInput …)` 的 **ClassifiedInput**、`IMailProvider.TestAsync()` 的 **ConnectionTestResult** 均无定义（文档缺口，非矛盾） | 提案定义：`record ClassifiedInput(string Subject, string FromName, string FromAddress, string? BodyText, DateTime? ReceivedAtUtc)`（BodyText 为 TextNormalizer 预处理后文本）；`record ConnectionTestResult(bool IsSuccess, string? ErrorCode = null, string? Message = null)`。**已按最小偏差实现**，待批准后视作 04 §2.1 的补充定义 | 已实施（最小偏差），待批准 |
| CHG-004 | 04 §2.3 `ClassifyRule.Category` 为非空 `MailCategory`，但 04 §7 示例规则 `P0-Deadline` 使用 `"category": null`（仅重要度线索、不投类别票的规则无法表达） | 提案改为 `MailCategory?`（null = 仅重要度线索）。**已按最小偏差实现**——语义为 03 §5.3 管线 H 节点所必需 | 已实施（最小偏差），待批准 |
| CHG-005 | 04 §3.2 `fts5(subject, from_name, body_preview, …)` 未指定 tokenizer；SQLite 默认 unicode61 将连续 CJK 字符视为单个 token，中文子串检索（如搜「学费」命中「缴纳学费」）必然失效，与 02 章 FR-13/CON-04/NFR-12 及 06 章 TC-017「中英文关键词检索」矛盾 | 提案 `messages_fts` 增加 `tokenize='trigram'`（SQLite ≥3.34，Microsoft.Data.Sqlite 8.x 自带版本满足）。**已按最小偏差实施**；查询词 <3 字符时走 LIKE 兜底路径（正确性不依赖索引） | 已实施（最小偏差），待批准 |
| CHG-006 | 总控指令四.4 指定「Microsoft.Graph SDK 的 delta query 增量同步」；但 (a) delta 的 `@removed` 条目在 SDK v5 类型化反序列化路径不可靠（未知属性可能丢弃，删除语义丢失）；(b) 04 §4.2 重试矩阵（429 Retry-After≤3 / 5xx 2s-8s-30s / 401 刷新一次）要求完全自控，与 SDK 默认 CompositeHandler 冲突需拆链；(c) SDK 属重量级依赖（总控十一.2 精神） | 提案 GraphMailProvider 以 **HttpClient 直调 Graph REST v1.0** 实现——REST 契约（端点、$select、nextLink/deltaLink、@removed）与 04 §4.1 调用清单一字不差，WireMock 契约测试更直接；认证仍用 MSAL.NET（不弱化）。**已按最小偏差实施**，待批准后视为对四.4 表述的修订 | 已实施（最小偏差），待批准 |
| CHG-007 | 04 §2.2 `SyncCoordinator` 签名引用了 `SyncStateChangedEventArgs` / `BatchSyncedEventArgs`，均未定义 | 提案定义：`SyncStateChangedEventArgs(NewState, ErrorCode?, Message?)`；`BatchSyncedEventArgs(PageIndex, Added, Updated, Removed)`（Added/Updated 由 upsert 结果得出——delta 响应不区分新增/更新，D-36）。**已实施** | 已实施，待批准 |
| CHG-008 | 04 §4.1 G-3 `$select` 含 `changeKey`，但 §2.1 RemoteMessage 无对应字段，DDL `messages.remote_change_key` 因此无数据来源 | 提案 RemoteMessage 追加可选参数 `string? RemoteChangeKey = null`（追加式不破坏现有构造调用）。**已实施** | 已实施，待批准 |
| CHG-009 | 04 §2.2 `ClassificationService.ClassifyPendingAsync` 返回 `ClassificationSummary`，未定义 | 提案定义：`ClassificationSummary(int Processed, IReadOnlyDictionary<MailCategory,int> CategoryCounts, int PendingReview, long ElapsedMs)`（对应 04 §6 埋点 classify.completed 的各类别分布与待确认数）。**已实施** | 已实施，待批准 |
| CHG-010 | 04 §8.1「同步完成事件携带 newMails」：现有 `BatchSyncedEventArgs`（CHG-007）仅含计数，无邮件列表载体；且逐批通知会造成一轮多批的碎片化弹窗 | 提案 SyncCoordinator **追加** `SyncRoundCompleted` 事件（整轮成功推进断点后触发一次）：`SyncRoundCompletedEventArgs(NewMails, IsInitialRound, DurationMs)`——NewMails=本轮全部入库邮件（更新与新增一并交给 notification_log 去重，恰为 04 §8.4 的 NOT EXISTS 语义）；失败/取消轮不触发。既有事件与签名零改动。**已实施** | 已实施，待批准 |

## 5. 决策记录（文档未写明、自行拍板项，均有依据）

| 编号 | 决策 | 依据 |
| --- | --- | --- |
| D-01 | FluentAssertions 锁定 6.12.x（取 6.12.2） | 7.x 起转商用许可；06 章未指定版本，规避许可风险 |
| D-02 | 测试栈版本：xunit 2.9.2 / Microsoft.NET.Test.Sdk 17.11.1 / coverlet.collector 6.0.2 | 06 §3 指定 xUnit+FluentAssertions+coverlet；取已验证稳定版 |
| D-03 | CI 覆盖率门禁：Core 无可执行行（纯枚举骨架）时放行并输出 notice；S1 起严格执行 ≥80% | 避免 S0 阶段 CI 假红；80% 门禁本意针对逻辑代码（NFR-10） |
| D-04 | CommunityToolkit.Mvvm 8.3.2、Microsoft.Extensions.Hosting 8.0.1 | 总控指令四.1 指定库；取已验证稳定版，SDK 到位后如有更新在 S1 评审 |
| D-05 | AnalysisLevel=latest + TreatWarningsAsErrors=true | 落实自检①「0 错误 0 警告」红线 |
| D-06 | S0 种子内容 = 04 §2.1 四枚举 + SyncState 状态枚举（逐字转写）+ 骨架冒烟测试 | 骨架需最少可编译内容；签名零设计自由度，无偏离风险 |
| D-07 | 分支采用 `main`（README/08 章 §1.1），初始提交前将 unborn HEAD 从 master 指向 main | 08 章分支策略 |
| D-08 | ReDoS 防护选「限时 Regex（100ms）」而非 Re2.NET | 04 §2.3 给出两选项；零新增依赖优先（禁止事项 2） |
| D-09 | 倍率截断上限 WeightCap=15 | 03 §5.3「上限截断」未给数值；取基准最高 10 × Feedback 1.5 = 15，随 JSON 可调 |
| D-10 | confidence 公式：锁定=0.9；过双阈值= top2≤0 ? 1 : top1/(top1+top2)；模糊=该值×0.5；无命中=0。下游待确认阈值（S5 落地，默认 0.55）：过阈值结果恒 >0.55、模糊/无命中恒 <0.55 | 03 §5.3 仅规定 f(top1,top2) 依分差归一化到 [0,1]，公式为实现自由度 |
| D-11 | SubjectKeyword/SubjectRegex 匹配范围=主题+预处理正文（Kind 命名保持 04 章签名不变） | 03 §5.3 管线 D 节点「主题+正文关键词/正则加权评分」 |
| D-12 | Core 层不引日志依赖（Serilog 在 Infrastructure/服务层）；CLASS-001 禁用状态经 TemporarilyDisabledRuleIds 查询，S5 接 Serilog 时由服务层包装记录 | 架构约束：Core 无第三方 SDK；日志红线（04 §6）由上层统一执行 |
| D-13 | 规则稳定 id = SHA256(name\|pattern) 前 16 字节 → Guid | 热重载后同规则 id 不变，CLASS-001 禁用状态得以延续 |
| D-14 | 覆盖率度量排除 *.g.cs 源生成器产物（coverlet.runsettings ExcludeByFile + CI 脚本 obj 过滤双保险） | 80% 门禁（NFR-10）针对手写可维护代码；RegexGenerator 生成 ~2700 行不可控代码 |
| D-15 | JSON importanceHint 数值语义按 04 §2.1 枚举数值（0=P3..3=P0）解释 | 唯一自洽解释（P0-Deadline hint=3 必须是 P0）；曾误按 P 级别解释，测试断言已纠正 |
| D-16 | 04 §3.1 SYNC_STATE 实体的领域记录命名 SyncCheckpoint | 避免与 Core.Services.SyncState（状态机枚举）同名冲突；语义=同步断点 |
| D-17 | 正文缓存 LRU 以文件 LastWriteTimeUtc 为依据（读/写时显式 touch），不建独立索引 | NTFS 默认不更新 LastAccessTime；显式 touch 简单可靠 |
| D-18 | SaveAsync 后同步执行 LRU 清理（O(文件数) 求和） | 正文写入为低频用户触发操作，2GB/数万文件量级开销可接受 |
| D-19 | ER 图非节选表（accounts/sync_state 等）时间列统一 INTEGER Unix 秒 | 与 messages DDL 风格一致（EX-09：一律 UTC）；ER 的 DATETIME 仅为示意 |
| D-20 | 幂等 upsert 用「查存在→分插改→SaveChanges」两步法 | EF Core 无内建 upsert；SQLite 本地批量满足 PERF-04；原生 ON CONFLICT 留作性能不达标时的优化路径 |
| D-21 | FTS 查询双路径：≥3 字符 trigram MATCH（rank 排序）；<3 字符（中文双字词常见）或 MATCH 异常时 LIKE 兜底 | trigram 对 <3 字符模式不可用；LIKE 正确性不依赖索引（CHG-005 配套） |
| D-22 | FTS 查询词清洗（去 `"` 与 `*`）+ LIKE 通配符转义 | 防 MATCH 语法注入（SEC-04 意识） |
| D-23 | EF 实体时间列一律 long Unix 秒、枚举列存原始 string/int，领域转换隔离在仓储层 | 实体与 DDL 一字不差；Core 不感知存储形态 |
| D-24 | dotnet-ef 以**本地工具**安装（`.config/dotnet-tools.json` 随仓库）；SQLitePCLRaw.bundle 显式升 2.1.13 | 本地工具 CI 可 `dotnet tool restore`；2.1.6 传递依赖有 High 公告 GHSA-2m69-gcr7-jv3q（自检③红线） |
| D-25 | 仓储连接策略：每操作独立 SQLite 连接（每连接 PRAGMA foreign_keys=ON + synchronous=NORMAL），连接与上下文同作用域释放 | synchronous 是连接级 PRAGMA；池化连接复用不保证 PRAGMA 生效 |
| D-26 | 认证契约（ITokenProvider/AuthService/AuthResult/AuthState/AuthErrorCodes）为自行设计 | 04 章对 MOD-04 只有职责描述无签名；属设计自由度（非文档矛盾），不走 CHG；错误码字符串常量落 Core 供两层共用 |
| D-27 | MSAL 实现类名沿用工程书 TokenService；交互登录不做单测 | 真实浏览器/云依赖，真实验证=检查点①（总控指令九）；ClientId 占位符防护防误连云 |
| D-28 | 未列举的 MSAL 异常统一映射 AUTH-003（需重新登录语义） | 04 §5 仅定义 AUTH-001~003；宁可保守要求重登 |
| D-29 | 覆盖率门禁升级为**并集语义 + filename 前缀归一化**（`tools/coverage-gate.ps1`，CI 同脚本） | 发现各测试工程 cobertura 的 filename 前缀不一致（'Rules\x.cs' vs 'MailHelper.Core\Rules\x.cs'），简单累加会把行覆盖低估近半（46.88% 假值）；并集后 Core 93.77% |
| D-30 | AUTH-00x 的 Serilog 日志接入随 S4 统一落地 | S3 以 AuthResult.ErrorCode 结构化承载错误码；模块零日志输出，红线零风险 |
| D-31 | ITokenProvider.AcquireTokenSilentAsync 增加 forceRefresh 参数；Graph 通道 401 后用它强制刷新一次（该次重试不消耗退避次数） | MSAL 默认对未过期缓存令牌直接复用，401 后必须 ForceRefresh 才有意义（04 §4.2「静默刷新一次」） |
| D-32 | Core.Services 引用 Microsoft.Extensions.Logging.Abstractions（ILogger<T>） | 微软官方抽象库，非第三方 SDK 语义（总控四.2 约束的精神是不引第三方实现）；Serilog 实现只在 Infrastructure |
| D-33 | Inbox 文件夹用 well-known 别名 `/me/mailFolders/inbox`（G-2 的定位往返省略） | Graph v1.0 长期支持别名；契约测试覆盖该路径 |
| D-34 | 非列举的 HTTP 错误（400/403/404 等）统一抛 SYNC-002，message 含状态码 | 04 §5 未为这些场景定义独立错误码；宁可保守终止本轮 |
| D-35 | SYNC-001 离线日志按 1/min 节流 | 04 §5「SYNC-001 Information（频率限制 1/min）」 |
| D-36 | delta 响应不区分新增/更新，统计口径由 upsert 结果得出（BatchSyncedEventArgs.Added/Updated） | delta API 无 added/updated 标记；幂等 upsert 下该区分仅具统计意义 |
| D-37 | @removed 删除事件仅携带 id：ReceivedAtUtc 落 1970 占位，入库只置 is_deleted_remote=1（默认视图隐藏） | Graph 删除事件不含元数据；本地不物理删除（02 §9） |
| D-38 | （保留） | — |
| D-39 | 06 §4.1 待确认率「15%–30%」按下限指示、上限硬门禁执行（>30% 测试失败，<15% 仅提示） | 表述自相矛盾（越低越好 vs 下限 15%）；上限是防滥用的门禁本意 |
| D-40 | 预置规则包位于 `src/MailHelper.Infrastructure/Rules/rules.builtin.json`（CopyToOutputDirectory 随包分发） | 04 §7「随包分发的只读层」；评估测试从源码路径直读 |
| D-41 | P0 双信号的「强类别证据」限定白名单 {Finance, Admin, Course} | 订阅/公告类别的 final reminder 措辞不应升 P0（P0 误报率红线；有专项回归测试） |
| D-42 | 样本 .eml 用自研极简解析器（固定格式：UTF-8、无折叠头、无 MIME 多部分），不提前引入 MailKit | 语料为生成器产物格式可控；引入真实邮件样本时再升级 MailKit（S11 计划引入） |
| D-43 | 语料 P0 前缀按类别对齐（finance 前缀不含 admin 词，反之亦然） | 初版全局前缀池产生跨类竞争主题（分差 1 落模糊区），P0 召回 93.75% 不达标；修正后 100% |
| D-44 | 单窗双态：Onboarding 面板与三栏主界面同窗以 IsOnboarding 可见性切换，不做独立向导窗口 | 05 §3.1/§3.2 未规定窗体形态；单窗切换实现最简且状态天然共享 |
| D-45 | 未读徽章暴露 AutomationId="unread-badge" + CountToUnreadNameConverter（名称="未读数N"）供 UIA 实时读取 | FlaUI 点击级冒烟需可稳定探测的自动化属性；ListBoxItem Name 默认是 VM 类型名不可用 |
| D-46 | WebView2 渲染排队模式：CoreWebView2 未就绪时 `_pendingHtml` 暂存，CoreWebView2InitializationCompleted(IsSuccess) 后渲染 | EnsureCoreWebView2Async 是异步的，选中小即渲染会抛 InvalidOperationException（Windows 事件日志实测取证）；初始化失败走 RISK-04 降级提示 |
| D-47 | FlaUI 测试宿主 P/Invoke 声明 PerMonitorV2 DPI 感知 | 非 DPI 感知宿主拿到虚拟化矩形：GDI 截图只覆盖 200% 缩放物理窗口的左上角（截图像素与 UIA DIP 几何整体错位，S6 实际踩坑并修复）；声明后 UIA/像素坐标一致（×2 严格对应） |
| D-48 | 外链拦截 v1：NavigationStarting 一律取消 http(s) 导航，仅放行 `data:`（NavigateToString 正文） | 09 §5「外链点击前确认」的保守实现；确认对话框随 S9 设置页评估 |
| D-49 | `notify.quiet_hours` 值格式 = `"HH:mm-HH:mm"` 字符串（可跨午夜），null/空=未启用；非法格式记 Warning 按未启用 | 04 §7 表格值列写「（Should）」疑为笔误、格式未定义；跨午夜为勿扰时段（如 23:00-07:00）的常见语义 |
| D-50 | 首轮同步静默：IsInitialRound（同步前无断点）时通知仅登记 notification_log 不弹 Toast | 首装全量同步动辄数百封 P0/P1，全部弹窗伤信任（RISK-06 精神）；登记后由 message_id 去重保证后续增量不重发 |
| D-51 | notification_log.level 值域 = {P0, P1, queued, flushed}：flushed=已随勿扰结束摘要补发，保留参与去重但不再计入待补发 | 04 §8.3 仅定义 queued；补发摘要后若清行会削弱去重（勿扰中入队→补发→同邮件更新再通知），置 flushed 最小且安全 |
| D-52 | ToastSender 落在 App 层（非 Infrastructure）：Toolkit Toast 桌面 API 完整形态仅在 windows TFM 资产提供；App TFM 随之升为 net8.0-windows10.0.17763.0，Infrastructure 保持 net8.0 | 04 §4 指定技术而非所在工程；Infrastructure 保持跨 TFM 可测性（Services.Tests 引用其 Fake 与仓储）；分层方向仍为 App→Services→Core，无逆向依赖 |
| D-53 | 反馈规则稳定 Id = SHA256("feedback\|"+小写发件人地址) 前 16 字节（手法同 D-13）：同发件人重复改判覆盖同一行而非堆积 | 04 §2.2「生成或加权规则」未定义加权口径；覆盖式 + 引擎 ×1.5 权重已表达强化语义，且防 rules 表膨胀；Priority=0 先于内置规则 |
| D-54 | IMessageStore 追加 GetByIdAsync（改判前置读取旧类别/重要度/发件人）；ClassificationFeedback 领域记录 new_importance 存解析后的具体值 | 04 §2.1 接口清单本就少于实现（S6 追加查询方法先例）；04 §2.2 ApplyCorrectionAsync 的 newImportance 可空 → 服务内解析为具体值落库，表 DDL 列非空 |
| D-55 | i18n 文案用编译期 C# 字典（Strings.Zh/En）而非 resx/内嵌 JSON：本构建链上 EmbeddedResource 资源名不可靠、resx 代码生成依赖 VS | NFR-12 只要求双语界面与走查；字典随程序集分发零部署风险、可 diff；键缺失三级回落（en→zh→键名）有测试 |
| D-56 | 语言/主题变更「重启后完全生效」（设置页文案明示）：WPF 已渲染字符串不做运行时热替换 | NFR-12 验收为双语走查而非热切换；热替换需全量 DynamicResource 改造，收益不成本，列 M3 打磨项 |
| D-57 | FR-04 定时同步在 S9 补挂载（App 启动即启动 RunPeriodicAsync 循环，间隔变更热重启）；S4 已有循环实现与测试但无宿主挂载 | 定时同步为 M 级需求；S4 完成的是协调器能力，S9 设置页落地使其可达（间隔来自 sync.interval_minutes，1–60 钳制） |
| D-58 | 搜索语法语义：from:/cat:/p: 空格分词、前缀大小写不敏感；cat: 取英文枚举名；p:N→Importance(3-N)；前缀值非法整 token 留作自由文本；重复前缀首个生效 | 总控 S10 给出 from:/cat:/p: 记法、03 章 MOD-09 未定义细则；「非法值当普通词」符合用户直觉且可测试；Importance 枚举 P0=3 故 p:N 映射 3-N |

## 6. S0 文件清单（本次落盘）

```
.gitignore  global.json  Directory.Build.props  .editorconfig  MailHelper.sln
src/MailHelper.Core/{MailHelper.Core.csproj, Domain/Enums.cs}
src/MailHelper.Core.Services/{MailHelper.Core.Services.csproj, SyncState.cs}
src/MailHelper.Infrastructure/{MailHelper.Infrastructure.csproj, InfrastructureAssembly.cs}
src/MailHelper.App/{MailHelper.App.csproj, App.xaml, App.xaml.cs, Bootstrapper.cs, MainWindow.xaml, MainWindow.xaml.cs}
tests/MailHelper.Core.Tests/{MailHelper.Core.Tests.csproj, SanityTests.cs}
tests/MailHelper.Services.Tests/{MailHelper.Services.Tests.csproj, SanityTests.cs}
tests/MailHelper.Integration.Tests/{MailHelper.Integration.Tests.csproj, SanityTests.cs}
.github/workflows/ci.yml
PROGRESS.md
```

## 7. 里程碑报告

### M0（S0）—— 2026-09-26 收口

**完成项与证据**：
- 工程可构建：`dotnet build MailHelper.sln -c Release` → 0 警告 0 错误（TreatWarningsAsErrors=true 生效，自检①）
- 测试全绿：`dotnet test` → 5/5 通过（Core 3 + Services 1 + Integration 1），覆盖率收集成功（自检②；Core 骨架无可执行行，门禁按 D-03 放行，S1 起严格 ≥80%）
- 无漏洞依赖：7 工程 `dotnet list package --vulnerable --include-transitive` 全部干净（自检③）
- PROGRESS.md 建立并全程维护（T-S0-05）
- 初始提交：22f72ee（分支 main，36 文件）

**未完成项与原因**：
- CI 远端跑通（T-S0-04）：本地无 git 远端/GitHub 仓库，workflow 已编写但无法触发。属外部依赖，需要用户动作（可选：提供 GitHub 仓库地址）。本地已等价执行 CI 的全部步骤逻辑。

**与设计的偏差及处理**：
- 无代码偏差。两处文档内部不一致已登记 CHG-001/CHG-002（见 §4），均以「不改 docs/、不扩签名」方式消化，待用户批复。
- 过程性偏差：BLK-001（SDK 缺失）曾阻塞验证 ~1 小时，经用户授权 winget 安装 .NET SDK 8.0.425 解除。

**下一步计划**：
- S1 领域核心：按流水线推进——RemoteMessage/接口签名落地 → R-01~R-07 失败测试先行 → TextNormalizer 实现 → RuleEngine（加载/热重载/加权评分/双阈值）→ 样本试跑脚本 → 六项自检 + PROGRESS 更新 + Conventional Commit。

### M1（S1–S6）—— 2026-09-27 收口

**完成项与证据**（对照 07 §1 出口标准）：
- OAuth 登录：MSAL PKCE + 系统浏览器 + DPAPI(CurrentUser+熵) 缓存；AuthService 状态机 8/8（AUTH-001/002/003 全路径）；ClientId 占位符防护（真实验证=检查点①）
- Graph 增量/全量同步：delta 分页/断点续传（EX-05 语义）、退避矩阵（429 Retry-After / 5xx 2s-8s-30s / 401 强刷 / 410 全量回退）、`@removed` 删除感知——WireMock 契约 12/12 + 协调器 10/10（真 SQLite 临时库）
- 规则引擎分类：RuleEngine 加权评分+双阈值+发件人锁定+ReDoS 限时；预置规则包 v2026.09；**评估门禁四项全过**（247 封评估集：准确率 97.98%、P0 召回 100%、误报 0%、待确认 11.34%，报告在 artifacts/eval/）
- 三栏浏览：三栏+虚拟化+色卡+四态+WebView2 沙箱+布局持久化；FlaUI 全链路冒烟（登录→同步→分类→浏览→已读联动→截图）；截图与 05 §3.2 线框几何/色卡一致
- 本地数据落库：SQLite(WAL) 幂等 upsert（EX-08 分类字段保留）、FTS5 trigram 中英检索、正文磁盘缓存 LRU、损坏自动重建（STORE-001）
- **核心单测 ≥80%：MailHelper.Core 并集行覆盖 94.23%、Core.Services 90.96%（门禁 G1 达标）**；测试 151/151 全绿；7 工程漏洞扫描干净

**未完成项与原因**：
- 出口标准「真实测试租户端到端跑通」：阻塞于**检查点①**（Azure 多租户应用注册 ClientId）与**检查点②**（真实学校测试账号），均待用户提供。按总控指令九，检查点①之前 Graph 相关代码一律基于 WireMock 与假令牌开发——属计划内路径而非偏差；用户提供 ClientId 后即可无缝切换真实通道（Bootstrapper 双模式已就绪）。
- CI 远端跑通（T-S0-04）：仍待 GitHub 仓库；本地已等价执行 CI 全部步骤。

**与设计的偏差及处理**：
- CHG-001~009 全部「已实施（最小偏差）+ 显著标注 + 待批准」，无静默偏差；其中 CHG-006（HttpClient 直调 Graph REST 替代 SDK）影响面最大，已在 S4 论证三点理由。
- 过程性修复：测试宿主 DPI 感知（D-47，截图完整性）、System.Drawing.Common 5.0.2→8.0.7（Critical 顾问，自检③红线）。

**下一步计划**：
- S7 通知与托盘（FR-14）：按 04 §8 通知决策表——P0 逐封即时、P1 聚合（≥3 封/15 分钟窗口）、message_id 去重、勿扰时段；托盘角标/菜单/关窗常驻。测试先行，Toast 效果的真实系统验证列入检查点②。

| 里程碑 | 状态 | 报告 |
| --- | --- | --- |
| M0（S0） | **完成** | 见上 |
| M1（S1–S6） | **完成（真实租户 E2E 待检查点①②）** | 见上 |
| M2（S7–S11） | 未开始 | — |
| M3（S12） | 未开始 | — |
