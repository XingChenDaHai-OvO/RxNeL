; Rx | NeL 安装程序（Inno Setup 6）
; 编译：ISCC.exe installer\RxNeL.iss

#define AppName "RxNeL"
#define AppTitle "Rx | NeL"
#define AppVersion "4.8.2"
#define AppPublisher "XingChenDaHai-OvO"
#define AppExe "RxNeL.exe"

[Setup]
AppId={{8B1F4C2A-9E3D-4A57-9C21-7B6E5D4A1F30}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppVerName={#AppTitle} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL=https://github.com/XingChenDaHai-OvO/RxNeL
DefaultDirName={localappdata}\Programs\RxNeL
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=RxNeL-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppTitle}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: checkedonce
Name: "protocol"; Description: "接管 roblox-player 协议（浏览器点 Play 直接唤起本启动器）"; GroupDescription: "附加任务:"; Flags: checkedonce

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#AppTitle}"; Filename: "{app}\{#AppExe}"
Name: "{group}\卸载 {#AppTitle}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppTitle}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; roblox-player: HKCU 下写入，不需要管理员
Root: HKCU; Subkey: "Software\Classes\roblox-player"; ValueType: string; ValueName: ""; ValueData: "URL:Roblox Player Protocol"; Flags: uninsdeletekey; Tasks: protocol
Root: HKCU; Subkey: "Software\Classes\roblox-player"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Tasks: protocol
Root: HKCU; Subkey: "Software\Classes\roblox-player\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" -player ""%1"""; Tasks: protocol

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // 卸载时删掉协议项，避免留下指向空目录的死链
  if CurUninstallStep = usPostUninstall then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\roblox-player');
end;