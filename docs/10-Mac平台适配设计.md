# 10 Mac 平台适配设计（设计补充）

| 文档编号 | MH-MAC-10 | 版本 | v1.0 |
| --- | --- | --- | --- |
| 作者 | 开发 | 状态 | **待评审（CHG-014，检查点①）** |
| 创建日期 | 2026-10-02 | 上游文档 | [02 需求规格说明书](02-需求规格说明书.md)、[03 系统架构设计说明书](03-系统架构设计说明书.md)（ADR-005）、[04 详细设计说明书](04-详细设计说明书.md)、[05 UI-UX设计说明](05-UI-UX设计说明.md)、[08 构建发布与运维手册](08-构建发布与运维手册.md)、[09 安全与合规](09-安全与合规.md) |
| 评审记录 | 无（首次提交） | 下游文档 | 06 测试计划（mac 扩展随 MS10）、08 构建发布（mac 节随 MS10）、PROGRESS.md Mac 阶段任务块 |

---

## 1. 背景与范围

Windows 版（WPF，v0.6.0，210 测试全绿）已交付，唯一邮件通道为本机经典版 Outlook 登录态直读（ADR-005，CHG-013）。本文档定义 **macOS 版**的适配设计：同一代码库、同一领域核（Core / Core.Services / Infrastructure 零修改复用），新增 Avalonia UI 与 Outlook for Mac（经典版界面）AppleScript 直读通道。

**成功标准**（对齐总控指令）：

1. 回归红线：既有 210 个测试在每次合并前全绿，Windows 版功能与界面行为不被改变。
2. 可安装的 .app 与 DMG：Outlook for Mac 直读、定时增量同步、自动分类、P0–P3 标记、菜单栏常驻、通知，与 docs/02 现行需求（FR-04 起）对齐。
3. PROGRESS.md Mac 阶段任务块（MS0–MS10）为唯一进度事实源。

**范围外**：Windows 版新功能（WPF 转入维护模式，见 ADR-006）；Graph/IMAP/MSAL 通道恢复（CHG-013 已否决）；macOS 自动更新（列后续，见 §11）。

## 2. 架构决策记录（ADR）

### ADR-006 Mac 版 UI 采用 Avalonia 11 + 共享 ViewModel；WPF 版冻结为维护模式（2026-10-02）

- **背景**：需在 macOS 提供与 Windows 版同构的三栏界面（05 §3.2），且不推倒已有的 MVVM 业务逻辑。
- **决策**：
  1. 新建 `MailHelper.App.Avalonia`（net8.0 + Avalonia 11.x，Fluent 主题，浅深色跟随系统 `ThemeVariant.Default`）。
  2. App 内可复用 ViewModel 迁入共享工程 `MailHelper.ViewModels`（net8.0），WPF 与 Avalonia 两套 UI 共用同一套 ViewModel；迁移必须是**等价重构**（行为不变，由既有测试与截图基线守护）。
  3. WPF 应用（`MailHelper.App`）转入**维护模式**：只修缺陷，不加功能；其回归由既有 FlaUI UI 冒烟与全量测试守护。
- **理由**：Avalonia 11 为成熟跨平台 .NET UI 框架（MIT），复用 C#/MVVM 技能栈与既有 ViewModel 逻辑，避免 Electron/Tauri 双语言栈（03 §3.1 同款论证）；单一共享 ViewModel 保证两平台行为一致性可测试。
- **代价**：两套 UI 并存增加构建矩阵与维护面；Avalonia 的部分 WPF 生态件（WebView2、H.NotifyIcon、WinRT Toast）无直接等价物，需平台替代（见 §4/§5）。

### ADR-007 Mac 唯一通道 = Outlook for Mac（经典版界面）AppleScript 直读（2026-10-02）

- **背景**：Mac 版与 Windows 版同理念——依赖用户已登录的本机 Outlook，完全绕开租户 OAuth（CHG-013 动机在 macOS 同样成立：学校租户普遍禁止用户对第三方应用授权）。
- **决策**：新建 `OutlookMacMailProvider : IMailProvider`（`Kind = ChannelKind.OutlookMac`），位于 Infrastructure，内部经 `osascript` 进程执行 AppleScript 直读已登录的 Outlook for Mac（经典版界面）。领域侧契约与 Windows 通道完全一致（`FetchDeltaAsync` / `CompleteAsync` / `TestAsync` / `GetAccountAddressAsync`）。
- **已知硬限制——New Outlook for Mac 不支持 AppleScript**：新版 Outlook for Mac 未暴露邮件 AppleScript 字典，本通道在「新 Outlook」模式下不可用。检测方法与用户引导见 §6.4。
- **理由**：零授权环节、零令牌存储、不受租户策略限制，与 ADR-005 完全同构；AppleScript 为 macOS 自带能力，符合零原生依赖原则（ADR-008）。
- **代价**：依赖 Outlook for Mac 本机在线可用且处于经典版界面；AppleScript 字典无微软官方文档，字段名存在版本漂移风险（缓解：§6.5 防御解析 + 检查点②真机核验）；远端删除/移动暂不感知（与 Windows 通道一致，v1 接受）。

