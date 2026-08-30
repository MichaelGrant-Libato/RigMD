#define MyAppName "RigMD"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "RigMD"

[Setup]
AppId={{9A9D9ED6-7B03-4C84-8A3D-8F6E6A2E6B1D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\RigMD\Agent
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=RigMD-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=RigMD Agent
SetupLogging=yes

[Files]
Source: "..\backend-dotnet\RigMD.Agent\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,appsettings.Development.json"; Flags: ignoreversion recursesubdirs createallsubdirs
[Run]
Filename: "{sys}\sc.exe"; Parameters: "create RigMDAgent binPath= ""{app}\RigMD.Agent.exe"" start= auto DisplayName= ""RigMD Agent"""; Flags: runhidden waituntilterminated; StatusMsg: "Registering RigMD Agent service..."
Filename: "{sys}\sc.exe"; Parameters: "description RigMDAgent ""RigMD Windows hardware diagnostic agent"""; Flags: runhidden waituntilterminated; StatusMsg: "Configuring RigMD Agent service..."
Filename: "{sys}\sc.exe"; Parameters: "start RigMDAgent"; Flags: runhidden waituntilterminated; StatusMsg: "Starting RigMD Agent service..."

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop RigMDAgent"; Flags: runhidden waituntilterminated; RunOnceId: "StopRigMDAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete RigMDAgent"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteRigMDAgent"
