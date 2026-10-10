; Inno Setup script: per-user install, no admin rights needed.
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6E1B7C0A-6A2F-4C2B-9B5E-2C1D7A9F4E10}
AppName=Lumi
AppVersion={#AppVersion}
AppPublisher=AlbertS15
AppPublisherURL=https://github.com/AlbertS15/Lumi
DefaultDirName={localappdata}\Programs\Lumi
DefaultGroupName=Lumi
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\dist
OutputBaseFilename=Lumi-Setup-{#AppVersion}
SetupIconFile=..\Lumi\app.ico
UninstallDisplayIcon={app}\Lumi.exe
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
Name: "startup"; Description: "{cm:AutoStartProgram,Lumi}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\dist\app\Lumi.exe"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Lumi used to be called ClaudeLight; an update installs over it, so its old files go.
Type: files; Name: "{app}\ClaudeLight.exe"
Type: files; Name: "{group}\ClaudeLight.lnk"
Type: files; Name: "{autodesktop}\ClaudeLight.lnk"

[Icons]
Name: "{group}\Lumi"; Filename: "{app}\Lumi.exe"
Name: "{autodesktop}\Lumi"; Filename: "{app}\Lumi.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lumi"; ValueData: """{app}\Lumi.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Lumi.exe"; Description: "{cm:LaunchProgram,Lumi}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im Lumi.exe"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  { Close the running app, under its old or new name, so its files can be replaced. }
  Exec('taskkill.exe', '/f /im ClaudeLight.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec('taskkill.exe', '/f /im Lumi.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