### ADR-008 零原生依赖原则（2026-10-02）

- **决策**：Mac 实现优先采用**纯托管代码 + 系统自带工具**，不引入 Microsoft.macOS / Xamarin.Native / 手写 Objective-C 原生工作负载：
  - AppleScript 经 `osascript` 进程调用（脚本为资源文件，可单测）；
  - 自启动用 `~/Library/LaunchAgents` plist + `launchctl`；
  - DMG 用 `hdiutil` 制作，.app 组装与 icns 用脚本（`iconutil`/`sips`）；
  - 单实例用 Unix domain socket 文件锁；托盘用 Avalonia 内置 `TrayIcon`。
- **例外流程**：确需原生 API 时（如通知署名、通知点击回传），须单独评估并在本文档记录理由与权衡，经变更提案批准后方可引入。
- **理由**：无原生编译链（无需 Xcode 工作负载）即可在 CI 与 Windows 上交叉开发；签名/公证链路简单；维护面最小。
- **代价**：个别系统体验让步（通知署名与点击回传，见 §5.2/§5.3），均有记录与后续路径。

## 3. 平台差异矩阵（Windows → macOS 全景）

| # | 维度 | Windows 现状（代码位置） | Mac 方案 | 承载层 |
| --- | --- | --- | --- | --- |
| P-01 | 邮件通道 | Outlook COM 直读（`OutlookComMailSource`，`[SupportedOSPlatform("windows")]`，纯晚绑定无 PIA） | `OutlookMacMailProvider`：osascript + AppleScript 资源（§6） | Infrastructure |
| P-02 | 数据/日志/正文缓存路径 | `%AppData%\MailHelper`（`Bootstrapper.cs:31` 与 `App.xaml.cs:134` 重复两处）+ `%TEMP%` 崩溃日志/阅读副本 | `~/Library/Application Support/MailHelper`；临时文件用 `Path.GetTempPath()` | 抽象 `IAppPaths`（§7），两平台各自实现 |
| P-03 | 单实例 | 命名 Mutex + 命名管道唤起（`App/SingleInstance.cs`，含 `NamedPipeServerStreamAcl` 纯 Windows API） | Unix domain socket 文件锁（socket 落数据目录，随 `MAILHELPER_DATA_DIR` 隔离）；二次启动连 socket 发 `SHOW` | 抽象 `ISingleInstanceLock`（§7） |
| P-04 | 开机自启动 | 注册表 `HKCU\...\CurrentVersion\Run`（`Infrastructure/SystemIntegration/AutostartService.cs`） | `~/Library/LaunchAgents/com.mailhelper.app.plist`（`RunAtLoad`，`launchctl bootstrap/bootout gui/$UID`） | 抽象 `IAutoStarter`（§7） |
| P-05 | 通知（P0/P1） | WinRT Toast（`App/Notifications/ToastSender.cs`，Toolkit.Uwp.Notifications；点击深链直达） | `osascript display notification`（§5.2 结论：署名 Script Editor、无点击回传——两处系统限制均有记录） | 既有 `IToastSender`（Core），Avalonia 侧新实现 |
| P-06 | 托盘/菜单栏 | H.NotifyIcon.Wpf `TaskbarIcon` + WPF 渲染角标（`App/Notifications/TrayIconController.cs`） | Avalonia 内置 `TrayIcon`（NSStatusItem）+ `NativeMenu`；角标=运行时 `RenderTargetBitmap` 合成计数图标 | Avalonia App 层 |
| P-07 | 阅读窗格 | WebView2 沙箱禁脚本，HTML 注入 `%TEMP%` 副本后 `Navigate(file://)` | **净化 HTML → 结构化文本渲染**（v1 结论，评估记录见 §5.1）；正文 HTML 仍落盘缓存，「在 Outlook 中打开」等效入口 | Avalonia App 层（净化器为纯托管，入 Infrastructure） |
| P-08 | 更新器 | 查版（GitHub Releases）+ Inno 静默重装（`App/Updates/UpdateService.cs`） | 查版逻辑复用（共享化）；Mac = 引导下载对应架构 DMG 手动安装；自动更新列后续（§11） | 查版共享化 + 抽象 `IUpdateInstaller`（§7） |
| P-09 | UI 框架 | WPF（net8.0-windows10.0.17763.0） | Avalonia 11（net8.0，Fluent，浅深色跟随系统） | `MailHelper.App.Avalonia` |
| P-10 | 主线程调度 | `Dispatcher.CurrentDispatcher`（`MainViewModel` 5 处） | 注入式主线程调度器抽象 `IMainThreadDispatcher`；WPF/Avalonia 各自包装本平台 Dispatcher | 共享 ViewModel 工程（§7） |
| P-11 | UI 自动化测试 | FlaUI/UIA（`UiSmokeTests`，拉起真实 WPF 进程） | Avalonia.Headless.XUnit headless 测试（关键交互）；真机走检查单 | 各测试工程 |
| P-12 | 打包/分发 | Inno Setup（`installer/MailHelper.iss`）+ SHA256 清单 | .app bundle + DMG（hdiutil）+ codesign/notarytool（有证书）/ 无证书 Gatekeeper 指引（§9） | `tools/mac/` 脚本 + CI macos job |
| P-13 | CI | windows-latest 单矩阵（build+test+门禁 / release） | 新增 macos job（osx-arm64 + osx-x64：build、test、publish、组 .app 与 DMG）（§10） | `.github/workflows/ci.yml` |

