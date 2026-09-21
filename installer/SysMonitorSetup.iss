; STX1 System Monitor - Inno Setup Script
; Copyright (c) 2024 Rocky Stack
;
; To sign the installer, use SignTool after compilation with a certificate from the Windows certificate store:
; signtool sign /sha1 <thumbprint> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "STX1-SystemMonitor-Setup-<version>.exe"

#define MyAppName "STX1 System Monitor"

; Read straight out of the executable being packaged. Written by hand this said 1.0.0 for a 2.2.2
; build, so Add/Remove Programs and HKLM\SOFTWARE\...\Version both reported a release that never
; existed. GetFileVersion returns four parts ("2.2.2.0"); the trailing one is dropped.
#define AppExeFile "..\publish\installer-build\SysMonitor.App.exe"
#define FullVersion GetFileVersion(AppExeFile)
#define MyAppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)
#define MyAppPublisher "Rocky Stack"
#define MyAppURL "https://github.com/Git-Rocky-Stack/sysmonitor-windows"
#define MyAppExeName "SysMonitor.App.exe"
#define MyAppAssocName "System Monitor"
#define MyAppAssocExt ".sysmon"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

[Setup]
; Basic app information
AppId={{8F4E2A1B-5C3D-4E6F-A8B9-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; License and readme
LicenseFile=..\LICENSE
InfoBeforeFile=..\README_INSTALLER.txt

; Output settings
OutputDir=..\publish\installer
OutputBaseFilename=STX1-SystemMonitor-Setup-{#MyAppVersion}
SetupIconFile=installer_icon.ico

; Compression - use LZMA2 for best compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4

; Privileges and compatibility
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Visual settings
WizardStyle=modern
WizardSizePercent=120
DisableWelcomePage=no
ShowLanguageDialog=auto

; Uninstaller settings
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Main application files
Source: "..\publish\installer-build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

; Licence text, installed beside the application. MIT requires the copyright and
; permission notice to travel with every copy of the software, and the bundled
; LibreHardwareMonitor (MPL-2.0) and Serilog (Apache-2.0) carry notice obligations
; of their own. Build-Release.ps1 does not stage these into installer-build, so
; they are taken from the repository root. Renamed .txt so it opens on a
; double-click rather than prompting for an application.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; App registration
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

; Uninstall information
Root: HKLM; Subkey: "SOFTWARE\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Code]
// Check if .NET 8 Desktop Runtime is installed (for framework-dependent deployments)
// Since we're self-contained, this is informational only
function IsDotNet8Installed: Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', '8.0.0', Version);
end;

// Initialize setup - show welcome message
function InitializeSetup: Boolean;
begin
  Result := True;
end;

// Custom uninstall - clean up app data if requested
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: String;
  MsgResult: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataPath := ExpandConstant('{localappdata}\SysMonitor');
    if DirExists(AppDataPath) then
    begin
      MsgResult := MsgBox('Do you want to remove application data (logs, settings, database)?'#13#10#13#10'Location: ' + AppDataPath, mbConfirmation, MB_YESNO);
      if MsgResult = IDYES then
      begin
        DelTree(AppDataPath, True, True, True);
      end;
    end;
  end;
end;
