#define MyAppName "RigMD"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "RigMD"
#define MyAppExeName "RigMD.Desktop.exe"
#ifndef DesktopPublishDir
  #define DesktopPublishDir "..\backend-dotnet\RigMD.Desktop\bin\Release\net10.0-windows\win-x64\publish"
#endif
#ifndef ApiPublishDir
  #define ApiPublishDir "..\backend-dotnet\RigMD.Api\bin\Release\net10.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{9A9D9ED6-7B03-4C84-8A3D-8F6E6A2E6B1D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\RigMD
DefaultGroupName=RigMD
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
OutputBaseFilename=RigMD-Setup
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
SetupIconFile=assets\rigmd-logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=RigMD
UninstallDisplayIcon={app}\Desktop\RigMD.Desktop.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[InstallDelete]
Type: files; Name: "{app}\Api\*.sys"
Type: files; Name: "{app}\Desktop\*.sys"

[Files]
Source: "{#DesktopPublishDir}\*"; \
    DestDir: "{app}\Desktop"; \
    Excludes: "*.pdb,*.sys,appsettings.Development.json,appsettings.Development.example.json,appsettings.Local.json,appsettings.*.local.json,secrets.json,.env*,*.db,*.db-shm,*.db-wal"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#ApiPublishDir}\*"; \
    DestDir: "{app}\Api"; \
    Excludes: "*.pdb,*.sys,appsettings.Development.json,appsettings.Development.example.json,appsettings.Local.json,appsettings.*.local.json,secrets.json,.env*,*.db,*.db-shm,*.db-wal"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "Uninstall-RigMD.bat"; \
    DestDir: "{app}\Desktop"; \
    DestName: "uninstall.bat"; \
    Flags: ignoreversion

Source: "Uninstall-RigMD.bat"; \
    DestDir: "{app}"; \
    DestName: "uninstall.bat"; \
    Flags: ignoreversion

Source: "uninstall-rigmd.ps1"; \
    DestDir: "{app}"; \
    Flags: ignoreversion

Source: "Manage-PawnIO.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion
Source: "dependencies\PawnIO_setup.exe"; DestDir: "{app}\Support\dependencies"; Flags: ignoreversion
Source: "CPU-SENSORS.md"; DestDir: "{app}\Support"; Flags: ignoreversion
Source: "licenses\*"; DestDir: "{app}\Support\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\*-source.zip"; DestDir: "{app}\Support\licenses\sources"; Flags: ignoreversion

[Tasks]
Name: "cpusensors"; \
    Description: "Enable CPU temperature readings (installs the signed PawnIO driver)"; \
    GroupDescription: "Hardware monitoring:"; \
    Flags: unchecked

Name: "desktopicon"; \
    Description: "Create a desktop shortcut"; \
    GroupDescription: "Additional shortcuts:"; \
    Flags: unchecked

[Icons]
Name: "{autoprograms}\RigMD\RigMD"; \
    Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    WorkingDir: "{app}\Desktop"; \
    IconFilename: "{app}\Desktop\RigMD.Desktop.exe"

Name: "{autoprograms}\RigMD\Uninstall RigMD"; \
    Filename: "{uninstallexe}"; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\Desktop\RigMD.Desktop.exe"

Name: "{app}\Desktop\Uninstall RigMD"; \
    Filename: "{uninstallexe}"; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\Desktop\RigMD.Desktop.exe"

Name: "{autodesktop}\RigMD"; \
    Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    WorkingDir: "{app}\Desktop"; \
    Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Run]
Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    Description: "Launch RigMD"; \
    WorkingDir: "{app}\Desktop"; \
    Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  PawnIORestartRequired: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Parameters: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('cpusensors') then
  begin
    Parameters := '-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -File "' +
      ExpandConstant('{app}\Support\Manage-PawnIO.ps1') + '" -Action Install';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      ExitCode := -1;
    PawnIORestartRequired := ExitCode = 3010;
    if (ExitCode <> 0) and not PawnIORestartRequired then
      MsgBox('The optional CPU sensor driver could not be installed. RigMD is installed. ' +
        'Run Support\Manage-PawnIO.ps1 with -Action Install to review the error and retry.',
        mbInformation, MB_OK);
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := PawnIORestartRequired;
end;