**明确不变的复用面**（零修改红线）：领域模型、RuleEngine、分类管线、SyncCoordinator、FeedbackService、NotificationService（决策层）、SearchService、EF Core 仓储与 FTS5、规则 JSON Schema、Serilog 配置（目录参数注入式）、`FakeMailProvider`/`DevSeed`。

## 4. 需要抽象的平台接口清单（以 2026-10-02 代码盘点为准）

> 盘点方法：全仓检索平台专有 API（详见 CHG-014 提案说明）。定义一律放 `MailHelper.Core/Abstractions`（纯签名、零平台 API）；实现放 Infrastructure（可共享）或各自 App（UI 专有）。

| 接口 | 职责（成员为 MS1 定稿签名） | Windows 实现 | Mac 实现 | 现状 |
| --- | --- | --- | --- | --- |
| `IAppPaths` | `DataDir` / `LogsDir` / `BodiesDir` / `DbPath`（属性）+ `GetTempFilePath(name)` | `WindowsAppPaths`：%AppData%\MailHelper（保留 `MAILHELPER_DATA_DIR` 覆盖语义，产出路径与迁移前硬编码逐字节一致） | `MacAppPaths`：`~/Library/Application Support/MailHelper`（同覆盖语义，MS4） | **已落地（MS1）**：消灭 Bootstrapper/App.xaml.cs 双处硬编码 |
| `ISingleInstanceLock` | `TryAcquireFirst()` / `NotifyRunningInstance()` / `StartListening(activate)` + `IDisposable`（Dispose=释放互斥并停止监听） | `WindowsSingleInstanceLock`：Mutex + ACL 命名管道（自 App/SingleInstance.cs 等价迁移；mutexName/pipeName 可注入供契约测试隔离） | `MacSingleInstanceLock`：UDS 文件锁 + `SHOW` 协议（MS4） | **已落地（MS1）**；已知语义边界：Windows 命名互斥同线程可重入，「第二实例失败」以跨进程为前提 |
| `IAutoStarter` | `IsEnabled`（属性）/ `Enable()` / `Disable()` | `AutostartService` 实现接口（注册表 Run 键，逻辑零改动） | `MacAutoStarter`：LaunchAgent plist + `launchctl`（MS4） | **已落地（MS1）** |
| `IUpdateInstaller` | `Install(localPackagePath)`（同步，Process.Start 即返） | `WindowsUpdateInstaller`：Inno `/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS`（参数与 S17 逐字一致） | `MacUpdateInstaller`：引导下载 DMG 手动安装（MS9） | **已落地（MS1）**：UpdateService.InstallSilently 改为策略委托 |
| `IMainThreadDispatcher` | `Post(Action)` / `InvokeAsync(Func<Task>)`（MS2 定稿） | 包装 `System.Windows.Threading.Dispatcher` | 包装 `Avalonia.Threading.Dispatcher` | **MS2**：解除 MainViewModel 对 WPF Dispatcher 的依赖 |
| `IToastSender`（已有） | `SendAsync(ToastNotification, ct)` | 现 `ToastSender`（WinRT，留在 WPF App） | `OsascriptToastSender`（Avalonia App，经 osascript 进程） | 接口已存在（Core/Abstractions/Notifications.cs） |
| `IOutlookMailSource`（已有，Infrastructure 内部） | COM 源抽象（可测试替换点） | `OutlookComMailSource` | `OutlookAppleScriptSource`（同接口语义：枚举摘要、按 id 取正文、连接探测，MS4 定稿） | 接口已存在（`OutlookDesktopMailProvider.cs:10`） |
| AppleScript 执行器（新增内部抽象） | `RunAsync(scriptName, args, timeout, ct) → (exitCode, stdout, stderr)` | —（Windows 侧无实现需求） | `OsascriptScriptRunner`（`osascript` 进程）+ 假实现（测试） | **MS4**：AppleScript 全部封装在 Infrastructure Mac 通道内，Core/Core.Services 零 AppleScript 字符串 |

> 通知与托盘按总控指令要求处置：`IToastSender` 已在 Core 定义（App 可共享位置），满足要求；托盘为纯 App 层控件（无跨 UI 共享价值），不设接口、两套 UI 各自持有。

## 5. 三项关键权衡结论（记录在案）

### 5.1 阅读窗格：v1 采用净化 HTML → 结构化文本渲染

**候选评估**（2026-10-02）：

