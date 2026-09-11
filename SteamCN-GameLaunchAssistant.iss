; Build through scripts/Publish-Release.ps1.
#ifndef MyAppVersion
  #error MyAppVersion must be supplied by the release script
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by the release script
#endif
#define MyAppName "Steam国服游戏启动助手"
#define MyAppIdName "SteamCN-GameLaunchAssistant"
#define MyAppExeName MyAppIdName + ".exe"
#define MyAppURL "https://github.com/Violet0923/SteamCN-GameLauncher"

[Setup]
; 独立仓库使用自己的安装器标识，不与旧项目建立升级关系。
AppId={{5274484C-9DD0-4995-AA31-2E01BFA0126A}
AppMutex=Local\SteamCN-GameLauncher_SingleInstance
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Violet0923
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases/latest
DefaultDirName={autopf}\{#MyAppIdName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputBaseFilename={#MyAppIdName}-v{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern windows11
SetupIconFile=Assets\Icons\SteamCN-GameLaunchAssistant.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "packaging\languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; 用户背景单独授权写入；升级、卸载时保留用户导入的图片。
Name: "{app}\Backgrounds"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\logs"; Permissions: users-modify
; 一键更新的 ACF 滚动备份；升级和卸载时保留，便于用户恢复。
Name: "{app}\backups"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
; Include new dependencies automatically; exclude development symbols and logs.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
