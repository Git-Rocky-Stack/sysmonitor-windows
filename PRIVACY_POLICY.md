# Privacy Policy

**STX-1 ULTIMATE System Monitor**
**Effective Date:** January 3, 2026
**Last Updated:** October 22, 2026

## Overview

STX-1 ULTIMATE System Monitor ("the App") is developed by Rocky Stack and published by
Strategia-X, in the Microsoft Store and on GitHub. This policy explains what the App reads, what it keeps, and
what - if anything - leaves your computer. It describes the code as it is; where a statement
can be checked, it says where.

## In short

- **Nothing about you or your computer is sent to the developer or to anyone else.** The App
  has no account, no analytics, no telemetry, no advertising and no crash reporting.
- **It needs no internet connection.** The only network activity is on your own network, and
  only when you start it (see "Network activity" below).
- **Everything it keeps stays in folders on your computer**, which you can delete.

## What the App reads, on your computer

To show and maintain your system, the App reads locally:

- Processor, graphics, memory, disk, battery and temperature readings, through Windows and
  the LibreHardwareMonitor library
- Network adapters and their throughput
- Running processes, startup entries, installed programs and device drivers
- The registry keys the Registry Cleaner and Startup pages inspect
- Files and folders you point it at (Large Files, Duplicate Finder, File Tools, PDF Tools,
  Image Tools, Backup, Drive Wiper), and the temporary and cache folders Directory Cleaner
  scans
- **Browser profiles**, when you open Browser Privacy: the folders where Chrome, Edge,
  Firefox, Brave, Opera and Vivaldi keep their cache, cookies, history, form data and saved
  passwords. The App reads their sizes to show them. It deletes only what you select, after
  you confirm; it never reads the contents of your passwords, history or cookies
- **Nearby Wi-Fi networks** (names, signal, channel and security, through Windows' `netsh`),
  when you open the WiFi Analyzer. On Windows 11 this can require location permission, which
  Windows asks for; the App does not read or store your location
- **Nearby Bluetooth devices** (names and signal), when you open Bluetooth

None of this is sent anywhere.

## Network activity

The App makes no connection to the internet of its own. On your local network, and only
when you press SCAN on the Network Mapper, it:

- pings the addresses on the subnet you choose, to find which devices answer, and
- tries a short list of common ports on a device you select, to show which services it offers

To pick the adapter that carries your traffic, the Network page asks Windows which interface
it would route a public address through; this is a question to Windows, and sends no packet.

The **Donate** page opens PayPal's donation page in your web browser when you press it, and
the Driver Updater can open Windows Update; what those sites and apps do is governed by their
own policies.

## What the App keeps, and where

| What | Where |
|---|---|
| Settings | `%LocalAppData%\SysMonitor\settings.json` (for the Microsoft Store build, Windows keeps this folder inside the App's own package data) |
| History of readings for the History page, kept 30 days | A SQLite database under `%LocalAppData%\SysMonitor\` |
| Logs, for troubleshooting | `%LocalAppData%\SysMonitor\Logs\` |
| Registry backups taken before the Registry Cleaner changes anything | `%LocalAppData%\SysMonitor\RegistryBackups\` |
| Reports you export | Dashboard EXPORT: your Documents folder. Performance EXPORT CSV: your Desktop. Driver Updater: where you choose. The App names the file it wrote |
| Backups you make | The destination you choose. Encrypted backups use AES-256 with a key derived from your password, which the App does not store - except for a backup you set to repeat, whose password is kept for its runs, encrypted with Windows' data protection for your account and readable only by you on that PC |
| Backup schedules | `%LocalAppData%\SysMonitor\Backups\schedules`, and a task in Windows Task Scheduler for each |

Logs record what the App did and any errors, with file paths where an operation failed. They
do not record passwords, browser data or file contents.

## Your control

- Settings' CLEAR resets every setting.
- Deleting `%LocalAppData%\SysMonitor\` removes everything the App keeps; uninstalling
  removes the App.
- Exports and backups are ordinary files you can delete.

## Permissions

The App runs as an ordinary user. A few actions ask Windows for administrator permission when
you start them: changing machine-wide registry keys or startup entries, creating a restore
point or a system image, copying files in use from a shadow copy, and some hardware sensors. The Microsoft Store build declares the
"full trust" capability because it is a desktop application that reads hardware and manages
files.

## Children's privacy

The App collects no personal information from anyone, including children under 13.

## Changes to this policy

Changes are published in this repository with a new "Last Updated" date.

## Contact

GitHub Issues: [https://github.com/Git-Rocky-Stack/sysmonitor-windows/issues](https://github.com/Git-Rocky-Stack/sysmonitor-windows/issues)

---

*This privacy policy applies to STX-1 ULTIMATE System Monitor for Windows.*
