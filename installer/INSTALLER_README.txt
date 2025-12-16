============================================================
      SYSMONITOR INSTALLER BUILD INSTRUCTIONS
============================================================

This folder contains everything needed to create a professional
Windows installer for SysMonitor using Inno Setup.


PREREQUISITES
-------------
1. Install Inno Setup 6 (free):
   https://jrsoftware.org/isdl.php

2. Build the application in Release mode:
   cd sysmonitor-windows
   dotnet publish src\SysMonitor.App -c Release -r win-x64 --self-contained


CREATING THE INSTALLER
----------------------
Option 1: Run the batch script
   Double-click: build-installer.bat

Option 2: Run the PowerShell script
   Right-click: build-installer.ps1 > Run with PowerShell

Option 3: Manual compilation
   1. Open Inno Setup Compiler
   2. File > Open > SysMonitor.iss
   3. Build > Compile (Ctrl+F9)


OUTPUT
------
The installer will be created at:
   installer\output\SysMonitor_Setup_1.0.0.exe


FILES IN THIS FOLDER
--------------------
SysMonitor.iss       - Main Inno Setup script
LICENSE.rtf          - License agreement (shown during install)
README_BEFORE.txt    - Pre-installation information
README_AFTER.txt     - Post-installation information
installer_icon.ico   - Installer icon (YOU NEED TO CREATE THIS)
build-installer.bat  - Windows batch build script
build-installer.ps1  - PowerShell build script


CUSTOMIZATION
-------------
To customize the installer:

1. CHANGE VERSION NUMBER:
   Edit SysMonitor.iss, line 7:
   #define MyAppVersion "1.0.0"

2. ADD CUSTOM ICON:
   Create a 256x256 .ico file named "installer_icon.ico"
   Tools: https://convertio.co/png-ico/

3. MODIFY LICENSE:
   Edit LICENSE.rtf in WordPad or Word

4. CHANGE PUBLISHER INFO:
   Edit lines 8-9 in SysMonitor.iss:
   #define MyAppPublisher "Rocky Stack"
   #define MyAppURL "https://rockystack.com"


INSTALLER FEATURES
------------------
* License agreement page
* Custom installation directory selection
* Start Menu folder selection
* Optional desktop shortcut
* Optional "Start with Windows" option
* Uninstaller with optional user data cleanup
* Modern wizard style
* LZMA2 compression (smallest size)
* Digital signature ready


CODE SIGNING (OPTIONAL)
-----------------------
To sign the installer for Windows SmartScreen:

1. Get a code signing certificate from:
   - DigiCert
   - Sectigo
   - SSL.com

2. Add to SysMonitor.iss [Setup] section:
   SignTool=signtool sign /f "cert.pfx" /p "password" /t http://timestamp.digicert.com $f

3. Or sign manually after build:
   signtool sign /f "cert.pfx" /p "password" /t http://timestamp.digicert.com output\SysMonitor_Setup_1.0.0.exe


TROUBLESHOOTING
---------------
Q: "Inno Setup not found" error
A: Install Inno Setup 6 from https://jrsoftware.org/isdl.php

Q: "SysMonitor.App.exe not found" error
A: Run: dotnet publish src\SysMonitor.App -c Release -r win-x64 --self-contained

Q: Installer is very large
A: The self-contained .NET app includes runtime (~150MB is normal)

Q: Windows SmartScreen warning
A: Either code-sign the installer or users can click "More info" > "Run anyway"


============================================================
             (C) 2024 Rocky Stack - All Rights Reserved
============================================================
