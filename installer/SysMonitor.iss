; SysMonitor for Windows - Inno Setup Script
; Created by Rocky Stack / Strategia
; ============================================

#define MyAppName "STX.1 System Monitor"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "Rocky Stack / Strategia-X"
#define MyAppURL "https://github.com/Git-Rocky-Stack/sysmonitor-windows"
#define MyAppExeName "SysMonitor.App.exe"
#define MyAppAssocName "SysMonitor Configuration"
#define MyAppAssocExt ".sysmon"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

; Path to your published build output
#define SourcePath "..\publish\release-folder"

[Setup]
; Basic installer info
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
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

; Allow user to choose install directory
DisableDirPage=no
DisableProgramGroupPage=no

; License and info files
LicenseFile=LICENSE.rtf
InfoBeforeFile=README_BEFORE.txt
InfoAfterFile=README_AFTER.txt

; Output settings
OutputDir=output
OutputBaseFilename=STX1-SystemMonitor-v{#MyAppVersion}-Setup
SetupIconFile=installer_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compression (lzma2 gives best results)
Compression=lzma2/ultra64
SolidCompression=yes

; Modern installer look
WizardStyle=modern
WizardSizePercent=110

; Require admin for Program Files install
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Version info embedded in installer
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (C) 2024-2025 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; Minimum Windows version (Windows 10 1903+)
MinVersion=10.0.18362

; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode
Name: "startupicon"; Description: "Start SysMonitor with Windows"; GroupDescription: "Startup Options:"; Flags: unchecked

[Files]
; Main application files (recursively copy all from publish folder)
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
; Start Menu shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; Desktop shortcut (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Quick launch (legacy, pre-Windows 7)
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Registry]
; App registration for Windows
Root: HKLM; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

; Startup entry (optional task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Option to launch after install (shellexec needed for UAC elevation)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Clean up app data folder on uninstall (optional - user data)
Type: filesandordirs; Name: "{localappdata}\SysMonitor"

[Code]
// Custom Pascal code for advanced installer behavior

var
  DownloadPage: TDownloadWizardPage;

function InitializeSetup(): Boolean;
begin
  // Check for .NET 8 Desktop Runtime (optional - since app is self-contained)
  Result := True;
end;

procedure InitializeWizard();
begin
  // Customize wizard appearance if needed
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Post-install actions (e.g., register services, create firewall rules)
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up user data prompt
    if MsgBox('Do you want to remove all SysMonitor user data and settings?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\SysMonitor'), True, True, True);
    end;
  end;
end;

// Check if app is already running
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('tasklist', '/FI "IMAGENAME eq {#MyAppExeName}" /NH', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // Could add check to close running app here
end;
