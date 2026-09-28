; CleanSlate — Windows Installer (Inno Setup 6)
; Build: tools\build-installer.ps1

#define MyAppName "简卸"
#define MyAppVersion "0.2.7"
#define MyAppPublisher "CleanSlate Project"
#define MyAppExeName "CleanSlate.exe"
#define PublishDir "..\dist\App"

[Setup]
AppId={{8F3C2A1E-6B4D-4E2A-9C1F-C1E2A3B4C5D6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/cleanslate-rules/agent-rules
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=CleanSlate-Setup-{#MyAppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\docs\LICENSE.txt
InfoAfterFile=..\docs\PRIVACY.md
SetupIconFile=..\assets\app.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
CloseApplications=yes
RestartApplications=no
; Default to Chinese; no language picker
ShowLanguageDialog=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "将命令行工具 (CleanSlate.Cli.exe) 加入 PATH 环境变量"; GroupDescription: "其他选项:"; Flags: unchecked

[Files]
; Self-contained publish output
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\PRIVACY.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\LICENSE.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\api\agent-rules.json"; DestDir: "{app}\rules"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} CLI Help"; Filename: "{app}\CleanSlate.Cli.exe"; Parameters: "help"; IconFilename: "{app}\CleanSlate.Cli.exe"
Name: "{group}\Privacy Notice"; Filename: "{app}\docs\PRIVACY.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional PATH entry for CLI
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep user data under %LOCALAPPDATA%\CleanSlate by default (quarantine/history).
; Only remove app directory leftovers.
Type: filesandordirs; Name: "{app}"

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
  AppDir: string;
begin
  AppDir := Param;
  if not RegQueryStringValue(HKLM,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  { look for AppDir as whole path segment }
  if Pos(';' + Uppercase(AppDir) + ';', ';' + Uppercase(OrigPath) + ';') > 0 then
    Result := False
  else
    Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if MsgBox('Uninstall CleanSlate?'#13#13 +
            'Your cleanup history / quarantine under %LOCALAPPDATA%\CleanSlate will be kept.',
            mbConfirmation, MB_YESNO) = IDNO then
    Result := False;
end;