| 候选 | 事实 | 结论 |
| --- | --- | --- |
| Avalonia 官方 WebView 控件（`AvaloniaUI/Avalonia.Controls.WebView`，macOS 走系统 WKWebView，MIT 已开源） | 开源时间线与版本线面向 Avalonia 11.3.16+/12；共享 Core「not published on NuGet」、经 ILRepack 合并，与 Avalonia 11.x 免费稳定版的可用组合缺乏可验证证据 | 暂不可控，**不采用**；列为后续单独评估项（外部条件变化时走变更提案） |
| 社区包 `WebView.Avalonia`（amerkoleci/MicroSugarDeveloperOrg，11.0.0.1，WKWebView） | net8 适配存在未决 issue；维护活跃度低 | 不可控 + 违背零原生依赖精神，**不采用** |
| **净化 HTML → 结构化文本渲染**（选定） | `TextNormalizer` 已有 HtmlAgilityPack 链路（HTML→文本、剔 script/style）；扩展为块级结构（段落/标题/列表/链接）纯托管渲染 | **v1 采用**：零新依赖、无脚本执行面、隐私上更优（不加载远程跟踪图片） |

**v1 行为定义**：正文经净化器转结构块渲染（SelectableTextBlock 链接可点击，经系统浏览器打开且仅限 http/https）；不渲染远程图片、脚本、样式与 iframe；正文 HTML 原文仍按现状落盘 BodyCache（`bodies/{accountId}/{sha1}.html`，供审计与「在 Outlook 中打开」）。**已知差异**（对照 Windows 版）：复杂排版/内嵌图片不还原——记录为平台已知差异，后续以官方 WebView 评估覆盖。安全结论：无 WebView 即无脚本执行面，09 章沙箱要求（FR-12 AC2）以更严格方式满足。

### 5.2 通知署名：v1 接受 osascript 的「脚本编辑器」署名

`osascript display notification` 在 macOS 中固定署名为「Script Editor（脚本编辑器）」，`display notification` 无署名参数；要自定义署名需真实 .app 身份 + 原生 `UNUserNotificationCenter`（原生工作负载，违反 ADR-008）或第三方 `terminal-notifier` 二进制（引入外部二进制，不采用）。

**v1 结论**：接受 Script Editor 署名；代价与引导（系统设置 → 通知 → 脚本编辑器 允许）写入 Mac 版使用手册与运维 FAQ（MS10）。通知开关、去重（`notification_log`）、P0 逐封/P1 聚合逻辑不受影响（决策层零改动）。后续升级路径（原生 UNUserNotificationCenter，署名正确 + 点击回传）在例外流程记录，暂不启用。

### 5.3 通知点击直达：v1 不可达，记录为已知差异

`display notification` 无点击回调（`activate` 动作不携带自定义负载且归属 Script Editor）。**Windows 版 FR-14 AC1「通知点击直达对应邮件」在 Mac v1 无法以零原生依赖方式满足**。v1 行为：通知仅提示（标题=主题截断、正文=摘要）；用户经菜单栏「打开 MailHelper」进入后定位。该差异随 CHG-014 一并提交批准；若批准后仍要求点击直达，则必须走原生例外流程，属范围变更。

## 6. AppleScript 通道设计（OutlookMacMailProvider）

### 6.1 原则

- 脚本一律为**资源文件**（`Infrastructure/Sync/AppleScript/*.applescript`，CopyToOutput），C# 侧零内联脚本字符串；
- 脚本经 `IAppleScriptRunner`（`osascript` 进程调用，可注入假实现）执行；单测用假 runner 验证**参数构造与输出解析**（正常/空邮箱/超时/权限被拒/New Outlook 探测/格式漂移全场景）；
- 防御解析：输出分隔符用 ASCII 单元分隔符 `0x1F`（字段间）与记录分隔符 `0x1E`（记录间）——邮件文本中不可预期出现；解析前防御性过滤值内两类字节；空值容忍（缺字段→空串/null，绝不抛原始异常给上层）；
- **单次查询限定返回量**（默认 ≤100 条/查询，参数可调），避免全量遍历大邮箱；超时（默认 30s/查询）经 runner 强制；
- 错误 `-1743`（errAEEventNotPermitted，自动化权限被拒）必须映射为引导用户去「系统设置 → 隐私与安全性 → 自动化」放行的文案（MAC-003）。

### 6.2 脚本清单与协议（v1 草案，字段名以检查点②真机核验为准）

| 脚本 | 入参（经 argv） | 输出（0x1E/0x1F 分隔） | 对应契约 |
| --- | --- | --- | --- |
| `probe_connection` | — | 账户数、首个账户 email、Outlook 运行态 | `TestAsync` |
| `list_recent` | lookbackHours、limit | 每条：id、subject、fromName、fromAddress、receivedEpoch、hasAttachment、isRead、plainText 预览（截断 ≤2000 字符） | `FetchDeltaAsync` |
| `fetch_body` | messageId | HTML content（全文） | 点开邮件按需取正文（对应 Windows 版 G-4 按需拉取语义） |
| `get_account` | — | 首个 Exchange 账户 email 地址 | `GetAccountAddressAsync` |

