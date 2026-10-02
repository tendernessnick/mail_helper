<div align="center">

<img src="docs/assets/logo.png" width="96" alt="MailHelper" />

# MailHelper 邮箱管家

让港校邮箱里的大事小事，各归其位。

[![CI](https://github.com/tendernessnick/mail_helper/actions/workflows/ci.yml/badge.svg)](https://github.com/tendernessnick/mail_helper/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/tendernessnick/mail_helper)](https://github.com/tendernessnick/mail_helper/releases/latest)
[![平台](https://img.shields.io/badge/平台-Windows%2010%20%7C%2011-0078D4)](#前提)
[![许可证](https://img.shields.io/badge/许可证-MIT-blue.svg)](LICENSE)

[下载最新版](https://github.com/tendernessnick/mail_helper/releases/latest) · [使用手册](docs/使用手册.md) · [问题反馈](https://github.com/tendernessnick/mail_helper/issues) · [更新日志](CHANGELOG.md)

</div>

## 前提

MailHelper 自己不做邮箱登录。它读取你电脑上**经典版 Outlook**（Office 桌面版自带的那个 Outlook）里已登录的学校账户来同步邮件，所以：

- 需要先装 Office 桌面版，并在经典版 Outlook 里配置好学校邮箱
- 新版 Outlook for Windows 和网页版 Outlook 用不了
- 不需要 OAuth 授权，也不用找学校 IT 开权限——Outlook 能收发邮件，它就能同步

## 界面

![主界面](artifacts/screens/s6-inbox.png)

## 功能

- 每 5 分钟增量同步一次新邮件（间隔可调 1–60 分钟），`Ctrl+R` 随时手动同步
- 邮件自动分进课程学习、职业发展、校园事务、财务缴费、通知公告、订阅营销、其他七类。规则分不准的会放进「待确认」等你处理，不会默默分错
- 每封邮件标注 P0（紧急）到 P3（低）的重要度，缴费截止、面试邀约这类一眼能看到
- 新邮件通知：P0 每封单独弹窗，P1 三封以上合并成一条摘要，P2/P3 不弹；支持勿扰时段，点通知直达邮件
- 分错了选中邮件点「改为…」就行，同发件人以后自动归入你选的分类；规则页也可以自己写规则，保存前能先试跑
- 主题/发件人/正文全文搜索（`Ctrl+F`），配合「仅未读」清邮件
- 分类不够用可以在设置里自建类别（名称 + 图标 + 颜色）
- 关窗后缩到托盘继续同步，图标带未读数，可设开机自启
- 设置里点「检查更新」，后台增量下载，点一下重启就装好

## 安装

1. 到 [Releases 最新正式版](https://github.com/tendernessnick/mail_helper/releases/latest) 下载 `MailHelper-stable-Setup.exe`，双击安装
2. 打开 MailHelper，勾选隐私说明，点「连接学校邮箱」，等首轮同步完成就能用了

要求 Windows 10 (19041+) 或 Windows 11。不用装 .NET（安装包自带）；WebView2 Runtime 系统一般都有，个别精简版 Win10 缺的话从[微软官网](https://developer.microsoft.com/microsoft-edge/webview2/)补。

程序没买代码签名证书，首次运行 SmartScreen 会拦一下，点「更多信息 → 仍要运行」即可；介意的话先对照发布页 `SHA256SUMS.txt` 里的校验值。不想安装的话，同页也有免安装的 Portable zip，但更新要自己重新下载。

安装器是一键式的，没有选目录的向导：默认装到 `%LocalAppData%\MailHelper`（当前用户目录，不需要管理员权限）。想装到别的位置，用命令行 `MailHelper-stable-Setup.exe --installto "D:\Apps\MailHelper"`，或者直接用便携版。

## 更新

设置 → 关于 → 检查更新。有新版本会自动在后台下载，点「重启并安装更新」完成升级，邮件数据和设置都保留。只会收到正式版。

## 隐私

邮件数据、分类结果、规则、设置全部存在本机 `%AppData%\MailHelper\`。没有遥测，分类在本地完成，应用也不保存任何账号凭据——同步用的就是 Outlook 自己的登录状态。阅读窗格里的外链不可点击（防钓鱼），要打开链接请去 Outlook。

## 从源码构建

```bash
dotnet restore MailHelper.sln
dotnet build MailHelper.sln -c Release
dotnet test MailHelper.sln -c Release
```

需要 .NET 8 SDK 和 Windows。设 `MAILHELPER_DEV=1` 启动可以用假数据跑完整界面，不用连真邮箱。

## 路线图

- 感知远端的删除和移动（目前在网页版删掉的邮件不会从本地消失）
- 更多港校的分类规则包
- 英文界面补全

有问题、有想法，欢迎[提 Issue](https://github.com/tendernessnick/mail_helper/issues)；想改代码的话请先开 Issue 对齐方向。

## 许可证

[MIT](LICENSE)。用到的开源库：Velopack、CommunityToolkit.Mvvm、EF Core (SQLite)、Serilog、H.NotifyIcon.Wpf、WebView2。

觉得好用的话，给个 Star。

<details>
<summary>项目内部文档（工程书 / 约定 / 进度）</summary>

| 编号 | 文档 | 用途 |
| --- | --- | --- |
| 01 | [项目立项说明书](docs/01-项目立项说明书.md) | 背景、目标、范围、可行性 |
| 02 | [需求规格说明书](docs/02-需求规格说明书.md) | 用户故事、用例、功能/非功能需求 |
| 03 | [系统架构设计说明书](docs/03-系统架构设计说明书.md) | 总体架构、ADR、模块划分、分类引擎 |
| 04 | [详细设计说明书](docs/04-详细设计说明书.md) | 数据库、接口、类设计、错误处理 |
| 05 | [UI-UX 设计说明](docs/05-UI-UX设计说明.md) | 信息架构、界面原型、交互与视觉 |
| 06 | [测试计划](docs/06-测试计划.md) | 测试策略、用例、发布门禁 |
| 07 | [项目计划与风险管理](docs/07-项目计划与风险管理.md) | 里程碑、Sprint 排期、风险登记 |
| 08 | [构建发布与运维手册](docs/08-构建发布与运维手册.md) | 分支策略、CI/CD、打包签名 |
| 09 | [安全与合规](docs/09-安全与合规.md) | 威胁建模、数据安全、PDPO/GDPR |

其他：[发布操作指南](docs/发布操作指南.md)（维护者发版流程）· [工程约定与术语表](docs/工程约定与术语表.md) · 进度记录 [PROGRESS.md](PROGRESS.md)（S0~S16）

</details>
