; Glasswave installer (Inno Setup 6). Per-user install: no administrator rights required.
; Build: tools\build-release.ps1  (publishes the app, then compiles this script)

#define AppName "Glasswave"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "Glasswave.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\fdd"
#endif

[Setup]
AppId={{7E1C2A9B-5D4F-4C3B-9A61-2F0E8B6D4C10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Glasswave (local build)
AppComments=Liquid Glass desktop customisation for Windows. No telemetry.
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=Glasswave-Setup-{#AppVersion}
SetupIconFile=..\src\Glasswave\Assets\glasswave.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Glasswave when I sign in"; GroupDescription: "Start-up:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value Glasswave's own "Start with Windows" switch reads and writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Glasswave"; ValueData: """{app}\{#AppExe}"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Glasswave"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Glasswave"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitGlasswave"

[Code]
const
  DotNetHelp = 'https://dotnet.microsoft.com/download/dotnet/8.0';

function DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not DesktopRuntimeInstalled() then
  begin
    if SuppressibleMsgBox('Glasswave needs the Microsoft .NET 8 Desktop Runtime (x64), which was not found.' + #13#10#13#10 +
              'Open Microsoft''s official download page now? Install the ".NET Desktop Runtime 8" for x64, then run this setup again.' + #13#10#13#10 +
              'Tip: the portable Glasswave package includes everything and needs no runtime.',
              mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', DotNetHelp, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // Close a running copy before files are replaced (upgrade in place keeps all settings).
  if CurStep = ssInstall then
    if FileExists(ExpandConstant('{app}\Glasswave.exe')) then
      Exec(ExpandConstant('{app}\Glasswave.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Glasswave');
    if FileExists(DataDir + '\backups\windows-settings-backup.json') then
      if SuppressibleMsgBox('Glasswave changed some Windows appearance settings (for example accent colour or taskbar options).' + #13#10#13#10 +
                'Restore them to how they were before Glasswave?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
        Exec(ExpandConstant('{app}\Glasswave.exe'), '--revert-tweaks', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Glasswave');
    if DirExists(DataDir) then
      if SuppressibleMsgBox('Also delete your Glasswave settings, themes, profiles, notes and tasks?' + #13#10#13#10 +
                'Choose No to keep them for a future reinstall. Rainmeter and its skins are never touched.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