**字典字段映射草案**（Outlook for Mac 经典版 AppleScript 字典无官方文档，以下为社区验证过的用法；**MS4 以假 runner 锁协议，MS8 检查点②以真机核验后冻结**）：

| 契约字段 | AppleScript 草案 | 备注 |
| --- | --- | --- |
| 账户地址 | `email address of first exchange account` | IMAP/POP 账户同字典族 |
| 时间 | `(time received of m) as «class isot» as text` → ISO8601 | **不跨进程序列化日期字面量**（locale 敏感）；C# 侧转 UTC epoch |
| 发件人 | `name of sender of m`、`address of sender of m` | 失败回退：`sender of m` 整串解析 |
| 正文预览 | `plain text content of m`（截断） | 已知会偶发为空（社区报告）→ 空时回退 `content of m` 经净化器转文本 |
| 附件 | `has attachment of m`（回退：`count of attachments > 0`） | — |
| 已读 | `is read of m` | — |
| 增量过滤 | `messages of inbox whose time received > ((current date) - lookbackHours * hours)`，脚本内计数器限 limit | whose 性能风险见 §13 R-2 |

### 6.3 增量水位（deltaLink 字符串槽语义）

- 水位 = **已见最大 `receivedEpoch`（Unix 秒，UTC）**，序列化格式 `otm:1:<epoch>`（版本前缀为未来演进预留）；
- `FetchDeltaAsync` 收到 null/不可解析水位 → 视为首次全量：lookback 上限 30 天起步、分页推进（与 FR-05 全量语义对应；更早历史引导用户在 Outlook 内查阅）；
- **重叠窗口推进**：每轮 lookback = `now − watermark + 24h` 重叠（时钟漂移与乱序到达容错）；上限单次 7 天；配合幂等 upsert（`ProviderMessageId` 主键）保证 FR-04 AC2「重复同步不产生重复记录」；
- 推进规则：仅当本轮无异常时把水位前移至本轮最大 epoch；空轮保持不变；
- 持久化沿用 `sync_state.delta_link`（列结构零改动，`imap_uid_watermark` 同为存量兼容保留列）。

### 6.4 New Outlook 探测与用户引导

- **探测**：`probe_connection` 的邮件字典命令失败且 osascript 错误为「不理解该命令」族（-1708/-1709 等）→ 判定 MAC-004（新版 Outlook 不支持）；辅以非致命启发式（`defaults read com.microsoft.Outlook` 相关键），启发式仅用于引导文案精化，不单独作为判定（检查点②核实后冻结）。
- **引导文案**（zh-CN，en 同步提供）：「检测到新版 Outlook for Mac。MailHelper 需要『经典版 Outlook』界面：请打开 Outlook → 顶部菜单『Outlook』→ 勾选『旧版 Outlook』（Legacy Outlook）→ 等待 Outlook 重启后，在 MailHelper 中点击『重试』。」
- 该限制写入使用手册与运维 FAQ（MS10），Onboarding/连接页错误态展示同款文案。

### 6.5 错误映射（TestAsync 四类 + 运行期）

| 内部码 | 场景 | osascript 信号 | 用户可见行为（映射文案） |
| --- | --- | --- | --- |
| MAC-001 | Outlook 未运行 | `running` = false / 进程启动错误 -600 | 「请先启动 Outlook 并登录学校邮箱」 |
| MAC-002 | 未登录/无账户 | 账户数 = 0 | 「未在 Outlook 中检测到已登录账户，请先在 Outlook 登录」 |
| MAC-003 | 自动化权限被拒 | 退出码 -1743 | 「需要授权 MailHelper 控制 Outlook：系统设置 → 隐私与安全性 → 自动化 → 勾选 Outlook」（TCC 弹窗未弹出/被拒两态同文案） |
| MAC-004 | New Outlook 不支持 | -1708/-1709 族 | §6.4 引导文案 |
| MAC-005 | 超时/格式漂移/其他 | 超时、解析失败、非零退出 | 「连接 Outlook 异常，请重试；若持续出现请开启诊断日志」（错误态可重试，SYNC 语义） |

映射为既有 `ConnectionTestResult`（`Fail(code, message)`）与 `SyncState.Error`；日志遵循脱敏红线（主题截断 40 字符+指纹、发件人仅域名、正文/令牌绝不落日志——04 §6 不变）。

### 6.6 与 SyncCoordinator 的装配

`Bootstrapper` 两态装配扩展为三态（平台分支）：DEV（假通道，**可注入 OutlookMac 假通道供 UI 测试**）/ 真实通道按操作系统选择 `OutlookDesktopMailProvider`（Win）或 `OutlookMacMailProvider`（macOS）。`ChannelKind` 追加 `OutlookMac` 值：

