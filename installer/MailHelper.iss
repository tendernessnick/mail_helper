; MailHelper 安装包脚本（S17：回归 Inno Setup 向导安装，支持自选安装目录）
; 向导页：欢迎 → 隐私/许可 → 安装目录（可自选） → 附加任务 → 安装 → 完成
; 默认安装 %LocalAppData%\Programs\MailHelper（按用户，免管理员权限）；版本号自动取自 publish 产物 exe。
; 应用内「检查更新」走 /SILENT 静默重装（同目录升级，完成后自动重启应用）。
; 编译：ISCC installer\MailHelper.iss（产物 artifacts\installer\MailHelper-stable-Setup.exe）

#define MyAppName "MailHelper"
#define MyAppPublisher "MailHelper Project"
#define MyAppExeName "MailHelper.App.exe"
#define MyAppVersion GetVersionNumbersString("..\\artifacts\\publish\\" + MyAppExeName)
#ifndef SetupOutputName
  #define SetupOutputName "MailHelper-stable-Setup"
#endif

[Setup]
AppId={{C376962C-F203-4EC5-B480-0A602498E04C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename={#SetupOutputName}
OutputDir=..\artifacts\installer
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 按用户安装（%LocalAppData%），无需提权；升级时经重启管理器关闭运行中的应用
PrivilegesRequired=lowest
CloseApplications=yes
SetupIconFile=..\src\MailHelper.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
; 简体中文向导：语言文件随仓库分发（本机 Inno 安装的语言包可能损坏），路径相对本脚本
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
; 应用完整发布目录（自包含 win-x64，含运行时与内置规则包）
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; 应用内更新走 /SILENT：postinstall 条目在静默模式下同样执行 → 升级完成后自动重启应用
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName} 邮箱管家"; Flags: nowait postinstall

[Code]
const
  WebView2RegKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Url = 'https://developer.microsoft.com/microsoft-edge/webview2/';

function NeedWebView2(): Boolean;
begin
  // WebView2 Runtime 检测（HKLM/HKCU 双视图）；Win11 与常规 Win10 均已内置
  Result := not (RegKeyExists(HKLM, WebView2RegKey) or RegKeyExists(HKCU, WebView2RegKey));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
begin
  Result := '';
  if NeedWebView2() then
  begin
    if MsgBox('本机未检测到 WebView2 Runtime（MailHelper 渲染邮件正文必需）。' #13#10
              + '是否打开微软官网下载安装？安装完成后请重新运行本安装程序。',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', WebView2Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := '缺少 WebView2 Runtime，安装已中止。';
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // SEC-06：卸载时询问是否一并删除本地数据（邮件缓存/规则/设置/日志）
    if MsgBox('是否同时删除本地数据（邮件缓存、规则与设置）？' #13#10
              + '选择「是」将删除 %AppData%\MailHelper 全部内容且不可恢复。',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\MailHelper'), True, True, True);
    end;
  end;
end;
