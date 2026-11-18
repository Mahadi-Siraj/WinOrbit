; WinOrbit installer (Inno Setup 6). Per-user install: no administrator rights required.
; Build: tools\build-release.ps1  (publishes the app, then compiles this script)

#define AppName "WinOrbit"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppExe "WinOrbit.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\fdd"
#endif

[Setup]
AppId={{7E1C2A9B-5D4F-4C3B-9A61-2F0E8B6D4C10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=mssatalite
AppPublisherURL=https://github.com/Mahadi-Siraj
AppSupportURL=https://github.com/Mahadi-Siraj/WinOrbit/issues
AppComments=Dynamic Island, widgets and liquid-glass desktop for Windows. Free, open source, no telemetry.
; Same AppId as the earlier "Glasswave" builds, so this upgrades them in place.
DefaultDirName={localappdata}\Programs\{#AppName}
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=WinOrbit-Setup-{#AppVersion}
SetupIconFile=..\src\WinOrbit\Assets\winorbit.ico
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
Name: "startup"; Description: "Start WinOrbit when I sign in"; GroupDescription: "Start-up:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Remove the program files and shortcuts of the app's previous name (settings are moved by the app itself).
Type: filesandordirs; Name: "{localappdata}\Programs\Glasswave"
Type: files; Name: "{autoprograms}\Glasswave.lnk"
Type: files; Name: "{autodesktop}\Glasswave.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value WinOrbit's own "Start with Windows" switch reads and writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinOrbit"; ValueData: """{app}\{#AppExe}"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "WinOrbit"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Glasswave"; Flags: deletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch WinOrbit"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitWinOrbit"

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
    if SuppressibleMsgBox('WinOrbit needs the Microsoft .NET 8 Desktop Runtime (x64), which was not found.' + #13#10#13#10 +
              'Open Microsoft''s official download page now? Install the ".NET Desktop Runtime 8" for x64, then run this setup again.' + #13#10#13#10 +
              'Tip: the portable WinOrbit package includes everything and needs no runtime.',
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
  begin
    if FileExists(ExpandConstant('{app}\WinOrbit.exe')) then
      Exec(ExpandConstant('{app}\WinOrbit.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // The same app under its previous name.
    if FileExists(ExpandConstant('{localappdata}\Programs\Glasswave\Glasswave.exe')) then
      Exec(ExpandConstant('{localappdata}\Programs\Glasswave\Glasswave.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\WinOrbit');
    if FileExists(DataDir + '\backups\windows-settings-backup.json') then
      if SuppressibleMsgBox('WinOrbit changed some Windows appearance settings (for example accent colour or taskbar options).' + #13#10#13#10 +
                'Restore them to how they were before WinOrbit?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
        Exec(ExpandConstant('{app}\WinOrbit.exe'), '--revert-tweaks', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\WinOrbit');
    if DirExists(DataDir) then
      if SuppressibleMsgBox('Also delete your WinOrbit settings, themes, profiles, notes and tasks?' + #13#10#13#10 +
                'Choose No to keep them for a future reinstall. Rainmeter and its skins are never touched.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
