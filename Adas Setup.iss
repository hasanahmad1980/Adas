#define MyAppName "Adas"
#define MyAppVersion "2.6.34"
#define MyAppPublisher "Adas"
#define MyAppURL "https://github.com/RankFTW/RHI"
#define MyAppExeName "RHI.exe"

[Setup]
AppId={{E90B7C80-3C2A-4AA4-A18B-40E21D3F81C2}
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
SetupIconFile=RenoDXCommander\icon.ico
LicenseFile=LICENSE
InfoAfterFile=THIRD_PARTY_NOTICES.md
SolidCompression=yes
WizardStyle=modern dynamic
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
    Procs := WMI.ExecQuery('SELECT * FROM Win32_Process WHERE Name="RHI.exe"');
    Result := (Procs.Count > 0);
  except
  end;
end;

function InitializeSetup(): Boolean;
var
  SignalDir, SignalPath: String;
  WaitCount: Integer;
begin
  Result := True;
  if not IsAdasRunning() then Exit;

  SignalDir := ExpandConstant('{localappdata}\RHI');
  if not DirExists(SignalDir) then ForceDirectories(SignalDir);
  SignalPath := SignalDir + '\rhi_shutdown_requested';
  SaveStringToFile(SignalPath, 'update', False);

  WaitCount := 0;
  while (WaitCount < 20) and IsAdasRunning() do
  begin
    Sleep(500);
    WaitCount := WaitCount + 1;
  end;
end;
