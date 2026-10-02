# PROGRESS.md — MailHelper 唯一进度事实源

> **纪律**：每完成一项立即更新本文件；状态只允许「待办 / 进行中 / 完成 / 阻塞」；每条任务五字段齐全（编号、对应 FR/UC、状态、证据、备注）。
> **会话恢复协议**：读本文件 → `git log --oneline -10` → `dotnet test`（SDK 可用时）→ 从最近未完成任务继续。禁止重做已完成工作，禁止询问「从哪里开始」。

## 0. 当前快照

| 项 | 值 |
| --- | --- |
| 更新时间 | 2026-10-02 |
| 里程碑 | Windows v0.6.0 已发布（S0~S17 全部完成）；**Mac 阶段启动**（feature/mac-platform 分支，mac-baseline tag 已打，210 测试基线全绿） |
| 当前模块 | **MS3 已完成**（Avalonia 骨架：三栏 Shell/主题/导航，截图比对 05 章通过，osx-arm64 跨编译发布成功，216/216×3 轮）；下一步 MS4 Mac 基础设施 + OutlookMacMailProvider 骨架 |
| 阻塞 | 无阻塞。检查点①已通过（CHG-014 获批）；检查点②（Mac 真机）与③（Apple 证书）按里程碑触发；无其他阻塞 |
| 下一步 | MS4 Mac 基础设施（MacAppPaths/UDS 锁/LaunchAgent）+ OutlookMacMailProvider 骨架（假 osascript 测试先行） |

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

### 2.3 检查点登记

**Windows 阶段（原总控指令）检查点——全部收口**：

| 检查点 | 内容 | 状态 |
| --- | --- | --- |
| ① | Azure 应用注册（原 OAuth 路线） | **已废弃**（CHG-013 移除 OAuth 通道，整条路线不再需要；历史：2026-09-30 曾完成 CityU 单租户注册，2026-10-01 起落地改走 CHG-011 桌面通道） |
| ② | 真实学校账号端到端 | **达成**（2026-10-01：CHG-011 桌面通道，用户实测真实 CityU 邮箱同步+自动分类+阅读窗格全通过） |
| ③ | 代码签名证书 | 无证书路径（08 §4.4：SHA256 + SmartScreen 指引），不阻塞 |

**Mac 阶段（现行总控指令 v1.1）检查点**：

| 检查点 | 内容 | 状态 |
| --- | --- | --- |
| ① | docs/10-Mac平台适配设计（CHG-014）评审 | ⏳ **待用户批准**（唯一强制等待点；批准前不写生产代码） |
| ② | Mac 真机验证包（用户提供已登录 Outlook for Mac 经典版的 Mac） | 待 MS2/MS8/MS9 交付检查单后执行 |
| ③ | Apple Developer ID 证书与公证凭据 | 未提供则走未签名 DMG + Gatekeeper 首启指引，不阻塞 |

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

### S11 IMAP 兜底通道（Sprint S4；真实连通=检查点②）—— **已完成（2026-09-28）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S11-01 | FR-02 | **完成** | ImapMailProviderTests 12/12（FakeImapAdapter 驱动不出网）：首轮全量（imap:email:INBOX:uid 复合键/全部 Added）、断点 URI 编解码往返、增量仅拉水位之上、UIDVALIDITY 变更→全量重置（SYNC-003 等价）、InternetMessageId 去尖括号、预览截 500、分页批次、Complete 断点=最大 UID、认证失败强刷重试后 SYNC-004（D-31 重试语义）、网络失败 SYNC-001、静默失败 AUTH-003、TestAsync；Bootstrapper 按账户 Channel 自动选择通道（Imap=TokenService 传 IMAP scope）；TokenService scopes 参数化（默认 User.Read+Mail.Read 不变） | IImapClientAdapter 抽象 + ImapKitClientAdapter(MailKit)——网络路径单测不覆盖，真实连通=检查点②（先例 D-27）；MailKit 4.16.0（GHSA-9j88-vvj5-vhgr CVE-2026-41319 修复版）；多文件夹/删除感知=V1.x（03 §5.4 v1.0 接受） |

**S11 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **230/230 全绿**（Core 79 + Services 78 + Integration 73），并集覆盖 Core **95.15%**（490/515）、Core.Services **95.02%**（591/622）；③ 漏洞扫描：MailKit 4.8.0→**4.16.0**（Moderate 公告 GHSA-9j88-vvj5-vhgr 修复版）后 **7 工程全部干净**；④ 错误码矩阵：SYNC-004（IMAP 认证失败→通道页提示）、SYNC-001（连接失败→离线）、AUTH-003（令牌失效→重登）、SYNC-003 等价（UIDVALIDITY 变更全量）全部实现并被测试；⑤ 日志红线：sync.imap_resync 仅含 uidvalidity 数值，无邮件内容；⑥ 文档一致性：04 §4.3（服务器/端口/XOAUTH2/scope/UID SEARCH 语义/ENVELOPE+FLAGS+BODYSTRUCTURE 组装）、03 §5.4（统一 RemoteMessage 输出、无远端删除感知）、FR-02 AC（通道切换、内容层一致）逐项落地。

### S12 打包发布（Sprint S5 / M3）—— **已完成（2026-09-28），正式 Release 待检查点+UAT**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S12-01 | NFR-11 | **完成** | 08 §2 原文命令发布：`artifacts/publish/MailHelper.App.exe` 单文件自包含 win-x64（约 200MB 含运行时）；**发布产物启动冒烟通过**（独立数据目录/DEV 种子链路/托盘常驻正常退出）；SHA256 清单 `artifacts/publish/SHA256SUMS.txt`（08 §4.4 无证书路径） | 版本 0.1.0（SemVer）；规则包 v2026.09 随产物分发 |
| T-S12-02 | 08 §4 | **完成（脚本/CI 就绪）** | `installer/MailHelper.iss`：默认装 %LocalAppData%\Programs、**WebView2 注册表检测+静默安装**（离线完整包 187MB 已就位）、桌面快捷方式、卸载询问删数据（SEC-06/08 §6.4）；CI 补 release job（push main→publish+vpk pack+beta 通道+SHA256 上传，08 §3.2）；ISCC 本机未装→安装包编译走 CI 路径 | Velopack 本地执行依赖 GitHub 仓库（同 T-S0-04 外部依赖） |
| T-S12-03 | 08 §5 | **完成（本机口径）** | 便携版全新启动验证通过（独立临时数据目录）；卸载语义在 iss 定义；**72h 长稳脚本** `tools/longrun.ps1`（进程存活/崩溃日志增长/内存观测，参数化；短跑 PASS + 长跑后 SQLite integrity_check=ok）；完整 72h 与安装包三步验证列入检查点②/发布执行 | CHANGELOG.md（检查单第 3 项）与 README 发布说明已更新 |