```csharp
public enum ChannelKind
{
    Graph,            // 存量兼容保留值（CHG-013 后不产生新记录）
    Imap,             // 存量兼容保留值（同上）
    OutlookDesktop,   // CHG-011（Windows）
    OutlookMac,       // CHG-014（macOS，本文档）
}
```

**存量数据兼容策略**：只追加不重排；Mac 版全新安装产生全新本地库，仅写 `OutlookMac`；Graph/Imap 仅为老库可读性保留。`accounts.channel`/`sync_state` 表结构零改动。`GetAccountAddressAsync` 由 Mac 通道实现（现 default 抛 NotSupportedException 的语义仅约束「非桌面登录态通道」，Mac 同为登录态通道，覆盖实现属接口预期用法）。

## 7. 工程结构变更

```
mail_helper/
├── src/
│   ├── MailHelper.App/              # WPF（维护模式；MS1/MS2 仅等价重构）
│   ├── MailHelper.ViewModels/       # 新增：共享 ViewModel（net8.0，CommunityToolkit.Mvvm）
│   ├── MailHelper.App.Avalonia/     # 新增：Avalonia 11 入口（net8.0，WinExe 等价，Fluent）
│   ├── MailHelper.Core/             # 不动（Abstractions 增平台接口定义）
│   ├── MailHelper.Core.Services/    # 不动
│   └── MailHelper.Infrastructure/   # + Mac：Sync/AppleScript/*、SystemIntegration Mac 实现、净化器
├── tests/
│   ├── （既有三个测试工程不动）
│   ├── MailHelper.Mac.Tests/        # 新增：假 osascript 全场景 + Mac 平台实现契约测试（net8.0）
│   └── MailHelper.App.Avalonia.Tests/ # 新增：Avalonia.Headless.XUnit（net8.0）
├── tools/
│   └── mac/                         # 新增：bundle/dmg/icon 脚本 + 真机检查单脚本（MS4 起递进）
└── MailHelper.Mac.slnf              # 新增：solution filter（排除 WPF App 与 net8.0-windows 集成测试）
```

- **边界红线**（违反即返工）：`Core` 与 `Core.Services` 不出现任何平台专有 API、无条件编译、无 COM interop、无 AppleScript 字符串；AppleScript 只允许存在于 Infrastructure 的 Mac 通道实现内；`MailHelper.App.Avalonia` 不引用 `MailHelper.App`（WPF）；两套 UI 共用 `MailHelper.ViewModels`；CI 以机械检索守护（MS0 起纳入流水线）。
- `MailHelper.App.Avalonia` 包：Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent（11.x 最新稳定版，MS0 定版并记录）；不引入第三套 UI 框架；字体走系统栈（Win: Segoe UI/微软雅黑；mac: 系统中文栈），05 §5.3 字号/行高/间距体系照搬。

## 8. 平台实现要点（逐项）

1. **数据路径**：`~/Library/Application Support/MailHelper/`（db、logs/、bodies/、rules JSON 同构布局）；`MAILHELPER_DATA_DIR` 覆盖语义两平台一致（测试隔离与一键清除依赖它）。
2. **单实例**：UDS 文件锁 `$DATA_DIR/instance.sock`——`bind` 成功即首实例并监听；二次启动 `connect` 发 `SHOW` 后退出 0； socket 随数据目录隔离，Mac 侧测试不与用户已开实例冲突（Windows 版 UiSmoke 的全局 Mutex 脆弱性在 Mac 设计上规避）。
3. **自启动**：`~/Library/LaunchAgents/com.mailhelper.app.plist`（`ProgramArguments` 指向 .app 内可执行文件、`RunAtLoad=true`）；启用=`launchctl bootstrap gui/$(id -u)`，停用=`bootout` + 删 plist；设置页开关语义与 Windows 一致（默认关）。
4. **通知**：见 §5.2/§5.3；`OsascriptToastSender` 实现 `IToastSender`，按邮件 ID 去重逻辑不变（NotificationService 决策层复用）。
5. **菜单栏常驻**：Avalonia `TrayIcon` 常驻 + `NativeMenu`（打开/立即同步/通知开关/设置/退出，对齐 05 §3.5）；角标=计数合成图标；关主窗常驻行为与 FR-14 AC3 一致。
6. **阅读窗格**：见 §5.1。
7. **主题**：Fluent + `ThemeVariant.Default` 跟随系统；05 §5.1 色卡逐值映射资源字典（P0 `#D13438`、P1 `#F7630C`、P2 `#0078D4`、P3 `#8A8886`；深色板按 05 同比例提亮）。
8. **代码风格**：与 Windows 版一致——nullable 全开、async 全链路 + CancellationToken 贯穿、禁 `.Result`/`.Wait()`（注意：现 `LoadRuleEngine` 的 `GetAwaiter().GetResult()` 为既有启动期同步上下文特例，不扩散）、Serilog 结构化 + 脱敏红线。

## 9. 打包与分发（MS9）

