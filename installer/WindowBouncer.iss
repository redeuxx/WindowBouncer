#define AppName "WindowBouncer"
; AppVersion is passed via /DAppVersion= by build-installer.ps1 and the MSBuild target.
; To build manually: ISCC.exe /DAppVersion=1.2.3 WindowBouncer.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "WindowBouncer"
#define AppURL "https://github.com/redeuxx/WindowBouncer"
#define AppExeName "WindowBouncer.exe"
#define SourceDir "..\WindowBouncer.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{B5C2A8F3-1E4D-4A6B-9C0E-2F3A4B5C6D7E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={localappdata}\{#AppName}
DisableProgramGroupPage=yes
AppMutex=WindowBouncer_SingleInstance
OutputDir=..\publish
OutputBaseFilename=WindowBouncer-{#AppVersion}-setup
SetupIconFile=..\WindowBouncer.WinUI\Resources\appicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
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
; Drop any cached settings from older builds so the new app starts fresh on upgrade if needed.

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Silently register startup if the user chose the startup task
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Parameters: "/REGISTERSTARTUP /QUIT"; Flags: runhidden skipifsilent; Tasks: startup
; Launch the app normally if the user checks the post-install checkbox
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]

// SCHEDULED TASK CLEANUP ON UNINSTALL
// The app may have registered a "WindowBouncer" scheduled task (used to launch elevated
// when RunAsAdmin is enabled). The task is created with HighestAvailable, so deleting it
// requires admin even though this is a per-user install. Try unelevated first; only
// trigger UAC if the task actually exists and the unelevated delete fails.

function WindowBouncerTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('schtasks.exe', '/query /tn WindowBouncer', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and WindowBouncerTaskExists then
  begin
    if (not Exec('schtasks.exe', '/delete /tn WindowBouncer /f', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
      ShellExec('runas', 'schtasks.exe', '/delete /tn WindowBouncer /f', '', SW_HIDE,
                ewWaitUntilTerminated, ResultCode);
  end;
end;
