#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained win-x64 publish folder.
#endif
#ifndef OutputDir
  #error OutputDir must point to the release artifact folder.
#endif

[Setup]
AppId={{A94C568D-C4F8-4543-9A5D-FC9599452E08}
AppName=Codex Monitor
AppVersion={#AppVersion}
AppVerName=Codex Monitor {#AppVersion}
AppPublisher=yangthen
AppPublisherURL=https://github.com/yangthen/CodexMonitor
AppSupportURL=https://github.com/yangthen/CodexMonitor/issues
DefaultDirName={localappdata}\Programs\CodexMonitor
DefaultGroupName=Codex Monitor
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\CodexMonitor.App.exe
SetupIconFile=..\src\CodexMonitor.App\Assets\AppIcon.ico
OutputDir={#OutputDir}
OutputBaseFilename=CodexMonitor-{#AppVersion}-win-x64-setup
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Codex Monitor"; Filename: "{app}\CodexMonitor.App.exe"
Name: "{autodesktop}\Codex Monitor"; Filename: "{app}\CodexMonitor.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CodexMonitor.App.exe"; Description: "{cm:LaunchProgram,Codex Monitor}"; Flags: nowait postinstall skipifsilent
