# PROGRESS.md — MailHelper 唯一进度事实源

> **纪律**：每完成一项立即更新本文件；状态只允许「待办 / 进行中 / 完成 / 阻塞」；每条任务五字段齐全（编号、对应 FR/UC、状态、证据、备注）。
> **会话恢复协议**：读本文件 → `git log --oneline -10` → `dotnet test`（SDK 可用时）→ 从最近未完成任务继续。禁止重做已完成工作，禁止询问「从哪里开始」。

## 0. 当前快照

| 项 | 值 |
| --- | --- |
| 更新时间 | 2026-09-26 |
| 里程碑 | M0（已完成，见 §7 里程碑报告；唯一未闭合项：CI 远端跑通待 GitHub 仓库） |
| 当前模块 | S1 领域核心（待开工） |
| 阻塞 | 无（BLK-001 已解除） |
| 下一步 | S1：TextNormalizer 与 RuleEngine 测试先行（R-01~R-07 失败测试 → 实现 → 自检） |

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

### S1 领域核心（Sprint S1）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S1-01 | FR-07 | 待办 | — | 枚举/RemoteMessage/IMailProvider/IClassifier 签名按 04 §2（四枚举种子已随 S0 落盘：`Core/Domain/Enums.cs`） |
| T-S1-02 | FR-07 / 06 §4.3 | 待办 | — | TextNormalizer：HTML→纯文本、剥引文/签名；先写 R-01~R-07 失败测试再实现 |
| T-S1-03 | FR-07/08 | 待办 | — | RuleEngine：规则 JSON 加载、热重载、加权评分、双阈值判定（top1≥3 且分差≥2） |
| T-S1-04 | FR-07 | 待办 | — | 样本集试跑脚本（S5 完整评估流水线的前置桩） |

### S2 存储层（Sprint S1）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S2-01 | 04 §3 | 待办 | — | EF 模型+迁移；DDL/四索引/FTS5 外部内容表+三触发器；WAL；启动 `integrity_check` 重建策略（EX-07） |
| T-S2-02 | 03 §6 | 待办 | — | 正文磁盘缓存 `{sha1}.html` + LRU（默认 2GB 上限） |
| T-S2-03 | FR-04 AC2 | 待办 | — | 仓储接口 + 主键幂等 upsert；临时库集成测试（CRUD/FTS/幂等） |

### S3 认证（Sprint S1；真实登录=检查点①）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S3-01 | FR-01 | 待办 | — | TokenService：系统浏览器交互登录 / 静默刷新 / DPAPI(CurrentUser+熵) 缓存 |
| T-S3-02 | FR-01 | 待办 | — | 假令牌提供器；授权状态机单元测试 |

### S4 Graph 同步（Sprint S1）

| 编号 | 对应 | 状态 | 证据 | 备注 |
| --- | --- | --- | --- | --- |
| T-S4-01 | FR-04/05 | 待办 | — | GraphMailProvider：delta 分页($top=100)、deltaLink 断点、正文按需单封拉取（P0 候选例外）、@removed 删除语义 |
| T-S4-02 | FR-04 | 待办 | — | SyncCoordinator：PeriodicTimer、状态机、429(Retry-After,≤3)/5xx(2s/8s/30s)/401(静默刷新一次)退避 |
| T-S4-03 | 06 §6 | 待办 | — | WireMock.NET 契约测试覆盖 EX-TC-01~08 |

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