- **发布命令**：`dotnet publish src/MailHelper.App.Avalonia -c Release -r osx-arm64`（及 `osx-x64`），自包含（`SelfContained=true`，net8 支持 macOS 12+，`LSMinimumSystemVersion=12.0`）。
- **.app 组装**（`tools/mac/bundle.sh`）：`Contents/MacOS/`（发布产物）、`Contents/Info.plist`（CFBundleName/Identifier `com.mailhelper.app`/Executable/IconFile/PackageType APPL/NSHighResolutionCapable/LSMinimumSystemVersion）、`Contents/Resources/MailHelper.icns`；icns 经 `iconutil`（PNG set 由 `sips` 从既有图标源生成）。
- **DMG**（`tools/mac/make-dmg.sh`）：staging 目录（MailHelper.app + /Applications 符号链接）→ `hdiutil create -volname MailHelper -format UDZO`。
- **签名/公证**（检查点③，有证书时）：`codesign --deep --force --options runtime` → `notarytool submit`（凭据只走 CI Secrets）→ `stapler staple`；无证书路径：未签名 DMG + Gatekeeper 首启指引（右键打开 / 系统设置放行），不阻塞发布（对齐 08 §4.4 无证书策略）。
- **更新器 Mac 策略**：查版逻辑共享复用（GitHub latest release 比较）；Mac 资产命名 `MailHelper-<channel>-osx-<arch>.dmg`；发现新版本 → 设置页提示 + 「前往下载」（系统浏览器打开 Release 页/直链 DMG），用户手动安装。自动更新（下载校验+替换+重启）列后续（§11）。

## 10. CI 变更（MS0 落地，先 build+test+publish，不做签名）

```yaml
jobs:
  build-test:            # 既有 windows job 不动（全 sln：Windows 回归 + 覆盖率门禁 + 平台边界机械检索）
  macos-build-test:      # 新增，needs 无（并行）
    strategy:
      matrix: { arch: [osx-arm64, osx-x64] }
    runs-on: macos-14    # arm64 物理机；osx-x64 走交叉编译（发布目标不影响测试架构语义）
    steps:
      - build: dotnet build MailHelper.Mac.slnf -c Release   # 排除 WPF App 与 net8.0-windows 测试
      - test:  dotnet test MailHelper.Mac.slnf -c Release    # Core/Services/Mac.Tests/Avalonia.Tests 真机执行
      - publish: dotnet publish ... -r ${{ matrix.arch }}    # MS0 产物即为打包可行性证据
      - (MS9 追加) bundle.sh / make-dmg.sh → 上传 artifact；有证书时签名公证
```

- macos job 同时承担：Mac 平台实现契约测试、假 osascript 全场景测试的真机执行、以及**真实 osascript 错误路径冒烟**（CI mac 机器无 Outlook → 验证 MAC-001/未安装路径的文案与退出语义）。
- Windows job 保持既有语义；新增工程进入全 sln 构建（Windows 上同样编译/测试），覆盖率门禁对象不变（`MailHelper.Core`）。

## 11. 显式排除与后续演进

| 项 | 状态 | 依据 |
| --- | --- | --- |
| macOS 自动更新（后台下载+替换） | 排除（后续版本） | 引导下载已满足可用性；避免自更新半途失败的复杂状态机（对照 Velopack 退役教训） |
| 通知原生署名/点击直达（UNUserNotificationCenter） | 排除（ADR-008 例外流程待评估） | §5.2/§5.3 |
| 官方 Avalonia WebView 阅读窗格 | 排除（后续评估） | §5.1 |
| 远端删除/移动感知 | 排除（与 Windows 通道一致） | ADR-005 代价条款 |
| Graph/IMAP/MSAL 通道恢复 | 永久排除（除非外部条件实质变化 + 变更提案） | CHG-013 |

## 12. 测试资产要求（随代码交付）

1. **回归红线**：既有 210 测试不得删除或削弱；合并前全量绿附证据。
2. **平台契约测试**：`IAppPaths`/`ISingleInstanceLock`/`IAutoStarter`/`IUpdateInstaller` 同一组契约用例，Windows 实现本地跑、Mac 实现在 CI macos job 真机跑（非 macOS 运行时以运行期探测跳过并注明原因，Windows 全量保持绿）。
3. **AppleScript 通道测试**（假 osascript，全平台可跑）：正常、空邮箱、超时、-1743 权限、New Outlook 探测、增量水位推进、格式漂移（缺字段/分隔符污染/空输出）。
4. **Avalonia headless 测试**：三栏导航、列表选择与过滤、改判入口、设置开关、双语资源渲染。
5. **真机检查单脚本化**：`tools/mac/real-mac-checklist/`（核对表 + 逐项探测脚本），随 MS4/MS6/MS8/MS9 递进，MS10 汇总为可执行核对包（见 §14）。

## 13. 风险登记（Mac 特有）