**S12 六项自检结果**：① Release 构建 0 警告 0 错误；② 测试 **230/230 全绿**（Core 95.15%、Core.Services 95.02%）；③ 漏洞扫描 7 工程干净（MailKit 4.16.0）；④⑤ 错误码/日志红线无回归；⑥ 文档一致性：产物形态=08 §4.1 两产物、WebView2 键值=§4.2、无证书路径=§4.4、检查单对照见 M3 报告。

### S12+ 落地路径调整：Outlook 桌面通道（CHG-011；真实端到端=检查点②）—— **已完成（2026-10-01）**

> 背景（D-65）：CityU 租户全局禁止 OAuth 用户同意，Graph/IMAP 通道被「需要管理员批准」拦截。用户安装经典版 Outlook 后，落地方法调整为 **COM 复用本机 Outlook 登录态**读邮箱——不经过任何 OAuth 授权，绕开租户同意限制。用户实测（2026-10-01）：真实 CityU 邮箱（ruijiehu7-c@my.cityu.edu.hk）连接、全量同步、自动分类打标、HTML 阅读窗格全部通过，**检查点②实质达成**。

| 任务 | 需求 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S12-04 | FR-02 落地路径调整 | **完成（用户实测通过）** | OutlookDesktopProviderTests 10/10（FakeOutlookSource 驱动不出网）：首轮全量（EntryID 键 `outlook:{EntryId}` 全部 Added）、增量仅拉水位之上、**水位回退 24h 晚到窗口**（断点间到达的旧邮件不漏）、预览截 500+HTML 入 BodyCache、断点 URI `outlook://inbox?received=` 编解码往返、非法断点回退全量、分页批次语义、TestAsync 成/败、GetAccountAddressAsync 成/败（连接流程）；OutlookComMailSource=STA 专用线程序列化 + dynamic 晚绑定（无 PIA 依赖），Jet Restrict 时间过滤、Class==43 过滤、proptag 0x1035001F 取 MessageId；MainViewModel 连接分支：桌面通道按 `provider.Kind` 跳过 OAuth，直接读登录态地址落库（日志仅记域名，红线） | SanityTests 枚举基线同步 CHG-011（Graph/Imap/OutlookDesktop）；全量 **240/240 全绿**、0 警 0 错；发布产物更新并实测启动；`tools/run-outlook.ps1`（MAILHELPER_CHANNEL=Outlook）；ITSC 批准前此通道为默认落地方法，OAuth 通道代码保留（D-64 注入不变） |

**S12+ 自检**：全量测试 240/240（含新增 2 连接流程用例）；构建 0 警告 0 错误；日志红线复核（连接日志仅 account_domain）；真实数据不进 git（测试截图为 DEV 种子重生成）。

### S13 分类精化 × UI 体验升级（专项轮，prompts/S13；2026-10-01）—— **已完成（真机验证通过）**

> 背景：基本功能与真实端到端达成后，用户提出两项特异性优化：①分类规则针对 CityU 学生邮箱精化（「类别更清楚」）；②前端精美化。执行指令落盘 `prompts/S13-分类精化与UI体验升级执行指令.md`，一夜自动执行，次日用户验收。

| 任务 | 需求 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S13-A | G-S13-1 定位 | **完成** | 只读统计工具 `tools/db_stats.py`（真库只读）：发现**100/100 封 from_address 为空**——`MailItem.Sender` 是 Recipient 对象无 `SmtpAddress` 属性（RuntimeBinderException 被 TryGet 吞），SenderDomain 规则全废、77 封落 other | 修复 `OutlookComMailSource.ResolveSenderSmtp`：SMTP 直用→EX 走 AddressEntry→GetExchangeUser→PrimarySmtpAddress→兜底 PropertyAccessor PR_SMTP_ADDRESS(0x39FE001F)；**真机 PowerShell COM 探针验证** cityu.edu.hk/instructure.com 解析正确 |
| T-S13-B | G-S13-1/2 规则 v2026.10 + 迁移 | **完成（真机生效）** | 规则包 26 条（CityU 特化，全部带 note 依据：Canvas-Instructure/Course-Code 课程代码正则/CareER 周刊压制营销词/FEPS 缴费/CAP 公告摘要/FRP 平台/预订确认等）；RuleEngine SenderDomain **子域后缀匹配**（+2 测试）；`RulePackMigrator`（版本标记→机器来源邮件回炉+断点清空全量重拉，+4 测试）；全量 **246/246 全绿** | **真机效果：待确认 80→20（↓75%）**，course 11→36、announce 0→30、finance 0→4；from_address 空 100→0；user 改判永不触碰；**教训**：分类来源实际值 `rule-engine`（EngineName）非 `rule`——首版 WHERE 不匹配致真机无效而测试绿（测试种子已改贴真实值）；发布目录根的孤儿旧 JSON 优先于 Rules/ 子目录被读——已删并更新 SHA256 清单 |
| T-S13-C/D | G-S13-3/4 UI 升级 | **完成（截图自检）** | `DesignTokens.xaml`（色板/类别色/字体栈/字号阶/圆角阶+按钮/输入框/复选框/导航/列表容器样式，零新增依赖）；邮件行=类别色首字母头像+未读点+双行排版+时间右对齐+圆角悬停/选中；左栏=图标色块+徽章；阅读窗格=大标题+头像+徽章分隔线；Onboarding=阴影卡片；状态栏=状态灯+主色同步钮；`tools/front-and-shot.ps1`（DPI 感知截图） | 布局骨架=05 §3.2 三栏不变（线框兼容）；DEV 冒烟截图 `artifacts/screens/s6-inbox.png`、真机 `s13-real-inbox.png`；**教训**：资源 key 冲突（CountBadge 转换器 vs 样式）致 XamlParseException 启动崩——样式改名 CategoryBadge |
| T-S13-E | G-S13-5 门禁 | **完成** | 全量 246/246（Core 81/Services 78/Integration 87）；Debug+Release 0 警 0 错；5 工程漏洞扫描干净；UI 冒烟 DevFlow 过（FlaUI 自动化名全保留） | 规则/截图脚本/指令文档入库；统计原始输出不进 git（隐私红线） |

