#define MyAppName "RigMD"
#define MyAppVersion "0.1.0"
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
OutputBaseFilename=RigMD-Setup-v0.1.1
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
SetupIconFile=C:\Users\Michael Grant\OneDrive\Documents\GitHub\RigMD-Project\installer\assets\rigmd-logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=RigMD
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\backend-dotnet\RigMD.Desktop\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}\Desktop"; \
    Excludes: "*.pdb,appsettings.Development.json"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\backend-dotnet\RigMD.Api\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}\Api"; \
    Excludes: "*.pdb,appsettings.Development.json"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\backend-dotnet\RigMD.Agent\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}\Agent"; \
    Excludes: "*.pdb,appsettings.Development.json"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; \
    Description: "Create a desktop shortcut"; \
    GroupDescription: "Additional shortcuts:"; \
    Flags: unchecked

[Icons]
Name: "{autoprograms}\RigMD"; \
    Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    WorkingDir: "{app}\Desktop"

Name: "{autodesktop}\RigMD"; \
    Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    WorkingDir: "{app}\Desktop"; \
    Tasks: desktopicon

[Run]
Filename: "{sys}\sc.exe"; \
    Parameters: "start RigMDAgent"; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Starting RigMD Agent service..."

Filename: "{app}\Desktop\RigMD.Desktop.exe"; \
    Description: "Launch RigMD"; \
    WorkingDir: "{app}\Desktop"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; \
    Parameters: "stop RigMDAgent"; \
    Flags: runhidden waituntilterminated; \
    RunOnceId: "StopRigMDAgent"

Filename: "{sys}\sc.exe"; \
    Parameters: "delete RigMDAgent"; \
    Flags: runhidden waituntilterminated; \
    RunOnceId: "DeleteRigMDAgent"

[Code]
function ServiceExists: Boolean;
var
    ResultCode: Integer;
begin
    Result :=
        Exec(
            ExpandConstant('{sys}\sc.exe'),
            'query RigMDAgent',
            '',
            SW_HIDE,
            ewWaitUntilTerminated,
            ResultCode
        ) and
        (ResultCode = 0);
end;

procedure StopExistingService;
var
    ResultCode: Integer;
begin
    if ServiceExists then
    begin
        Exec(
            ExpandConstant('{sys}\sc.exe'),
            'stop RigMDAgent',
            '',
            SW_HIDE,
            ewWaitUntilTerminated,
            ResultCode
        );

        Sleep(2000);
    end;
end;

procedure ConfigureAgentSettings;
var
    AppSettingsPath: string;
    JsonText: string;
begin
    AppSettingsPath :=
        ExpandConstant('{app}\Agent\appsettings.json');

    JsonText :=
        '{' + #13#10 +
        '  "Agent": {' + #13#10 +
        '    "ApiBaseUrl": "http:' + '//' + 'localhost:5273"' + #13#10 +
        '  },' + #13#10 +
        '  "Logging": {' + #13#10 +
        '    "LogLevel": {' + #13#10 +
        '      "Default": "Information",' + #13#10 +
        '      "Microsoft.Hosting.Lifetime": "Information"' + #13#10 +
        '    }' + #13#10 +
        '  }' + #13#10 +
        '}';

    if not SaveStringToFile(
        AppSettingsPath,
        JsonText,
        False
    ) then
    begin
        MsgBox(
            'RigMD could not save the Agent API configuration.',
            mbError,
            MB_OK
        );
    end;
end;

procedure ConfigureService;
var
    ResultCode: Integer;
    ServiceExe: string;
begin
    ServiceExe :=
        ExpandConstant('{app}\Agent\RigMD.Agent.exe');

    if ServiceExists then
    begin
        Exec(
            ExpandConstant('{sys}\sc.exe'),
            'config RigMDAgent binPath= "' + ServiceExe + '" start= auto DisplayName= "RigMD Agent"',
            '',
            SW_HIDE,
            ewWaitUntilTerminated,
            ResultCode
        );
    end
    else
    begin
        Exec(
            ExpandConstant('{sys}\sc.exe'),
            'create RigMDAgent binPath= "' + ServiceExe + '" start= auto DisplayName= "RigMD Agent"',
            '',
            SW_HIDE,
            ewWaitUntilTerminated,
            ResultCode
        );
    end;

    Exec(
        ExpandConstant('{sys}\sc.exe'),
        'description RigMDAgent "RigMD Windows hardware diagnostic agent"',
        '',
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode
    );
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
    if CurStep = ssInstall then
    begin
        StopExistingService;
    end;

    if CurStep = ssPostInstall then
    begin
        ConfigureAgentSettings;
        ConfigureService;
    end;
end;