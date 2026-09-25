# Getting started with STX.1 System Monitor

In about ten minutes you will install STX.1, read your machine's health score, clean
up disk space you can actually spare, and save a diagnostic report you can send to
someone. By the end you will know where the app keeps its data and how to undo
anything it did.

## What you will need

- A PC running Windows 10 version 2004 (build 19041) or later, or Windows 11
  (`src/SysMonitor.App/SysMonitor.App.csproj:5`)
- An x64 processor. The released installer is x64 only
  (`installer/SysMonitorSetup.iss:57`)
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
(`installer/SysMonitorSetup.iss:87-88`).

## Step 2: Read your health score

Launch STX.1. It opens on the Dashboard, and the first thing you see is a health
score out of 100 (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:169-170`).

The score maps to a word (`DashboardViewModel.cs:242-246`):

| Score | Reads as |
|---|---|
| 90 and above | Excellent |
| 75 to 89 | Good |
| 60 to 74 | Fair |
| 40 to 59 | Poor |
| Below 40 | Critical |

Around it are live cards for processor use, memory, disk, network, temperature and
uptime. If a card says "Not reported", that is the truth about your hardware, not an
error. Battery health in particular is computed from design capacity against
full-charge capacity, and many machines do not publish those figures to Windows at
all (`src/SysMonitor.Core/Services/Monitors/BatteryMonitor.cs`).

Run the app as administrator if temperature readings are missing. Hardware sensors
need it.

## Step 3: Reclaim some disk space

On the Dashboard, click **QUICK CLEAN**
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:273`).

It removes temporary files and browser cache, and tells you how much it freed. This
is the safe subset. Nothing it touches is a document, a download or a setting.

If you want to choose exactly what goes, use the **Directory Cleaner** page instead
and read [Free up disk space](howto-free-disk-space.md). Quick Clean is the version
that does not ask questions.

## Step 4: Save a report you can send

Still on the Dashboard, click **EXPORT**
(`DashboardViewModel.cs:325`).

The app writes a plain-text diagnostic summary into your Documents folder and tells
you the file name (`DashboardViewModel.cs:333-341`). It contains your health score,
hardware inventory and current readings. Open it before you send it to anyone, so you
know what is in it.

## What you built

You now have STX.1 installed, you have read a health score you can interpret, you have
freed disk space without guessing, and you have a report on disk.

Everything the app stores about you lives in one folder:

```
%LocalAppData%\SysMonitor
```

Logs are under `Logs` inside it (`src/SysMonitor.App/App.xaml.cs:72`). Deleting that
folder resets the application completely. Nothing is sent anywhere: readings stay on
your machine.

## Where to go next

- [Free up disk space](howto-free-disk-space.md), for the version with the controls
- [Manage startup programs](howto-manage-startup-programs.md), to make sign-in faster
- [Back up and restore](howto-back-up-and-restore.md), before you change anything big
- [Pages reference](reference-pages.md), for what every one of the 34 pages does
- [What v3.0.0 removed, and why](explanation-honest-reporting.md), if you used an
  earlier version and something you relied on is gone
