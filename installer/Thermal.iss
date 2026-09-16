#ifndef AppVersion
  #error AppVersion is required; use scripts/build-installer.ps1
#endif
#ifndef AppSource
  #error AppSource is required
#endif
#ifndef RepoRoot
  #error RepoRoot is required
#endif

[Setup]
#ifdef TestBuild
AppId=Thermal.PackagingTest
AppName=Thermal Packaging Test
DefaultDirName={autopf}\Thermal Packaging Test
DefaultGroupName=Thermal Packaging Test
OutputBaseFilename=Thermal-PackagingTest
UninstallDisplayName=Thermal Packaging Test
#else
AppId={{C20BE136-C61A-48FA-AFD6-DF91336C62A2}
AppName=Thermal
DefaultDirName={autopf}\Thermal
DefaultGroupName=Thermal
OutputBaseFilename=Thermal-Setup-{#AppVersion}
AppMutex=Local\Thermal.Desktop.Install
UninstallDisplayName=Thermal
#endif
AppVersion={#AppVersion}
AppPublisher=Thermal
AppPublisherURL=https://github.com/DaltonSchwanke/Thermal
AppSupportURL=https://github.com/DaltonSchwanke/Thermal/issues
UninstallDisplayIcon={app}\Thermal.exe
OutputDir={#RepoRoot}\dist
SetupIconFile={#RepoRoot}\assets\Thermal.ico
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: sensordriver; Description: "Set up the signed PawnIO driver for CPU and motherboard temperatures"; GroupDescription: "Temperature sensors:"; Check: NeedsSensorDriver

[Files]
Source: "{#AppSource}\Thermal.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\Start Thermal.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\*.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\Thermal.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\MainWindow.xaml"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\PawnIO_setup.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Thermal"; Filename: "{app}\Start Thermal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Thermal.ico"
Name: "{group}\Uninstall Thermal"; Filename: "{uninstallexe}"
#ifdef TestBuild
Name: "{autodesktop}\Thermal Packaging Test"; Filename: "{app}\Start Thermal.exe"; Tasks: desktopicon
#else
Name: "{autodesktop}\Thermal"; Filename: "{app}\Start Thermal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Thermal.ico"; Tasks: desktopicon
#endif

[Run]
Filename: "{app}\Start Thermal.exe"; Description: "Open Thermal"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
function NeedsSensorDriver: Boolean;
var
  DriverVersion: String;
  PackedVersion: Int64;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO', 'DisplayVersion', DriverVersion) then
    if StrToVersion(DriverVersion, PackedVersion) then
      Result := ComparePackedVersion(PackedVersion, PackVersionComponents(2, 0, 0, 0)) < 0;
end;

function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('Thermal needs Microsoft .NET Framework 4.8. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48, then run this installer again.', mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  SetupPath: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('sensordriver') and NeedsSensorDriver then
  begin
    SetupPath := ExpandConstant('{app}\PawnIO_setup.exe');
    if CompareText(GetSHA256OfFile(SetupPath), 'a3a46226c5e2824f4cdd42be0eecbabfc672c86f7889710f5ab1e6ad385b47a0') <> 0 then
      MsgBox('The sensor installer checksum did not match. Thermal is installed, but sensor setup was skipped. Download a fresh copy or retry from Thermal Settings.', mbError, MB_OK)
    else if not Exec(SetupPath, '-install', ExpandConstant('{app}'), SW_SHOW, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      MsgBox('The sensor driver did not finish installing. Thermal is installed and can run with limited readings. Retry using Settings > Set up temperature sensors.', mbInformation, MB_OK);
  end;
end;

// User history and settings are deliberately outside the install directory.
// Uninstall does not remove them or PawnIO, which may be shared by other apps.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Thermal', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\Thermal.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Thermal');
end;
