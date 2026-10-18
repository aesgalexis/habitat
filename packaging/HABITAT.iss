#ifndef HabitatVersion
  #error HabitatVersion is required.
#endif
#ifndef PayloadDirectory
  #error PayloadDirectory is required.
#endif
#ifndef InstallerOutputDirectory
  #error InstallerOutputDirectory is required.
#endif

[Setup]
AppId={{{#HabitatAppId}}
AppName=HABITAT
AppVersion={#HabitatVersion}
AppPublisher={#HabitatPublisher}
UninstallDisplayName=HABITAT
AppPublisherURL={#HabitatPackageUrl}
AppSupportURL={#HabitatPackageUrl}
VersionInfoVersion={#HabitatVersion}.0
DefaultDirName={localappdata}\Programs\HABITAT
DefaultGroupName=HABITAT
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile={#PayloadDirectory}\LICENSE.txt
UninstallDisplayIcon={app}\HABITAT.exe
SetupIconFile={#PayloadDirectory}\Assets\HabitatDot.ico
OutputDir={#InstallerOutputDirectory}
OutputBaseFilename=HABITAT-{#HabitatVersion}-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=HABITAT.exe,HABITAT.Hotkey.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HABITAT"; Filename: "{app}\HABITAT.exe"; WorkingDir: "{app}"; AppUserModelID: "HABITAT.Desktop"; Check: not WizardNoIcons
Name: "{autodesktop}\HABITAT"; Filename: "{app}\HABITAT.exe"; WorkingDir: "{app}"; AppUserModelID: "HABITAT.Desktop"; Tasks: desktopicon; Check: not WizardNoIcons

[Run]
Filename: "{app}\HABITAT.exe"; Description: "Launch HABITAT"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\HABITAT.vbs')) then
    Result := 'This folder contains a ZIP installation of HABITAT. Choose a different folder or remove the old application files before using this installer. Your tasks and preferences are stored separately.';
end;

function InitializeUninstall(): Boolean;
var
  Companion: String;
  ExitCode: Integer;
begin
  Result := True;
  Companion := ExpandConstant('{app}\HABITAT.Hotkey.exe');
  if FileExists(Companion) then
  begin
    Result := Exec(Companion, '--stop', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Result then Result := ExitCode = 0;
    if not Result then
      SuppressibleMsgBox('Could not stop the HABITAT shortcut component. Close HABITAT and try again.', mbError, MB_OK, IDOK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove only this installation's startup registration. User data and
      registrations belonging to development checkouts remain untouched. }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'HABITAT.CtrlH', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\HABITAT.Hotkey.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'HABITAT.CtrlH');
  end;
end;
