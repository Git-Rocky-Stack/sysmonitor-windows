============================================================
      SYSMONITOR INSTALLER BUILD INSTRUCTIONS
============================================================

This folder contains everything needed to create a Windows
installer for SysMonitor using Inno Setup.


PREREQUISITES
-------------
1. Install Inno Setup 6 (free):
   https://jrsoftware.org/isdl.php

2. Publish the application. The installer packages whatever is in
   publish\installer-build, and reads the version out of the
   executable it finds there - so it has to exist first:

   cd sysmonitor-windows
   dotnet publish src\SysMonitor.App -c Release -r win-x64 --self-contained -o publish\installer-build


CREATING THE INSTALLER
----------------------
Option 1: The full release build (publish + installer + zip + optional signing)
   .\Build-Release.ps1

Option 2: Installer only, from this folder
   Double-click: build-installer.bat
   or right-click build-installer.ps1 > Run with PowerShell

Option 3: Manual compilation
   1. Open Inno Setup Compiler
   2. File > Open > SysMonitorSetup.iss
   3. Build > Compile (Ctrl+F9)


OUTPUT
------
   publish\installer\STX1-SystemMonitor-Setup-<version>.exe

<version> is taken from the published SysMonitor.App.exe, which
takes it from <Version> in src\SysMonitor.App\SysMonitor.App.csproj.


FILES IN THIS FOLDER
--------------------
SysMonitorSetup.iss  - The Inno Setup script. The only one: every build path
                       compiles this file, and a second script would mean a
                       second AppId, which Inno Setup treats as a different
                       product - so installing from both would leave two
                       copies side by side instead of upgrading.
LICENSE.rtf          - License agreement (shown during install)
README_BEFORE.txt    - Pre-installation information
README_AFTER.txt     - Post-installation information
installer_icon.ico   - Installer icon
build-installer.bat  - Windows batch build script
build-installer.ps1  - PowerShell build script


CUSTOMIZATION
-------------
1. CHANGE VERSION NUMBER:
   Edit <Version> in src\SysMonitor.App\SysMonitor.App.csproj, and the
   matching Version="x.y.z.0" in src\SysMonitor.App\Package.appxmanifest.
   Nothing in this folder carries a version of its own; the installer,
   the portable zip and the registry entry all derive from that one value.
   ReleaseVersionTests in the test suite fails the build if they drift apart.

2. REPLACE THE ICON:
   Replace installer_icon.ico with a 256x256 .ico file.

3. MODIFY LICENSE:
   Edit LICENSE.rtf in WordPad or Word

4. CHANGE PUBLISHER INFO:
   Edit these lines in SysMonitorSetup.iss:
   #define MyAppPublisher "Rocky Stack"
   #define MyAppURL "https://github.com/rockystack"


INSTALLER FEATURES
------------------
* License agreement page
* Custom installation directory selection
* Start Menu folder selection
* Optional desktop shortcut
* Uninstaller that offers to remove application data
  (%LocalAppData%\SysMonitor - logs, settings, database)
* Modern wizard style
* LZMA2 compression
* Digital signature ready


CODE SIGNING (OPTIONAL)
-----------------------
To sign the installer for Windows SmartScreen:

1. Get a code signing certificate from:
   - DigiCert
   - Sectigo
   - SSL.com

2. Install the certificate in your Windows certificate store (CurrentUser\My) and note its
   thumbprint. Never place certificate files in this repository.

3. Sign after build, selecting the certificate by thumbprint:
   signtool sign /sha1 <thumbprint> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ^
     ..\publish\installer\STX1-SystemMonitor-Setup-<version>.exe

   (Build-Release.ps1 -SignCode -CertificateThumbprint <thumbprint> does this for you.)


TROUBLESHOOTING
---------------
Q: "Inno Setup not found" error
A: Install Inno Setup 6 from https://jrsoftware.org/isdl.php

Q: "SysMonitor.App.exe not found", or the compiler cannot read the version
A: Publish first - see PREREQUISITES. The installer reads its version from
   that executable, so it will not compile without it.

Q: Installer is very large
A: The self-contained .NET app includes runtime (~150MB is normal)

Q: Windows SmartScreen warning
A: Either code-sign the installer or users can click "More info" > "Run anyway"


============================================================
             (C) 2024 Rocky Stack - All Rights Reserved
============================================================
