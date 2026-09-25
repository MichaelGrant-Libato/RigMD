#define MyAppName "RigMD"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "RigMD"
#define MyAppExeName "RigMD.Desktop.exe"

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

[Files]
Source: "..\backend-dotnet\RigMD.Desktop\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}\Desktop"; \
    Excludes: "*.pdb,appsettings.Development.json,appsettings.Development.example.json,appsettings.Local.json,appsettings.*.local.json,secrets.json,.env*,*.db,*.db-shm,*.db-wal"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\backend-dotnet\RigMD.Api\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}\Api"; \
    Excludes: "*.pdb,appsettings.Development.json,appsettings.Development.example.json,appsettings.Local.json,appsettings.*.local.json,secrets.json,.env*,*.db,*.db-shm,*.db-wal"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "Uninstall-RigMD.bat"; \
    DestDir: "{app}\Desktop"; \
    DestName: "uninstall.bat"; \
    Flags: ignoreversion

Source: "Uninstall-RigMD.bat"; \
    DestDir: "{app}"; \
    DestName: "uninstall.bat"; \
    Flags: ignoreversion

[Tasks]
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
