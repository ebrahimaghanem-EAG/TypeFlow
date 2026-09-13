; TypeFlow installer — Inno Setup 6 script.
; Compiled against the self-contained single-file publish (dist\TypeFlow\TypeFlow.exe).

#define MyAppName "TypeFlow"
#define MyAppVersion "1.0.0-beta"
#define MyAppPublisher "TypeFlow"
#define MyAppExeName "TypeFlow.exe"

[Setup]
AppId={{81F127D3-72FB-42AE-AB5C-067043D111EC}
AppName={#MyAppName}
AppVersion=1.0.0-beta
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=TypeFlow-Setup-1.0.0-beta
SetupIconFile=..\src\TypeFlow.App\assets\TypeFlow.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=TypeFlow text expander

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\TypeFlow\TypeFlow.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"