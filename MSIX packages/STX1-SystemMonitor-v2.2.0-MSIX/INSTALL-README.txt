STX.1 System Monitor v2.2.0 - Installation Instructions
=======================================================

BEFORE INSTALLING THE APP, YOU MUST INSTALL THE CERTIFICATE:

Step 1: Install the Certificate
--------------------------------
1. Right-click "SysMonitor.App_2.2.0.0_x64.cer"
2. Select "Install Certificate"
3. Choose "Local Machine" -> Next
4. Select "Place all certificates in the following store"
5. Click "Browse" and select "Trusted People"
6. Click OK -> Next -> Finish
7. You should see "The import was successful"

Step 2: Install the App
-----------------------
Double-click "SysMonitor.App_2.2.0.0_x64.msix" to install.

OR run the PowerShell installer:
- Right-click "Install.ps1" -> Run with PowerShell

TROUBLESHOOTING:
- If you get "Access Denied", make sure you ran the certificate install as Administrator
- If the app won't install, try enabling Developer Mode in Windows Settings

Need help? Visit: https://github.com/Git-Rocky-Stack/sysmonitor-windows
