#define AppName "WindowBouncer"
; AppVersion is passed via /DAppVersion= by build-installer.ps1 and the MSBuild target.
; To build manually: ISCC.exe /DAppVersion=1.2.3 WindowBouncer.iss
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#define AppPublisher "WindowBouncer"
#define AppURL "https://github.com/redeuxx/WindowBouncer"
#define AppExeName "WindowBouncer.exe"
#define SourceDir "..\WindowBouncer.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; UninstallKey in [Code] repeats this GUID.
AppId={{B5C2A8F3-1E4D-4A6B-9C0E-2F3A4B5C6D7E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
; Program Files when installing for all users, %LOCALAPPDATA%\Programs for the current user.
; Upgrades keep the folder of the existing install.
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
AppMutex=WindowBouncer_SingleInstance
OutputDir=..\publish
OutputBaseFilename=WindowBouncer-{#AppVersion}-setup
SetupIconFile=..\WindowBouncer.WinUI\Resources\appicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
; Ask "Install for me only" or "Install for all users" (also /CURRENTUSER and /ALLUSERS).
PrivilegesRequiredOverridesAllowed=dialog commandline
; Ask even when a copy is already installed, so a per-user install can move to all users.
UsePreviousPrivileges=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start WindowBouncer when Windows starts"; GroupDescription: "Additional settings:"

[Files]
; Ship the entire published, self-contained WinUI 3 output (exe, .NET runtime, WindowsAppSDK, dependencies).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Silently register startup if the user chose the startup task
; runasoriginaluser: in an all-users install Setup is elevated, but the Run value and settings
; belong to the user who started Setup.
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Parameters: "/REGISTERSTARTUP /QUIT"; Flags: runhidden runasoriginaluser; Check: ShouldRegisterStartup
; Launch the app normally if the user checks the post-install checkbox
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]

// ONE INSTALL PER USER
// Per-user and all-users installs register under different roots (HKCU and HKLM), so Setup
// would happily put a second copy next to the first. Moving to all users removes the per-user
// copy first; going back to per-user while an all-users copy exists is refused.

const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B5C2A8F3-1E4D-4A6B-9C0E-2F3A4B5C6D7E}_is1';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  // The removed per-user copy started with Windows; keep that for the new copy.
  KeepAutostart: Boolean;

function WindowBouncerTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('schtasks.exe', '/query /tn WindowBouncer', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Silent installs leave startup alone, unless it carries over from a removed per-user copy.
function ShouldRegisterStartup: Boolean;
begin
  Result := ((not WizardSilent) and WizardIsTaskSelected('startup')) or KeepAutostart;
end;

function RemovePerUserInstall: String;
var
  Uninstaller: String;
  ResultCode, Waited: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', Uninstaller) then
    Exit;
  // Settings in %APPDATA% survive the uninstall, but its Run value and scheduled task do not.
  KeepAutostart := RegValueExists(HKCU, RunKey, 'WindowBouncer') or WindowBouncerTaskExists;
  if (not WizardSilent) and (MsgBox('WindowBouncer is already installed for your account only.' + #13#10#13#10 +
      'Setup will remove that copy and install WindowBouncer for all users. Your settings are kept.',
      mbConfirmation, MB_OKCANCEL) <> IDOK) then
  begin
    Result := 'Setup was cancelled. The existing installation for your account was not changed.';
    Exit;
  end;
  if not Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    Result := 'Setup could not remove the copy of WindowBouncer installed for your account. ' +
              'Uninstall it from Settings > Apps, then run Setup again.';
    Exit;
  end;
  // The uninstaller hands off to a copy of itself and returns at once. Wait for it to finish,
  // or its cleanup could delete the Start with Windows entry this install is about to write.
  Waited := 0;
  while RegKeyExists(HKCU, UninstallKey) and (Waited < 60000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if RegKeyExists(HKCU, UninstallKey) then
    Result := 'Removing the copy of WindowBouncer installed for your account did not finish. ' +
              'Uninstall it from Settings > Apps, then run Setup again.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if IsAdminInstallMode then
    Result := RemovePerUserInstall
  else if RegKeyExists(HKLM, UninstallKey) then
    Result := 'WindowBouncer is already installed for all users. Run Setup again and choose ' +
              '"Install for all users" to update it.'
  else
    Result := '';
end;

// SCHEDULED TASK CLEANUP ON UNINSTALL
// The app may have registered a "WindowBouncer" scheduled task (used to launch elevated
// when RunAsAdmin is enabled). The task is created with HighestAvailable, so deleting it
// requires admin even though this is a per-user install. Try unelevated first; only
// trigger UAC if the task actually exists and the unelevated delete fails.

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  // The app writes this Run value itself (Start with Windows), so Inno doesn't track it.
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'WindowBouncer');

  if (CurUninstallStep = usUninstall) and WindowBouncerTaskExists then
  begin
    if (not Exec('schtasks.exe', '/delete /tn WindowBouncer /f', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
      ShellExec('runas', 'schtasks.exe', '/delete /tn WindowBouncer /f', '', SW_HIDE,
                ewWaitUntilTerminated, ResultCode);
  end;
end;