| 编号 | 风险 | 概率/影响 | 缓解 |
| --- | --- | --- | --- |
| R-1 | AppleScript 字典字段与草案不符（无官方文档，版本漂移） | 中/高（通道不可用） | 协议先行（假 runner 锁 C# 侧）；检查点②真机核验冻结；字典探针脚本随检查单交付；字段访问全部经「首选+回退」双路径 |
| R-2 | 大邮箱 `whose time received` 查询慢 | 中/中 | 单查询限 limit + lookback 窗口；检查点②实测；劣化则回退「取最新 N 条 + 本地过滤」策略（协议不变，仅脚本实现换） |
| R-3 | `plain text content` 偶发为空 | 中/低 | 回退 `content` 经净化器转文本（§6.2）；预览为空的邮件仍可入库（预览空串合法） |
| R-4 | New Outlook 成为部分用户默认 | 中/高（用户不可用） | 探测 + 引导文案（§6.4）；使用手册/FAQ 显著说明 |
| R-5 | Gatekeeper 拦截未签名 DMG | 高/中 | 首启指引图文（右键打开）；检查点③有证书则公证消除 |
| R-6 | TCC 自动化权限被用户拒绝且弹窗不再出现 | 中/中 | MAC-003 文案引导系统设置手动放行；检查单含「拒绝→放行」往返用例 |
| R-7 | 通知署名/点击差异引发用户困惑 | 低/低 | FAQ 说明；后续原生路径（§5.2/§5.3） |

## 14. 真机验证检查单（检查点②执行；MS10 汇总为核对包）

> 前置：一台真实 Mac（已装 Outlook for Mac 并以学校账户登录**经典版界面**）。以下各项只能在真机验证，禁止以「Windows 上跑过」宣称完成。

| # | 检查项 | 通过标准 | 对应脚本 |
| --- | --- | --- | --- |
| 1 | DMG 首启（未签名） | 下载→挂载→拖入 Applications→右键打开放行→主界面出现 | `check-dmg-firstlaunch.sh` + 人工 |
| 2 | TCC 自动化权限 | 首次连接弹「MailHelper 想要控制 Outlook」→放行后连接成功；拒绝态出现 MAC-003 文案 | `check-tcc.sh`（构造拒绝态）+ 人工 |
| 3 | 真实登录态读取 | 设置页显示真实账户地址（`get_account`）；四类错误文案逐一触发比对（未开 Outlook/未登录/拒权/New Outlook） | `check-channel-errors.sh` |
| 4 | 真实邮箱增量同步 | 收一封新邮件 ≤1 同步周期出现在列表且分类正确；重复同步无重复记录；水位文件单调推进 | `check-sync-incremental.sh` |
| 5 | 通知 | P0/P1 触发系统通知（署名为脚本编辑器属已知差异）；开关与去重生效 | `check-notification.sh` + 人工 |
| 6 | 菜单栏常驻与角标 | 关主窗后菜单栏驻留；未读角标计数变化；菜单各项可用 | `check-tray.sh` + 人工 |
| 7 | 开机自启 | 勾选后 plist 存在且 `launchctl` 已注册；重启后应用拉起；取消后不再拉起 | `check-autostart.sh` |
| 8 | 全量同步与搜索 | 首次全量完成（进度可见）；FTS 中英文检索命中 | `check-fullsync-search.sh` |
| 9 | 浅深色跟随 | 系统切换外观，界面跟随 | 人工 |
| 10 | 卸载清除 | 「清除本地数据」后数据目录清空；删 .app 后无残留 LaunchAgent | `check-uninstall.sh` |

## 15. 模块顺序与本文档映射

| 里程碑 | 内容 | 本文档依据 |
| --- | --- | --- |
| MS0 | docs/10 获批；CI macos job（build+test+publish）；mac-baseline tag；feature/mac-platform 分支 | §7/§10 |
| MS1 | 平台抽象抽取（§4 清单）+ Windows 等价迁移 + DI 改造；Windows 回归绿 + 运行冒烟 | §4 |
| MS2 | 共享 ViewModel（含 `IMainThreadDispatcher`、阅读 HTML 构建共享化）；合回 main | §4/ADR-006 |
| MS3 | Avalonia 骨架（Shell/导航/主题/双语）；Windows 上运行截图比对 05 章 | §7/§8.7 |
| MS4 | Mac 基础设施（IAppPaths/单实例/自启动）+ OutlookMacMailProvider 骨架（假 osascript 先行） | §6/§8 |
| MS5 | Avalonia 三栏主界面（虚拟化列表/徽章色卡/四态/阅读窗格按 §5.1） | §5.1/§8.7 |
| MS6 | 通知与菜单栏（TrayIcon/角标/去重/关窗常驻） | §5.2/§5.3/§8.5 |
| MS7 | 功能对齐（待确认队列/反馈闭环/规则编辑器/设置/FTS 搜索） | 复用 Windows 行为 |
| MS8 | AppleScript 通道打磨（水位/探测/错误文案/装配接入） | §6 |
| MS9 | 打包分发（.app/DMG/icns/签名脚本/CI 集成/更新器 Mac 策略） | §9 |
| MS10 | 发布对齐（08 章 mac 变体检查单、FAQ mac 节、SHA256、核对包汇总） | §12/§14 |
