#define AppName "AgentQ"
#define AppPublisher "AgentQ"
#define AppExeName "AgentQ.Desktop.exe"

#ifndef AppVersion
#define AppVersion "0.0.0-local"
#endif

#ifndef ReleaseTag
#define ReleaseTag "local"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\desktop\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{B50E8F90-51B8-44C9-956F-E9E86D768D5A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\AgentQ
DefaultGroupName=AgentQ
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=AgentQ-Setup-{#ReleaseTag}
SetupIconFile=..\csharp\AgentQ.Desktop\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AgentQ"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall AgentQ"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AgentQ"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch AgentQ"; Flags: nowait postinstall skipifsilent