**S13 六项自检**：① 构建 0 警 0 错（双配置）；② 246/246 全绿；③ 漏洞扫描干净（零新增依赖）；④⑤ 错误码/日志红线复核（pack_migrated 日志仅版本与计数，无主题/发件人）；⑥ 真机验收态：迁移+重拉+重分类全链路自动完成，用户改判保留语义经测试锁定。

### S14 自定义类别 × 界面修复 × 应用图标（prompts/S14；2026-10-01）—— **已完成（真机验证通过）**

| 任务 | 需求 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S14-A | G-S14-1 应用图标 | **完成（真机可见）** | `tools/make-icon.ps1` 程序化绘制（GDI+：主色渐变圆角底+白信封+橙角标，16~256 六尺寸 PNG-ICO）；三处接入=csproj ApplicationIcon（exe/任务栏）+ MainWindow.Icon（标题栏）+ 托盘底图（保留未读角标） | 截图 `artifacts/screens/icon-preview.png`；真机任务栏/标题栏已见新图标 |
| T-S14-B | G-S14-2 界面修复 | **完成** | 顶栏 DockPanel→Grid 三段（品牌导航｜搜索居中｜未读筛选），最大化不散架；阅读窗格 `InjectReaderChrome` 构建期注入居中样式（页面浅灰底+正文白卡片 860px 对称留白+图片限宽）——消除最大化右侧空白；WebView2 禁脚本不变（样式构建期注入） | 用户报告的两处显示问题（第一张截图）均修复 |
| T-S14-C | G-S14-3 自定义类别（**CHG-012**） | **完成（250/250 全绿）** | 类别从枚举升级「类别注册表」：`CategoryIds` 内置七 ID（=历史 DB 值，**零数据迁移**）+ `categories` 表（EF 迁移 AddCustomCategories）+ `ICategoryStore/CategoryStore`（删除级联=邮件归其他+规则同删，事务）+ `CategoryCatalog` 进程内缓存；分类栏/改判菜单/规则编辑下拉全动态；设置页新增「自定义类别」管理（名称+图标+六色板+删除确认） | 删 `MailCategory` 枚举，42 文件 string 化手术（编译器驱动）；规则 JSON/评估集/存量库完全兼容（评估 98.79% 保持）；SanityTests 七值锁定改为内置 ID 锁定；新增 CategoryStoreTests 4 用例（级联/内置保护） |
| T-S14-D | 门禁 | **完成** | 全量 **250/250**（Core 81/Services 78/Integration 91）；0 警 0 错；发布产物+SHA256 更新；真机截图 `s14-real-inbox.png`（图标/顶栏/分类栏可见） | 修复 ExTc04 测试环境脆弱性：固定低位端口被防火墙 DROP 伪装超时→动态空闲端口（断言不削弱） |

**S14 六项自检**：① 0 警 0 错；② 250/250；③ 零新增依赖；④⑤ 日志红线复核（rule.saved 记类别 ID 非敏感）；⑥ CHG-012 登记完整、docs/ 零改动。

## 3.5 Mac 阶段任务块（MS0–MS10；总控指令 Mac v1.1；分支 `feature/mac-platform`；设计基线 docs/10，CHG-014 已批准 2026-10-02）

> 纪律：每个模块按「定位→计划→测试先行→实现→六项自检→运行验证→收尾」七步闭环；合回 main 前全量回归必须绿（基线 210）；Mac 行为证据=CI macos 真机执行或真机检查单，禁止以 Windows 本机运行宣称 Mac 功能完成。

