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
CloseApplications=no
RestartApplications=no

[Files]
Source: "..\backend-dotnet\RigMD.Agent\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,appsettings.Development.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{sys}\sc.exe"; Parameters: "start RigMDAgent"; Flags: runhidden waituntilterminated; StatusMsg: "Starting RigMD Agent service..."

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop RigMDAgent"; Flags: runhidden waituntilterminated; RunOnceId: "StopRigMDAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete RigMDAgent"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteRigMDAgent"

[Code]
var
  ApiPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ApiPage :=
    CreateInputQueryPage(
      wpWelcome,
      'RigMD API Configuration',
      'Configure the RigMD Agent connection',
      'Enter the RigMD API base URL. Use localhost when the API runs on this PC, or a LAN/hosted address when connecting to another machine.'
    );

  ApiPage.Add('API Base URL:', False);
  ApiPage.Values[0] := 'http://localhost:5273';
end;

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

procedure ConfigureService;
var
  ResultCode: Integer;
  ServiceExe: string;
begin
  ServiceExe :=
    ExpandConstant('{app}\RigMD.Agent.exe');

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

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ApiUrl: string;
begin
  Result := True;

  if CurPageID = ApiPage.ID then
  begin
    ApiUrl := Trim(ApiPage.Values[0]);

    if ApiUrl = '' then
    begin
      MsgBox(
        'Please enter the RigMD API base URL.',
        mbError,
        MB_OK
      );

      Result := False;
      Exit;
    end;

    if (Pos('http://', Lowercase(ApiUrl)) <> 1) and
       (Pos('https://', Lowercase(ApiUrl)) <> 1) then
    begin
      MsgBox(
        'The API address must begin with http:// or https://.',
        mbError,
        MB_OK
      );

      Result := False;
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppSettingsPath: string;
  JsonText: string;
  ApiUrl: string;
begin
  if CurStep = ssInstall then
  begin
    StopExistingService;
  end;

  if CurStep = ssPostInstall then
  begin
    AppSettingsPath :=
      ExpandConstant('{app}\appsettings.json');

    ApiUrl :=
      Trim(ApiPage.Values[0]);

    JsonText :=
      '{' + #13#10 +
      '  "Agent": {' + #13#10 +
      '    "ApiBaseUrl": "' + ApiUrl + '"' + #13#10 +
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

    ConfigureService;
  end;
end;