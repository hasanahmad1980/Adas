; Adas — Avalonia app installer (Phase 5).
; Targets the new lean Adas.exe (Avalonia, self-contained + trimmed), replacing the retired
; WinUI RHI.exe. New AppId so it installs side-by-side rather than upgrading an RHI install.
#define MyAppName "Adas"
#define MyAppVersion "2.6.56"
#define MyAppPublisher "Adas"
#define MyAppURL "https://github.com/hasanahmad1980/Adas"
#define MyAppExeName "Adas.exe"

[Setup]
AppId={{BD64CDEA-920C-46B2-AFE9-5F277886DF2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription=Adas automatic DLSS 5 setup
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Adas
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=artifacts\installer
OutputBaseFilename=Adas-Setup
SetupIconFile=Adas.App\icon.ico
LicenseFile=LICENSE
InfoAfterFile=THIRD_PARTY_NOTICES.md
SolidCompression=yes
WizardStyle=modern dynamic
; Ask the Windows restart manager to close a running Adas.exe before we overwrite files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "artifacts\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsAdasRunning(): Boolean;
var
  WMI: Variant;
  Procs: Variant;
begin
  Result := False;
  try
    WMI := CreateOleObject('WbemScripting.SWbemLocator');
    WMI := WMI.ConnectServer('.', 'root\cimv2');
    Procs := WMI.ExecQuery('SELECT * FROM Win32_Process WHERE Name="Adas.exe"');
    Result := (Procs.Count > 0);
  except
  end;
end;

function InitializeSetup(): Boolean;
var
  WaitCount: Integer;
begin
  Result := True;
  // The restart manager (CloseApplications=yes) asks Adas.exe to close; give a graceful
  // window for it to exit before the file copy begins. Nothing consumes a signal file in
  // the new Avalonia app, so we simply poll for the process to clear.
  WaitCount := 0;
  while (WaitCount < 20) and IsAdasRunning() do
  begin
    Sleep(500);
    WaitCount := WaitCount + 1;
  end;
end;