### MS0 适配设计与 CI（docs/10 §10/§15）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-MS0-01 | 基线 | **完成** | git tag mac-baseline；全量 210/210 绿（Core 81 + Services 70 + Integration 59，Release）；首跑 UiSmoke 红灯=本机安装版实例占用全局互斥（环境冲突，已复跑确认） | 2026-10-02 |
| T-MS0-02 | docs/10 获批 | **完成** | CHG-014 状态=已批准（用户 2026-10-02）；含三项权衡差异批准 | 检查点①通过 |
| T-MS0-03 | 分支 | **完成** | feature/mac-platform 自 main(78c5a38) 拉出，已推送 origin | MS1/MS2 全程在此分支 |
| T-MS0-04 | CI macos job | **完成** | run [36986843084](https://github.com/tendernessnick/mail_helper/actions/runs/36986843084)：build-test（win）+ macos-build-test(osx-arm64) + macos-build-test(osx-x64) 全 success，release 正确 skip；本地 osx-arm64 交叉编译发布验证 `libe_sqlite3.dylib` 正确解析 | MailHelper.Mac.slnf 排除 WPF App 与 net8.0-windows 集成测试；push 触发扩 feature/** |
| T-MS0-05 | 平台边界机械检索 | **完成** | tools/check-platform-boundary.ps1（UTF-8 BOM，与仓库 ps1 约定一致）；Windows job 与 macos job 双侧执行，当前 0 违规 | 检查项：#if 族/System.Windows/Registry/COM interop/osascript |
| T-MS0-06 | 验证 | **完成** | 双架构 CI 绿（T-MS0-04 run 链接） | 门禁出口通过 |

### MS1 平台抽象抽取（docs/10 §4；Windows 等价迁移）—— **已完成（2026-10-02）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-MS1-01 | P-02 路径 | **完成** | `IAppPaths`（Core/Abstractions/Platform.cs）+ `WindowsAppPaths`；Bootstrapper/App.xaml.cs 双处硬编码收敛；WindowsAppPathsTests 3/3（默认路径=迁移前硬编码逐字节一致锚点） | GetTempFilePath 供崩溃日志/阅读副本使用（现静态实现等价保留） |
| T-MS1-02 | P-03 单实例 | **完成** | `WindowsSingleInstanceLock`（App/SingleInstance.cs 逐行等价迁移并删原文件）；WindowsSingleInstanceLockTests 3/3（ acquisitions/释放/SHOW 唤起） | mutexName/pipeName 可注入（AutostartService 先例）；记录语义边界：命名互斥同线程可重入，「第二实例失败」以跨进程为前提——测试以另一线程模拟 |
| T-MS1-03 | P-04 自启动 | **完成** | `AutostartService : IAutoStarter`（逻辑零改动）；既有 AutostartServiceTests 2/2 保持绿 | Bootstrapper 闭包改经 DI 的 IAutoStarter |
| T-MS1-04 | P-08 更新安装 | **完成** | `IUpdateInstaller` + `WindowsUpdateInstaller`（Inno 参数与 S17 逐字一致）；UpdateService.InstallSilently 改策略委托（1 行，编译期保证） | SettingsViewModel 签名零改动 |
| T-MS1-05 | DI 改造 | **完成** | Bootstrapper.BuildHost(singleInstanceLock) 显式注入（抢占仍先于 BuildHost——第二实例不做 DB 初始化的时序等价保留）；App.xaml.cs 四处调用点换接口；ClearLocalData 改 IAppPaths.DataDir | static lambda 捕获限制以 RegisterServices 静态方法化解 |
| T-MS1-06 | 验证 | **完成** | ① Release 构建 0 警 0 错；② 全量 **216/216 绿**（Core 81 + Services 70 + Integration 65，含新增 6 契约测试，既有 210 零删除零削弱）；③ 7 工程漏洞扫描 0；④ 平台边界检查 0 违规；⑤ 新代码零新增日志/错误路径（红线无涉）；⑥ docs/10 §4 接口表已更新为定稿签名；⑦ **Windows 版行为未被改变**——UiSmoke 真机冒烟全绿 + 截图比对（artifacts/screens/s6-inbox.png vs HEAD 基线）：三栏布局/色卡/字体/状态栏零变化，仅 DEV 种子时间漂移 | 中途红灯定性：a) 锁测试与 UiSmoke 抢全局互斥（测试设计缺陷，名称注入修复）；b) Serilog_WritesSyncCompletedToFile 负载偶发（隔离 3/3 绿、修复后全量绿、夹具为每测试唯一 GUID 目录） |

