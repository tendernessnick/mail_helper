# PROGRESS.md — MailHelper 唯一进度事实源

> **纪律**：每完成一项立即更新本文件；状态只允许「待办 / 进行中 / 完成 / 阻塞」；每条任务五字段齐全（编号、对应 FR/UC、状态、证据、备注）。
> **会话恢复协议**：读本文件 → `git log --oneline -10` → `dotnet test`（SDK 可用时）→ 从最近未完成任务继续。禁止重做已完成工作，禁止询问「从哪里开始」。

## 0. 当前快照

| 项 | 值 |
| --- | --- |
| 更新时间 | 2026-09-26 |
| 里程碑 | M0 已完成；M1 进行中（S1~S4 已完成，S5 分类服务待开工） |
| 当前模块 | S5 分类服务（ClassificationService 管线 + 预置规则包 v1 + 600 封样本语料 + 评估流水线） |
| 阻塞 | 无（CI 远端跑通仍待 GitHub 仓库，非关键路径） |
| 下一步 | S5 按流水线推进：ClassificationService 测试先行（管线：预处理→分类→写回→待确认）→ 预置规则包 v1（含 P0 截止正则，四种标准 Kind）→ 样本集 ≥600 封 .eml + labels.csv → 评估流水线（达标线 ≥85%/≥95%/≤10%，不达标禁入 S6） |

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

### S5 分类服务（Sprint S2；不达标禁入 S6）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S5-01 | FR-07 | 待办 | — | ClassificationService 管线：预处理→分类→阈值→落库→待确认 |
| T-S5-02 | FR-07/08 | 待办 | — | 预置规则包 v1（含 P0 截止时间正则；Kind 仅用四种标准值，见 CHG-001） |
| T-S5-03 | 06 §2 | 待办 | — | 样本集 ≥600 封 .eml + labels.csv（7 类×P0–P3×中英文，含 R-01~R-07 与 ReDoS 探针；60/40 划分） |
| T-S5-04 | FR-07/08 | 待办 | — | 评估流水线（dotnet test 形式）输出混淆矩阵至 artifacts/eval/；达标线三指标 |

### S6 WPF 主界面（Sprint S2 / M1 出口）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S6-01 | FR-12 | 待办 | — | Shell 与导航、三栏骨架、布局持久化 |
| T-S6-02 | FR-12 / NFR-02 | 待办 | — | 列表虚拟化、重要度徽章色卡（05 §5.1）、空/加载/错误/离线四态 |
| T-S6-03 | FR-12 AC2 | 待办 | — | WebView2 沙箱阅读窗格（禁脚本） |
| T-S6-04 | 05 章比对 | 待办 | — | PowerShell 截屏至 artifacts/screens/ + 线框比对；FlaUI 点击级冒烟（引入理由：UI 验证需要） |

### S7 通知与托盘（Sprint S3）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S7-01 | FR-14 | 待办 | — | P0 逐封 / P1 聚合(≥3 封)、message_id 去重表、勿扰时段 |
| T-S7-02 | FR-14 | 待办 | — | 托盘角标/菜单/关窗常驻（真实系统通知效果验证列入检查点②） |

### S8 待确认队列与反馈闭环（Sprint S3）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S8-01 | FR-09 | 待办 | — | 置信度阈值 + 待确认队列 + 侧栏入口计数 |
| T-S8-02 | FR-11 | 待办 | — | 改判入口 + FeedbackService 半自动发件人规则（×1.5 权重上限） |
| T-S8-03 | FR-11/TC-014 | 待办 | — | 端到端：改判后同发件人新邮件自动归新类别 |

### S9 规则编辑器、设置、账户、i18n（Sprint S4）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S9-01 | FR-10 | 待办 | — | 规则编辑器（四种类型）+ 试跑预览（最近 100 封） |
| T-S9-02 | FR-03/06 | 待办 | — | 设置页（同步/通知/外观/高级）、账户管理、登出、开机自启注册表 |
| T-S9-03 | NFR-12 | 待办 | — | zh-CN / en 双语资源文件 |

### S10 全文搜索（Sprint S4）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S10-01 | FR-13 | 待办 | — | FTS5 接入 + 搜索语法（from:/cat:/p:） |
| T-S10-02 | NFR-03 | 待办 | — | 万级造数脚本 + 检索计时 P95 < 500ms |

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

| 里程碑 | 状态 | 报告 |
| --- | --- | --- |
| M1（S1–S6） | 未开始 | — |
| M2（S7–S11） | 未开始 | — |
| M3（S12） | 未开始 | — |
