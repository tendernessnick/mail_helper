; MailHelper 安装包脚本（08 §4.1/4.2：Inno Setup 6；主推安装包）
; 默认安装 %LocalAppData%\Programs\MailHelper；内置 WebView2 引导（RISK-04 缓解）；
; 创建快捷方式与卸载项；卸载可选清除本地数据（SEC-06 / 08 §6.4）。
; 编译：ISCC installer\MailHelper.iss（产物 artifacts\installer\MailHelperSetup.exe）

#define MyAppName "MailHelper"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "MailHelper Project"
#define MyAppExeName "MailHelper.App.exe"

[Setup]
AppId={{C376962C-F203-4EC5-B480-0A602498E04C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=MailHelperSetup
OutputDir=..\..\artifacts\installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; 非管理员按用户安装（%LocalAppData%），无需提权
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 应用单文件（08 §2 publish 产物）
Source: "..\artifacts\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; 内置规则包（04 §7 随包分发只读层）
Source: "..\src\MailHelper.Infrastructure\Rules\rules.builtin.json"; DestDir: "{app}"; Flags: ignoreversion
; WebView2 离线完整包（约 187MB；编译前从官方链接下载至本目录：
; https://go.microsoft.com/fwlink/?linkid=2099617，08 §4.2）
Source: "..\installer\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedWebView2

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Run]
; 08 §4.2：WebView2 Evergreen 引导——检测注册表缺失则静默安装
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  Flags: waituntilterminated runhidden; Check: NeedWebView2
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载清理安装目录（用户数据在 %AppData%\MailHelper，由 [Code] 询问后处理）

[Code]
const
  WebView2RegKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function NeedWebView2(): Boolean;
begin
  // 08 §4.2：检测 WebView2 Runtime 注册表（HKLM/HKCU 双视图），存在即跳过引导
  Result := not (RegKeyExists(HKLM, WebView2RegKey) or RegKeyExists(HKCU, WebView2RegKey));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 08 §6.4 / SEC-06：卸载时询问是否一并删除本地数据（邮件缓存/规则/令牌/日志）
    if MsgBox('是否同时删除本地数据（邮件缓存、规则与设置）？' #13#10
              + '选择「是」将删除 %AppData%\MailHelper 全部内容且不可恢复。',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\MailHelper'), True, True, True);
    end;
  end;
end;