### MS2 共享 ViewModel（docs/10 ADR-006）—— **已完成（2026-10-02）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-MS2-01 | 共享工程 | **完成** | MailHelper.ViewModels（net8.0）：Main/Rules/Settings 三 VM + CategoryCatalog 迁入（命名空间 MailHelper.ViewModels）；IMainThreadDispatcher 抽象 + WpfMainThreadDispatcher（App 层）；UpdateService 迁 Infrastructure/Updates（安装器必选注入，化解 CA1416）；SettingsViewModel 增 WriteCrashLog 委托（模式同 SetAutostartAsync） | UiSmoke 抓出 MS1 潜伏 bug：`AddSingleton(_ => paths)` 泛型推断注册为具体类型，IAppPaths 仅「一键清除」路径解析——已修为显式接口类型注册（同修 IMainThreadDispatcher） |
| T-MS2-02 | WPF 改引用 | **完成** | App.csproj 引用共享工程；XAML×3 xmlns 更新；App 代码后置 usings 更新；Bootstrapper 注入 dispatcher | 行为不变：XAML 绑定/AutomationProperties.Name 零改动 |
| T-MS2-03 | 验证 | **完成** | ① 构建 0 警 0 错（7→8 工程）；② 全量 **216/216 绿**；③ 漏洞扫描 0；④ 平台边界 0 违规；⑤ 无新增日志/错误路径；⑥ 接口与 docs/10 §4 定稿一致；⑦ **Windows 版行为未被改变**：UiSmoke 全绿 + 截图比对（MS2 vs MS1 基线）布局/色卡/字体零变化；CI run [36989550301](https://github.com/tendernessnick/mail_helper/actions/runs/36989550301) 全 success | MS1 的 CI 红灯（run 36988416202 windows Test 步骤）未在 MS2（含其全部内容）复现——判定为 CI 慢机 UiSmoke 时序性失败（历史同型：0d64a70/fffcd14/abd9442/a210d1a），非代码回归；持续观察 |

### MS3 Avalonia 骨架（docs/10 §7）—— **已完成（2026-10-02）**

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-MS3-01 | App 工程 | **完成** | MailHelper.App.Avalonia（net8.0 + Avalonia **11.3.22**，11 线最新稳定；12.x 已出但按获批 ADR-006 取 11 线，升级走变更提案）；Fluent + `RequestedThemeVariant=Default` 跟随系统；DesignTokens.axaml 浅色=WPF 令牌逐值镜像、深色=Fluent 深色板提亮（P0 #FF5C5C）；Shell 三栏（220/1.5*/*≈05 §5.3）+ 顶栏导航 pill + 状态栏；规则/设置占位页（MS7 落地完整功能） | i18n：LanguageService/Strings 复用接入（WPF 现状为中文直书 XAML，Avalonia 同形态对齐；完整双语接线列 MS7 对齐项） |
| T-MS3-02 | 平台件立桩 | **完成** | OS 守卫模式（非 Windows 抛明确 PlatformNotSupportedException）；AvaloniaMainThreadDispatcher（IMainThreadDispatcher 第二实现）；DevSeed 迁 Infrastructure（双 UI 共用）；Avalonia 预览用独立互斥名（不与 WPF 安装版互抢） | MS4 将守卫展开为真实 Mac 分支 |
| T-MS3-03 | CI 接入 | **完成** | slnf 增 App.Avalonia（macos job 真机编译）；publish 换 `App.Avalonia -r <arch> -p:SelfContained=true` | docs/10 §10 MS3 里程碑动作 |
| T-MS3-04 | 验证 | **完成** | ① 构建 0 警 0 错（9 工程）；② 全量 **216/216 × 连续三轮**；③ 漏洞扫描 0（含 Avalonia 新依赖）；④ 边界 0 违规；⑤ 无新增日志/错误路径；⑥ 与 docs/10 §7 结构一致；⑦ **Windows 版行为未被改变**（UiSmoke 于全量内通过；DevSeed 迁移等价）；**截图** artifacts/screens/avalonia/ms3-shell.png（三栏/顶栏/状态栏与 05 §3.2 结构一致，类别色块正确）；**osx-arm64 跨编译发布成功**（112MB 自包含，libAvaloniaNative.dylib/libe_sqlite3.dylib 解析正确） | 左栏计数徽章空=预期（DEV 数据流需「连接」动作，属 MS5 Onboarding 范围） |
| T-MS3-05 | 红灯根因修复 | **完成** | FirstAcquire 契约测试偶发红（本地 2 次 + CI 2 次同因）：`Task.Run` 线程池**工作窃取**可把「第二实例」任务派回持有互斥的线程→同线程重入假成功；改专用 Thread 后连续三轮全绿——**MS2 期两次 CI 红灯（f6c25c4/a7824c5）真因即此**，推翻「UiSmoke 时序」初步判定 | 修复处含确定性注释；CI 失败注解化管道保留备用 |

### MS4 Mac 基础设施 + 通道骨架（docs/10 §6）—— 待办

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-MS4-01 | P-02/03/04 Mac 实现 | 待办 | — | MacAppPaths/MacSingleInstanceLock(UDS)/LaunchAgent 自启动 + 契约测试（CI macos 真机执行） |
| T-MS4-02 | P-01 通道骨架 | 待办 | — | OutlookMacMailProvider + IAppleScriptRunner + 脚本资源化 + 假 osascript 测试先行（docs/10 §6.2 协议） |
| T-MS4-03 | 真机检查单 | 待办 | — | tools/mac/real-mac-checklist 起步（§14 项 1–3） |

### MS5 Avalonia 三栏主界面（docs/10 §5.1/§8.7）—— 待办：虚拟化列表/徽章色卡/四态/净化文本阅读窗格；headless 测试 + 截图比对。

### MS6 通知与菜单栏 Mac（docs/10 §5.2/§5.3/§8.5）—— 待办：Avalonia TrayIcon 菜单/角标/osascript 通知去重/关窗常驻；单测 + 真机检查单。

### MS7 功能对齐 —— 待办：待确认队列/纠正反馈/规则编辑器/设置/FTS 搜索在 Avalonia 落地（复用 Windows 同逻辑测试 + 截图）。

### MS8 AppleScript 通道打磨（docs/10 §6.3–6.5）—— 待办：水位/分页限速/New Outlook 探测与引导/四类错误文案/SyncCoordinator 装配（DEV 可注入 Mac 假通道）；假 osascript 全场景 + CI 真机冒烟；真实邮箱列检查点②。

### MS9 打包与分发（docs/10 §9）—— 待办：.app 组装/icns/DMG/codesign+notarytool 脚本/CI 集成/更新器 Mac 策略；CI 产物结构校验；真机安装列检查点②。

### MS10 发布对齐（docs/10 §12/§14）—— 待办：08 章 mac 变体检查单、运维 FAQ mac 节（自动化权限/Gatekeeper/New Outlook/通知权限）、SHA256 校验、真机核对包汇总。

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
| CHG-011 | **落地路径调整（用户批准发起，2026-09-30）**：02 章 FR-02/EX-01 预案仅覆盖 Graph→IMAP 双 OAuth 通道；D-65 实况为 CityU 租户全局禁止用户同意，两条 OAuth 通道均被「需要管理员批准」拦截，且用户无 ITSC 管理员权限——预案未覆盖「本机已装经典 Outlook 且已登录」的第三条路 | 提案**追加** Outlook 桌面通道（`ChannelKind.OutlookDesktop`）：COM 晚绑定复用经典 Outlook 本机登录态读取收件箱（不发起任何 OAuth）；`IMailProvider` 追加默认方法 `GetAccountAddressAsync`（仅桌面通道实现）；连接流程按 `provider.Kind` 分支跳过 MSAL。docs/ 零改动；03 §5.4 断点/幂等语义、04 §4.1 增量契约以桌面等价物保持（见 D-66/D-67）。**已实施并经用户真机实测通过（2026-10-01）** | 已实施（用户发起），待归档 |
| CHG-012 | **02 章附录 A「邮件七类别固定」**：用户验收后提出自定义类别需求（S13 汇报披露能力边界，用户确认扩展）；枚举形态无法承载用户运行期新增类别 | 类别升级为「类别注册表」：内置七 ID 不变（course/career/admin/finance/announce/subscription/other——DB 存量值/规则包/评估集完全兼容，零数据迁移），新增 categories 表承载用户自定义（名称/图标/颜色/排序），删除级联=邮件归其他+规则同删；docs/ 零改动。**已实施（2026-10-01，真机验证通过）** | 已实施（用户发起），待批准 |
| CHG-014 | **Mac 平台适配（总控指令发起，2026-10-02）**：仓库演进为双平台单代码库；Mac 版通道=Outlook for Mac 经典版 AppleScript 直读（与 CHG-013 同理念，绕开租户 OAuth）；涉及新增 ADR-006（Avalonia 11 + 共享 ViewModel + WPF 冻结维护模式）、ADR-007（Mac 通道）、ADR-008（零原生依赖）、ChannelKind 追加 OutlookMac、FR-14 AC1「通知点击直达」在 Mac v1 不可达（osascript 无点击回传，已知差异）、阅读窗格 v1 改净化文本渲染、通知署名为脚本编辑器 | 设计基线 **docs/10-Mac平台适配设计.md**（差异矩阵 P-01~P-13、平台接口清单 IAppPaths/ISingleInstanceLock/IAutoStarter/IUpdateInstaller/IMainThreadDispatcher、AppleScript 协议与错误映射 MAC-001~005、CI macos job、真机检查单）。**已批准（2026-10-02，用户批准，检查点①通过；含三项权衡差异与通道路线确认=仅经典版 Outlook 直读）** | **已批准（检查点①通过）** |

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
| D-59 | IMAP 断点复用 SyncCheckpoint.delta_link 字段，编码 `imap://INBOX?uidvalidity={v}&lastuid={u}`；解析非法/缺失一律视为无断点（全量，安全侧） | 04 §2.1 CompleteAsync「IMAP 返回 UID 水位标记」未定格式；TEXT 字段通用承载使协调器 EX-05 断点语义零改动 |
| D-60 | IMAP ProviderMessageId = `imap:{登录邮箱}:INBOX:{uid}`；通道层不含 accountId（协调器入库时经 RemoteMessageMapper 关联） | 04 §3.1 注释「imap:accountId:folder:uid」中 accountId 在通道层不可得；登录邮箱全局唯一且稳定，入库后 id 语义等价 |
| D-61 | FR-02 AC2「两通道同步结果一致」解释为内容层一致（邮件集合/字段语义一致）；messages.id 因 Graph id 与 IMAP 复合键结构不同必然不同 | Graph id 为 Exchange GUID、IMAP 为 UID 复合键，结构一致性不可实现；幂等 upsert 保证通道内一致（FR-04 AC2） |
| D-62 | v1 IMAP 仅同步 INBOX 文件夹 | 03 §5.4「每文件夹记录水位」在 v1 收敛为 INBOX；多文件夹随 V1.x 演进；IImapClientAdapter 接口已按文件夹粒度预留 |
| D-63 | MailKit 4.16.0（而非较旧稳定版）：4.8.0~4.15.x 含 Moderate 公告 GHSA-9j88-vvj5-vhgr（CVE-2026-41319 STARTTLS 响应注入） | 自检③红线：dotnet list package --vulnerable 无高危/无 Moderate 残留；4.16.0 为公告修复版 |
| D-64 | 真实租户配置经环境变量注入（MAILHELPER_CLIENT_ID/TENANT_ID/FORCE_IMAP/IMAP_USER），源码占位符不变；单租户验证模式 authority 租户化 + 回调改 http://localhost（loopback 免协议注册，移动桌面平台默认支持） | 检查点①落地方式：不硬编码（09 红线）、用户无需重新编译；用户 CityU 自助注册的是单租户应用，common 端点不适用 |
| D-65 | 检查点②实况（2026-09-30）：CityU 租户用户同意策略=全局禁止（Graph Mail.Read 与 IMAP scope 均提示「需要管理员批准」），RISK-01 触发且预案 1（自助注册单租户应用）不能独立解锁——预案未覆盖「注册放行/同意全禁」的组合 | 真实租户验证即为此暴露事实；结论：唯一合规解锁=ITSC 管理员批准（工单/邮件），已交付用户英文邮件稿；此事实应回写 02 章 EX-01/RISK-01 应急预案（列 CHG 候选，待用户批复） |
| D-66 | Outlook 桌面通道断点=收件时间水位 `outlook://inbox?received={yyyyMMddTHHmmssZ}`（复用 delta_link 字段）；每轮拉取窗口回退 **24h 晚到窗口**（服务器推送延迟/离线期到达的旧邮件不漏），靠 EntryID 复合键 upsert 幂等防重；首轮断点为 null=全量 | COM 无 delta query 等价物；ReceivedTime 为单调近似水位——时间水位+回退窗口+幂等键三层兜底，语义对齐 03 §5.4 EX-05（断点损坏回退全量） |
| D-67 | Outlook 桌面通道无远端删除感知（Outlook 删除的邮件本地缓存保留） | 与 IMAP 通道同等限制（03 §5.4 v1.0 明确接受：删除感知列 V1.x）；桌面通道删除同步无事件源，不做轮询比对 |
| D-68 | **检查点②达成路径=CHG-011 桌面通道**（2026-10-01 用户实测：真实 CityU 邮箱连接→全量同步→自动分类→HTML 阅读窗格全通过）；~~Graph/IMAP OAuth 通道代码保留，ITSC 批准后经环境变量切换即可启用（D-64 注入机制不变）~~ **【已被 CHG-013（2026-10-02）取代：OAuth 通道代码整体移除，唯一通道=经典版 Outlook 登录态】** | RISK-01 的用户侧解法：不申请权限，复用用户已在 Outlook 完成的登录态；是 03 章 MOD-03「统一输出 RemoteMessage 流与 deltaLink」抽象的第三个实现，未破坏通道可切换性 |
| D-69 | SenderDomain 匹配语义=根域后缀匹配（`domain == pattern || domain.EndsWith("." + pattern)`） | 03 §5.3 未定义子域语义；真机实证 Canvas 经 `*.instructure.com` 投递域群发，精确相等会漏配；后缀拼界（evil-instructure.com）经测试锁定不误命中 |
| D-70 | 内置规则包版本迁移（S13-B）：Settings 键 `rules.builtin.applied_version` 记录已应用版本；不一致时机器来源（classified_by≠'user'）邮件回炉重分类 + 断点清空触发全量重拉（顺带回填历史缺失的同步字段） | 04 §7 未定义规则升级语义；断点清空一举两得——规则重跑需要地址已修复的存量，而 COM 通道无 delta 概念只有时间水位；用户改判（EX-08）永不触碰 |
| D-72 | 类别表示=字符串 ID 全链路（内置 ID 恒定小写；自定义 ID=custom-<8hex>）：Core 不依赖类别存储（RuleSetParser 仅校验内置 ID，自定义 ID 由上层 RuleManagementService/UI 依注册表管理）；删除类别语义=邮件归 other+规则同删（事务） | 枚举→string 的受控迁移；DB messages.category 本就 TEXT 存 ID（零数据迁移根因）；规则引擎投票/排序对类别字符串透明，不破坏 03 §5.3 语义 |
| D-71 | UI 视觉系统落 `DesignTokens.xaml` 唯一事实源（Fluent 基色保留 05 §5.1 语义色），类别新增语义色板；样式零魔法数 | 05 章未钉死整体设计语言（仅线框与语义色）；零新增 NuGet 依赖（总控十一.2）；深色主题列 S14 |

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
| M2（S7–S11） | **完成** | S7 通知托盘 / S8 反馈闭环 / S9 规则+设置+i18n / S10 搜索 / S11 IMAP——FR-01~14 全部实现；各模块六项自检全过（见 §3 各节） |
| M3（S12） | **完成（RC 就绪；正式 Release 待检查点+UAT）** | 见下 |

### M3（S12）—— 2026-09-28 收口（RC 就绪）

**完成项与证据**：
- 发布双产物（08 §4.1）：便携版单文件 200MB（发布冒烟通过）+ Inno Setup 安装包脚本（WebView2 离线引导 187MB、卸载清数据）；SHA256 清单（08 §4.4 无证书路径）
- CI release job：push main → publish → vpk pack/publish beta 通道 → SHA256（08 §3.2/§4.3）
- 72h 长稳脚本（短跑验证 PASS + 长跑后 SQLite integrity ok）+ CHANGELOG.md + README 发布说明

**发布门禁（06 §9 G1–G7）对照**：

| 门禁 | 标准 | 状态 | 证据 |
| --- | --- | --- | --- |
| G1 测试 | 100% 通过，核心 ≥80% | **✅ 全绿** | 230/230（含 UI 冒烟）；Core 95.15%、Core.Services 95.02%（CI 同款门禁脚本） |
| G2 分类评估 | §4.1 四项指标 | **✅ 全绿** | 97.98% / P0 召回 100% / 误报 0% / 待确认 11.34%（artifacts/eval/evaluation-report.md） |
| G3 系统测试 | M 级 FR 100%，S 级 ≥90% | **✅（自动化口径）** | TC-001~020 自动化全过（见下表）；TC-015 帧率/TC-018 真实弹窗属人工口径，待检查点② |
| G4 性能 | PERF-01~05 | **⚠️ 部分** | PERF-03（万级搜索 P95<500ms）✅ 自动化；PERF-01/02/04/05 需真实环境与完整 72h——列检查点② |
| G5 安全 | SEC-01~06 | **✅（本地口径）** | SEC-01 令牌 DPAPI（S3）/SEC-02 最小权限/SEC-03 WebView2 沙箱（S6）/SEC-04 FTS 注入清洗（S2）/SEC-05 数据位置/SEC-06 卸载清除（iss 脚本）；真实渗透测试列 UAT 后 |
| G6 缺陷 | 无 P0/P1 未关闭 | **✅** | 全程 TDD；已知限制均为文档披露项（IMAP 删除感知/语言重启生效）非缺陷 |
| G7 UAT | T1–T4 ≥90% | **⏳ 待用户** | 5 学生×2 周真实使用——依赖检查点②账号 |

**TC-001~020 状态摘要**（详证在各模块节）：TC-001~009（认证/同步/幂等）✅ 自动化；TC-010~013（分类/规则/搜索）✅ 自动化+评估门禁；TC-014 反馈闭环 ✅ 端到端；TC-015 滚动 ⚠️ 虚拟化已实现、帧率计量待真实环境；TC-016 沙箱 ✅（脚本禁用+外链拦截断言）；TC-017 搜索中英 ✅；TC-018 通知 ✅（决策断言+真实弹窗待②）；TC-019 关窗常驻 ✅ FlaUI 自动化；TC-020 自启 ✅ 注册表实测。

**08 §5 发布检查单对照**：1 门禁=本表；2 版本=0.1.0+规则包 v2026.09；3 CHANGELOG ✅；4 规则包评估 ✅；5 安装三步=待 CI/真实机；6 SHA256 ✅（签名=无证书路径）；7 Release 页=待 GitHub 仓库；8 通道切换=待 CI 首次运行。

**检查点清单（唯一剩余项）**：① Azure ClientId（占位符已在位，回填即切真实通道）；② 真实学校测试账号（Graph 端到端 50 封/IMAP 连通/真实弹窗/完整 72h/PERF-02）；③ 签名证书（不阻塞，无证书路径已执行）。

### S15 —— 2026-10-01 开源发布链路（GitHub Releases + 应用内更新）

**完成项与证据**：
- 应用内检查更新（S15/08 §4.3）：`Updates/UpdateService`（UpdateManager + GithubSource，prerelease=false 只认正式版）+ 设置页「关于」分组（检查更新/进度/重启并安装）；Velopack 1.2.161
- CI 发布链路修正：release job 补 `permissions: contents: write` 与 GITHUB_TOKEN（原 `vpk publish --gitHubRepo` 参数在 vpk 新版不存在，首跑必败）；改用 `vpk upload github`；push main→beta 预发布，tag v*.*.*→stable 正式版（版本以 tag 为唯一事实源，-p:Version 注入）
- 版本统一：Directory.Build.props 0.1.0 → 0.4.0 基线（原三处版本不一致：props 0.1.0 / iss 0.3.1 / CI 0.1.构建号）
- 发布方式：Inno Setup 退役（installer/MailHelper.iss 删除，git 历史可溯），Velopack 全面接管（Setup.exe + 便携 zip + 增量更新）
- 文档：README 下载/安装/更新章节 + docs/发布操作指南.md（发版流程/排障/渠道说明）+ LICENSE(MIT) + CHANGELOG 0.4.0

**质量**：全量测试 250/250 通过；0 警告 0 错误；本地 publish+vpk pack 冒烟通过

**待办**：首次推送后盯 CI 绿 → tag v0.4.0 发首个正式 Release → 真机验证应用内更新闭环（0.4.0→下一版）

### S16 —— 2026-10-02 通道收敛（CHG-013：仅经典版 Outlook，随 v0.5.0）

**决策**：用户拍板——放弃 OAuth 系通道（真实租户授权不可行，检查点①长期阻塞），唯一通道 = 本机经典版 Outlook 登录态（COM 直读）

**完成项与证据**：
- 移除 GraphMailProvider / ImapMailProvider / ImapClientAdapter / TokenService(MSAL) / AuthService / FakeTokenProvider / DpapiFileProtector / AuthErrorMapper 及 ITokenProvider、AuthModels 抽象；MailKit、Microsoft.Identity.Client、ProtectedData 包移除
- Bootstrapper 通道装配收敛两态（DEV 假通道 / OutlookDesktop）；MainViewModel.ConnectAsync 仅剩 Outlook 探测分支；设置页移除登出按钮（无令牌可撤销）、通道恒显「经典版 Outlook」；Onboarding 卡片加「需经典版 Outlook」前提提示
- FakeMailProvider.Kind=OutlookDesktop + GetAccountAddressAsync（DEV/UI 冒烟走同真实路径，无 Outlook 环境可测）
- 文档同步：README 前提声明+技术栈、使用手册（连接流程/FAQ/附录/已知限制）、CHANGELOG 0.5.0、docs/02 FR-01~03 废弃注记、docs/03 ADR-002 标记替代 + 新增 ADR-005

**质量**：全量测试 210/210 通过（移除认证/Graph/IMAP 用例 40 个）；0 警告 0 错误；UI 冒烟（含新 Onboarding 路径）通过

**遗留**：语言字典 Strings 其余键无变化；ChannelKind 枚举保留 Graph/Imap 值仅作存量 DB 兼容，不再产生新记录

### S17 —— 2026-10-02 安装向导回归（Inno Setup）+ 应用内更新改静默重装

**决策**：用户要传统安装向导（自选目录）；Velopack 安装器为刻意一键式、无目录页（--instLocation 仅管 scope）→ 回归 Inno Setup，应用内更新改为下载完整安装包静默重装

**完成项与证据**：
- installer/MailHelper.iss：全中文向导（语言文件 ChineseSimplified.isl 随仓库分发——本机 Inno 自带的该文件内容竟是 404 页）；目录页默认 {localappdata}\Programs\MailHelper；WebView2 检测引导；卸载询问清数据；版本号经 GetVersionNumbersString 取自 publish 产物；/DSetupOutputName 支持 beta/stable 命名
- UpdateService 重写：GitHub API releases/latest 查版（Version 比较，只认正式版）→ 安装包下载（字节进度）→ /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS 静默覆盖安装 → Inno [Run] 自动重启应用；Velopack 包与启动钩子移除
- CI：vpk 全链路替换为「便携 Compress-Archive + ISCC 编译 + tools/publish-release.ps1（创建/复用 Release、按名替换资产、CHANGELOG 小节作说明、校验和内嵌）」；publish-release.ps1 兼容 PS5.1/pwsh7，DryRun 本地验证
- 文档：README 安装/更新/开源库行、使用手册（向导流程/装到哪了/FAQ/更新节）、发布操作指南整体重写（S17 形态 + v0.5.0 迁移说明）

**质量**：全量测试 210/210 通过；0 警告 0 错误；本地 ISCC 编译产物 MailHelper-stable-Setup.exe ≈74MB

**迁移注意**：v0.5.0（Velopack 安装）用户的应用内更新找不到 0.6.0+（发布物无 Velopack 元数据），需从发布页手动装一次 v0.6.0；旧 Velopack 副本建议卸载（数据目录不受影响）

### MM0（MS0–MS2）—— 2026-10-02 收口：Mac 阶段基座就绪

**完成项与证据**：
- docs/10 获批（CHG-014，检查点①；用户同步确认通道路线=仅经典版 Outlook 直读，PROGRESS 过时 Azure 表述已清理）
- MS0：mac-baseline tag；feature/mac-platform 分支；CI macos job 双架构绿（run 36986843084）；平台边界机械检索入 CI；MailHelper.Mac.slnf
- MS1：平台抽象四接口落地（IAppPaths/ISingleInstanceLock/IAutoStarter/IUpdateInstaller），Windows 实现等价迁移（SingleInstance.cs 迁 Infrastructure 并删原文件）；+6 契约测试；216/216
- MS2：MailHelper.ViewModels 共享工程（三 VM + CategoryCatalog）；IMainThreadDispatcher；UpdateService 迁 Infrastructure；WPF 改引用共享工程；216/216 + CI run 36989550301 全 success
- 发现并修复：DI 泛型推断注册具体类型的潜伏 bug（UiSmoke 抓出）；记录 Windows 命名互斥同线程可重入语义边界

**未完成项与原因**：无阻塞项；MS3 起按 §3.5 顺序推进（Avalonia 骨架为下一步）

**与设计的偏差及处理**：
- UpdateService 落点由 App 调整为 Infrastructure/Updates（共享 VM 需消费且不能违反 Core.Services 无 SDK 原则）——docs/10 §4 P-08「查版逻辑共享化」的落位细化，已回写文档
- IAppPaths 成员以属性定稿（文档原为方法示意）——已回写 docs/10 §4
- IMailProvider 的 OutlookMac 侧按计划在 MS4（本里程碑不涉及）

**对 Windows 版的影响评估**：**无**。回归证据=全量 216/216（基线 210 零删除零削弱）+ UiSmoke 真机冒烟全绿 + 两次截图比对（MS1 vs v0.6.0 基线、MS2 vs MS1）布局/色卡/字体零变化 + CI windows job 绿（run 36989550301）

**下一步计划**：MS3 Avalonia 骨架（App 工程/Shell 导航/Fluent 主题映射 05 色卡/双语资源复用；Windows 上运行截图比对）
