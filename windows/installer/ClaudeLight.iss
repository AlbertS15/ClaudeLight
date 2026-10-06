; Inno Setup script: per-user install, no admin rights needed.
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6E1B7C0A-6A2F-4C2B-9B5E-2C1D7A9F4E10}
AppName=ClaudeLight
AppVersion={#AppVersion}
AppPublisher=AlbertS15
AppPublisherURL=https://github.com/AlbertS15/ClaudeLight
DefaultDirName={localappdata}\Programs\ClaudeLight
DefaultGroupName=ClaudeLight
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\dist
OutputBaseFilename=ClaudeLight-Setup-{#AppVersion}
SetupIconFile=..\ClaudeLight\app.ico
UninstallDisplayIcon={app}\ClaudeLight.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "{cm:AutoStartProgram,ClaudeLight}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\dist\app\ClaudeLight.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClaudeLight"; Filename: "{app}\ClaudeLight.exe"
Name: "{autodesktop}\ClaudeLight"; Filename: "{app}\ClaudeLight.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClaudeLight"; ValueData: """{app}\ClaudeLight.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ClaudeLight.exe"; Description: "{cm:LaunchProgram,ClaudeLight}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im ClaudeLight.exe"; Flags: runhidden; RunOnceId: "KillApp"
