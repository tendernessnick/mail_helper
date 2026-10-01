# MailHelper — 港校学生 Outlook 邮箱管理助手

> 一款运行于 Windows 的桌面应用程序，帮助港校学生自动同步、分类、标记学生邮箱（Office 365 / Outlook）中的邮件，按「类别 + 重要程度」组织收件箱，让重要的事不再被淹没。

[![CI](https://github.com/tendernessnick/mail_helper/actions/workflows/ci.yml/badge.svg)](https://github.com/tendernessnick/mail_helper/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/tendernessnick/mail_helper)](https://github.com/tendernessnick/mail_helper/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**工程代号**：`mail_helper`　|　**产品名（建议）**：MailHelper　|　**平台**：Windows 10 (19041+) / Windows 11

## ⬇️ 下载（Windows）

| 渠道 | 链接 | 说明 |
| --- | --- | --- |
| **安装版（推荐）** | [Releases 页最新正式版](https://github.com/tendernessnick/mail_helper/releases/latest) → `MailHelper-stable-Setup.exe` | 双击安装，支持应用内自动更新 |
| 便携版（免安装） | 同一发布页 → `MailHelper-stable-portable.zip` | 解压即用，更新需重新下载 |
| 尝鲜（beta） | [全部 Pre-releases](https://github.com/tendernessnick/mail_helper/releases) | 每次提交 main 自动产出，稳定性不作保证 |

安装后，应用内 **设置 → 关于 → 检查更新** 即可随时升级到最新正式版。详细说明见下文[下载、安装与更新](#下载安装与更新)。

---

## 一句话定位

学生在港校录取后会收到海量混杂邮件（课程、实习/CV、校园事务、缴费、通知……）。MailHelper 通过 Microsoft Graph API 定时增量同步邮箱，使用本地规则引擎自动分类并标注 P0–P3 重要程度，以「侧边分类栏 + 邮件列表 + 阅读窗格」的三栏界面呈现，支持托盘常驻后台运行。

## 技术栈（已评审确定）

| 层面 | 选型 | 说明 |
| --- | --- | --- |
| 客户端框架 | C# / .NET 8 + WPF（MVVM） | 原生 Windows，自包含发布 |
| 身份认证 | MSAL.NET（OAuth 2.0 + PKCE） | 微软官方认证库，令牌缓存 DPAPI 加密 |
| 邮件接入 | Microsoft Graph API（主）+ IMAP XOAUTH2（兜底） | delta query 增量同步，避免全量拉取 |
| 本地存储 | SQLite + EF Core 8 | 邮件缓存、规则、配置全部本地化 |
| 分类方案 | 本地规则引擎（发件人/关键词/正则加权评分） | 预留 `IClassifier` 扩展点，后期可接入 LLM |
| 打包发布 | dotnet publish 自包含 + Velopack + GitHub Actions | tag 自动发版（Setup.exe/便携版/增量更新），应用内检查更新 |

## 文档导航（软件工程项目工程书）

阅读顺序建议：01 → 02 → 03 → 05 → 04 → 07 → 06 → 08 → 09。

| 编号 | 文档 | 用途 | 主要读者 | 状态 |
| --- | --- | --- | --- | --- |
| 01 | [项目立项说明书](docs/01-项目立项说明书.md) | 背景、目标、范围、可行性、立项结论 | 全体 / 决策者 | v1.0 待评审 |
| 02 | [需求规格说明书](docs/02-需求规格说明书.md) | 用户故事、用例、功能/非功能需求 | 产品 / 开发 / 测试 | v1.0 待评审 |
| 03 | [系统架构设计说明书](docs/03-系统架构设计说明书.md) | 总体架构、技术选型（ADR）、模块划分、分类引擎设计 | 开发 | v1.0 待评审 |
| 04 | [详细设计说明书](docs/04-详细设计说明书.md) | 数据库、接口、类设计、错误处理、配置 | 开发 | v1.0 待评审 |
| 05 | [UI-UX 设计说明](docs/05-UI-UX设计说明.md) | 信息架构、界面原型、交互与视觉规范 | 开发 / 设计 | v1.0 待评审 |
| 06 | [测试计划](docs/06-测试计划.md) | 测试策略、用例、性能/安全测试、发布门禁 | 测试 / 开发 | v1.0 待评审 |
| 07 | [项目计划与风险管理](docs/07-项目计划与风险管理.md) | 里程碑、Sprint 排期、WBS、风险登记册 | 全体 / 项目经理 | v1.0 待评审 |
| 08 | [构建发布与运维手册](docs/08-构建发布与运维手册.md) | 分支策略、CI/CD、打包签名、排障手册 | 开发 / 运维 | v1.0 待评审 |
| 09 | [安全与合规](docs/09-安全与合规.md) | 威胁建模、令牌安全、PDPO/GDPR、微软平台政策 | 开发 / 安全 | v1.0 待评审 |

## 统一编号约定

全文档使用以下编号体系，便于交叉引用与需求追溯：

| 前缀 | 含义 | 示例 |
| --- | --- | --- |
| `US-xx` | 用户故事（User Story） | US-01 |
| `UC-xx` | 用例（Use Case） | UC-03 |
| `FR-xx` | 功能需求（Functional Requirement） | FR-02 |
| `NFR-xx` | 非功能需求（Non-Functional Requirement） | NFR-01 |
| `ADR-xx` | 架构决策记录（Architecture Decision Record） | ADR-001 |
| `RISK-xx` | 风险条目（Risk） | RISK-01 |
| `TC-xx` | 测试用例（Test Case） | TC-012 |
| `MOD-xx` | 系统模块（Module） | MOD-02 |

## 术语表

| 术语 | 全称 / 解释 |
| --- | --- |
| O365 | Microsoft Office 365，港校学生邮箱所在的云端服务 |
| Graph | Microsoft Graph API，微软统一的云端数据 REST 接口 |
| MSAL | Microsoft Authentication Library，微软官方身份认证库 |
| OAuth 2.0 / PKCE | 授权框架及其代码交换扩展，桌面应用推荐的安全授权方式 |
| delta query | Graph 的增量查询机制，通过 deltaLink 只拉取有变化的邮件 |
| XOAUTH2 | IMAP 使用 OAuth2 令牌进行 SASL 认证的机制 |
| MVVM | Model-View-ViewModel，WPF 推荐的界面架构模式 |
| DPAPI | Windows 数据保护 API，用于加密本机令牌缓存 |
| PDPO | 香港《个人资料（私隐）条例》 |
| 租户（Tenant） | 学校在微软云中的组织目录；租户策略可能限制第三方应用授权 |
| P0–P3 | 重要程度四级：P0 紧急 / P1 重要 / P2 普通 / P3 低 |

## 文档版本记录

| 版本 | 日期 | 作者 | 变更说明 |
| --- | --- | --- | --- |
| v1.0 | 2026-09-26 | 项目发起人 | 初稿，覆盖立项至运维全流程，待评审 |

## 构建与运行

```bash
dotnet restore MailHelper.sln
dotnet build MailHelper.sln -c Release      # 0 警告 0 错误（TreatWarningsAsErrors）
dotnet test MailHelper.sln -c Release       # 全部测试（含 UI 冒烟，需 Windows 桌面会话）
```

- **开发模式**（无需真实账号）：`MAILHELPER_DEV=1` 启动即用假令牌与种子邮件驱动完整链路（登录→同步→分类→浏览）。
- **数据目录**：`%AppData%\MailHelper\`（数据库/正文缓存/令牌/日志）；可用 `MAILHELPER_DATA_DIR` 覆盖。
- **本地打包**（与 CI 一致）：

```bash
dotnet publish src/MailHelper.App -c Release -r win-x64 -p:Version=0.4.0 -p:SelfContained=true -o artifacts/publish
dotnet tool install -g vpk
vpk pack --packId MailHelper --packVersion 0.4.0 --packDirectory artifacts/publish `
  --mainExe MailHelper.App.exe --icon src/MailHelper.App/Assets/app.ico --outputDir Releases
```

## 下载、安装与更新

### 获取最新版

所有正式版发布在 [GitHub Releases](https://github.com/tendernessnick/mail_helper/releases/latest)，`releases/latest` 链接永远指向最新正式版。每个发布随附：

- `MailHelper-stable-Setup.exe` — 安装版（推荐，支持自动更新）
- `MailHelper-stable-portable.zip` — 便携版（解压即用）
- `SHA256SUMS.txt` — 全部产物的 SHA256 校验清单

### 安装

双击 `Setup.exe` 即可，无需预装 .NET（自包含）。系统要求：Windows 10 (19041+) / Windows 11；WebView2 Runtime（Win11 内置，个别 Win10 精简系统缺失时从[微软官网](https://developer.microsoft.com/microsoft-edge/webview2/)安装）。

**SmartScreen 提示**：当前未做代码签名（08 §4.4 无证书路径），首次运行请选「更多信息 → 仍要运行」，并建议核对发布页 `SHA256SUMS.txt` 校验值。

### 应用内更新

已装用户：**设置 → 关于 → 检查更新**。发现新版本会自动后台下载（支持增量，通常只有几 MB），完成后点「重启并安装更新」即完成升级。应用只接收正式版；beta 预发布需手动从 Releases 页下载。

**注意**：v0.3.1 及之前由 Inno Setup 安装的旧版本不支持应用内更新，请从发布页重装一次 v0.4.0+ 完成迁移。

### 隐私

邮件数据仅保存在本机，分类完全本地完成，应用无任何遥测（09 章）。许可协议见 [LICENSE](LICENSE)（MIT），更新日志见 [CHANGELOG.md](CHANGELOG.md)。

## 仓库约定

- 分支策略与提交规范见 [08-构建发布与运维手册](docs/08-构建发布与运维手册.md)；发版流程与排障见 [发布操作指南](docs/发布操作指南.md)。
- 代码结构：`src/`（App/Services/Core/Infrastructure 四层）+ `tests/`（Core/Services/Integration 三测试工程）+ `tools/`（覆盖率门禁/长稳脚本）。
- 发布链路：push main 自动发 beta 预发布，打 tag `v*.*.*` 自动发正式版（CI：[.github/workflows/ci.yml](.github/workflows/ci.yml)）。
- 进度事实源：[PROGRESS.md](PROGRESS.md)（S0~S14 完成，S15 开源发布链路）。
