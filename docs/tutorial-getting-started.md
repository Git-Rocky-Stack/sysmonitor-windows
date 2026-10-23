# Getting started with STX-1 ULTIMATE System Monitor

In about ten minutes you will install STX-1 ULTIMATE, read your machine's health score, clean
up disk space you can actually spare, and save a diagnostic report you can send to
someone. By the end you will know where the app keeps its data and how to undo
anything it did.

## What you will need

- A PC running Windows 10 version 2004 (build 19041) or later, or Windows 11
  (`src/SysMonitor.App/SysMonitor.App.csproj:5`)
- An x64 processor. The released installer is x64 only
  (`installer/SysMonitorSetup.iss:59`)
- About 300 MB of free disk space
- An administrator account. Windows will prompt you during installation

You do not need to install .NET or any other runtime first. The release is
self-contained and carries the .NET 8 and Windows App SDK runtimes inside it
(`src/SysMonitor.App/SysMonitor.App.csproj:12`).

## Step 1: Install it

Download `STX1-SystemMonitor-Setup-3.0.1.exe` from the
[releases page](https://github.com/Git-Rocky-Stack/sysmonitor-windows/releases)
and run it.

The installer shows you the MIT licence, which is the whole of the terms. There is no
separate end-user agreement. Accept it and continue through the wizard.

The build is not code signed, so Windows SmartScreen will warn you that the publisher
is unknown. If you want to check what you downloaded before running it, compare its
SHA-256 against the checksum published on the release page:

```powershell
Get-FileHash .\STX1-SystemMonitor-Setup-3.0.1.exe -Algorithm SHA256
```

When setup finishes, the app is installed and `LICENSE.txt` and
`THIRD-PARTY-NOTICES.txt` sit next to the executable in the install folder
(`installer/SysMonitorSetup.iss:89-90`).

## Step 2: Read your health score

Launch STX-1 ULTIMATE. It opens on the Dashboard. Each section of a page is a faceplate whose
stripe names it, such as `DASH - QUICK ACCESS - 01A`, and this tutorial refers to
sections by that name.

The first faceplate, QUICK ACCESS (01A), shows a live reading each for network,
temperature, system, disk, battery and processes. Below it, SYSTEM HEALTH (01B) holds
a health score out of 100 (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:159-160`).

The score maps to a word (`DashboardViewModel.cs:229-233`):

| Score | Reads as |
|---|---|
| 90 and above | Excellent |
| 75 to 89 | Good |
| 60 to 74 | Fair |
| 40 to 59 | Poor |
| Below 40 | Critical |

Further down, SYSTEM RESOURCES (01C) has faceplate readings for processor use, memory,
disk and battery, and SYSTEM INFORMATION (01D) shows the operating system, uptime and
cleanable space. If a reading says "Not reported", that is the truth about your
hardware, not an error. Battery health in particular is computed from design capacity against
full-charge capacity, and many machines do not publish those figures to Windows at
all (`src/SysMonitor.Core/Services/Monitors/BatteryMonitor.cs`).

Run the app as administrator if temperature readings are missing. Hardware sensors
need it.

## Step 3: Reclaim some disk space

In SYSTEM HEALTH (01B), click **QUICK CLEAN**
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:260`).

QUICK CLEAN is a red cap, and a red cap asks before it acts. The question says what
will be deleted, and that log files and the Recycle Bin are left
(`DashboardViewModel.cs:262-266`). Click **QUICK CLEAN** in it to go ahead, or
**CANCEL**.

It deletes temporary files from fixed Windows locations, such as the temp folders,
the thumbnail cache and old crash dumps
(`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:73-80`), and tells you how
many files it removed. Windows log files are scanned but not pre-selected, so it
leaves them (`TempFileCleaner.cs:117`), and the Recycle Bin is never pre-selected
(`TempFileCleaner.cs:147`). This is the safe subset. Nothing it touches is a document,
a download or a setting.

If you want to choose exactly what goes, use the **Directory Cleaner** page instead
and read [Free up disk space](howto-free-disk-space.md). QUICK CLEAN is the version
with no choices to make.

## Step 4: Save a report you can send

Still in SYSTEM HEALTH (01B), click **EXPORT**
(`DashboardViewModel.cs:320`).

The app writes a plain-text diagnostic summary into your Documents folder and tells
you the file name (`DashboardViewModel.cs:328-336`). It contains your health score,
hardware inventory and current readings. Open it before you send it to anyone, so you
know what is in it.

## If you start it twice

STX-1 ULTIMATE runs as one copy. With Minimize to system tray on, which is the default, closing
the window hides it in the notification area, and starting the app again from the
Start menu brings the running copy's window back rather than starting a second one (`src/SysMonitor.App/App.xaml.cs:365-375`). Alerts
are checked by that one copy whichever page is showing, and while the window is in
the notification area (`src/SysMonitor.App/Services/TrayIconService.cs:145-149`).

## What you built

You now have STX-1 ULTIMATE installed, you have read a health score you can interpret, you have
freed disk space without guessing, and you have a report on disk.

Everything the app stores about you lives in one folder:

```
%LocalAppData%\SysMonitor
```

Logs are under `Logs` inside it (`src/SysMonitor.App/App.xaml.cs:81`). Deleting that
folder resets everything the app keeps in files. The
[settings and data reference](reference-settings-and-data.md) covers the two entries Run
at Startup and a cleaning schedule leave elsewhere, and where the packaged build keeps
the folder. Nothing is sent anywhere: readings stay on your machine.

## Where to go next

- [Free up disk space](howto-free-disk-space.md), for the version with the controls
- [Manage startup programs](howto-manage-startup-programs.md), to make sign-in faster
- [Back up and restore](howto-back-up-and-restore.md), before you change anything big
- [Pages reference](reference-pages.md), for what every one of the 34 pages does
- [What v3.0.0 removed, and why](explanation-honest-reporting.md), if you used an
  earlier version and something you relied on is gone
