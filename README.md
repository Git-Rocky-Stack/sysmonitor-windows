# STX.1 System Monitor

**Strategic. Excellence. Engineered.**

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-3.0.1-blue.svg)](CHANGELOG.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%202004%2B-lightgrey.svg)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

A Windows system monitoring and optimization desktop application, built with WinUI 3
and .NET 8. **Free and open source under the MIT License** - use it, read it, change
it, ship it.

![STX.1 System Monitor dashboard](App%20Screenshots/v%202.2.0.0%20%28Microsoft%20store%29/1.png)

---

## What it is

STX.1 reads your hardware in real time, and gives you a set of maintenance tools that
report honestly about what they did. The emphasis of the current release is that second
part: if a tool says it removed something, verified something, or restored something,
that is what happened. Where a feature could not deliver what its label promised, the
feature was removed rather than reworded. See [CHANGELOG.md](CHANGELOG.md).

## What's new

Changes since v3.0.1. Each one is described in full, with the file it lives in, in
[CHANGELOG.md](CHANGELOG.md) under Unreleased.

**A new look is on its way.** STX.1 is being restyled to match System-X, its sister app,
so the two read as one family. The type comes first: text is now set in Public Sans,
and the faces the rest of the look uses (Archivo, Departure Mono and Iosevka) ship with
the app under the SIL Open Font License. The accent follows: check boxes, switches,
radio buttons, sliders, progress bars, selections and scroll bars use System-X's armed
red instead of whatever accent colour Windows is set to. High Contrast keeps your own
colours.

**Nothing is destroyed without asking.**
- Drive Wiper's WIPE NOW and Large Files' DELETE SELECTED ask first, naming how many
  items are involved and how much they hold. Cancel is the default, so Enter or Escape
  changes nothing.
- Large Files and Duplicate Finder never delete a file permanently. A file Windows
  cannot recycle, on a network or removable drive or larger than the Recycle Bin is set
  to hold, is left where it is, and the result says so. A file is only reported as moved
  once it has been found in the Recycle Bin.
- The Drive Wiper's X removes a file from the list, and nothing on the page can change
  while a wipe runs.

**What the app reports is what happened.**
- The Drive Wiper shows the result of a wipe, including overwrites it could not read
  back. Before, the result was replaced before it reached the screen.
- A missing temperature reads `--` instead of a measured-looking 32F, sizes carry one
  unit, and the game overlay labels its Fahrenheit readings F.
- The Memory page reports what a trim did, in the Dashboard's words, and shows success
  only when the trim worked.
- Every CPU reading covers its reader's own interval, so two views reading at once no
  longer shorten each other's measurement or report 0%.

**Settings are honoured everywhere.** The Microsoft Store (packaged) build saved the
Settings page to a place the rest of the app never read, so turning notifications off,
changing an alert threshold or turning minimize-to-tray off had no effect there. Both
builds now keep every setting in one file, and the Settings page says so when a save
fails.

**Fixed:** clicking the PDF editor's page with the Sticky Note tool closed the app. It
opens the note now.

## Features

Thirty-four entries in the navigation menu, grouped the way the app groups them
(`src/SysMonitor.App/MainWindow.xaml:73-252`). Settings
(`MainWindow.xaml:238`), Donate (`:243`) and the built-in User's Guide (`:248`)
sit below the groups listed here.

### Monitoring
| Page | What it does |
|---|---|
| **Dashboard** | Health score, quick-access cards, and one-click Quick Clean / Trim Memory / Export |
| **CPU Monitor** | Per-core usage, clocks and processor details |
| **GPU Monitor** | Graphics adapter load, memory and temperature |
| **Memory Monitor** | RAM usage, with working-set trimming |
| **Processes** | List, inspect and manage running processes |
| **Disk Analyzer** | Storage usage and drive health |
| **Network** | Live throughput, read from the adapter that actually carries your traffic |
| **Battery** | Charge, power state, and capacity-based health |
| **Temperature** | CPU/GPU thermal sensors and fan speeds, via LibreHardwareMonitor |
| **System Info** | Hardware and OS inventory |
| **Performance** | Operation metrics and timing, with P95/P99 and CSV export |
| **History** | 30 days of stored readings, charted |

### Maintenance
| Page | What it does |
|---|---|
| **Directory Cleaner** | Temporary files, caches and system junk |
| **Registry Cleaner** | Registry issues, with a real `reg.exe` export taken before any change |
| **Startup** | Enable and disable startup items through `Explorer\StartupApproved` - the same mechanism Task Manager uses, so changes are reversible and visible to Windows |
| **Installed Programs** | Inventory and uninstall, with the exit code reported in plain words |
| **Health Check** | System-wide checks |
| **Game Mode** | Lowers background apps below your game in the processor queue and restores them afterwards. Closing apps is a separate opt-in that only ever asks |
| **Browser Privacy** | Clears browsing traces, counted per browser found |
| **Drive Wiper** | Overwrites files with the pattern set you choose, reads the last pass back to confirm it, and warns when the target is an SSD; asks before it starts |
| **Scheduled Cleaning** | Daily, weekly or monthly cleans that run headlessly and report an exit code to Task Scheduler |
| **Backup Manager** | File backup and restore, with optional AES-256 encryption that is authenticated and restorable |
| **Driver Updater** | Device driver inventory |

### Utilities
| Page | What it does |
|---|---|
| **Large Files** | Find large files; deletion goes to the Recycle Bin after confirmation |
| **Duplicate Finder** | Duplicates decided by SHA-256 of the whole file, never by sampling; deletion goes to the Recycle Bin after confirmation |
| **File Tools** | Compress a file or folder to ZIP, or a single file to GZip, and extract archives |
| **PDF Tools** | Merge, split, convert and manipulate PDFs |
| **Image Tools** | Image conversion and processing |

### Wireless
| Page | What it does |
|---|---|
| **WiFi Analyzer** | Nearby networks, channels and signal |
| **Bluetooth** | Device discovery and status |
| **Network Mapper** | Devices on the local network |

## What it deliberately does not do

Listed because previous versions implied otherwise:

- **No app-wide keyboard shortcuts.** The app registers no `KeyboardAccelerator`. The
  only key handling is Delete, Escape and Enter inside the PDF editor.
- **No PDF text extraction or search.** The PDF editor searches annotations, not page
  text, and its report export contains annotations rather than document text.
- **No 7z or tar archives.** Compression is ZIP (file or folder) and GZip (`.gz`, a
  single file).
- **No RAM disk.** The former "RAM Cache" was a folder on disk and a variable visible
  only to this process. It was removed rather than reworded.
- **No self-updating.** The app does not check for or download new versions.
- **No guarantee of unrecoverability from the Drive Wiper on an SSD.** The drive decides
  where writes land, so overwriting a file cannot promise the flash that held it was
  written over. The page says so when it detects one.
- **No permanent deletion from Large Files or Duplicate Finder.** They only move files
  to the Recycle Bin. A file Windows cannot recycle, such as one on a network or
  removable drive or one larger than the Recycle Bin is set to hold, is left where it
  is, and the result says so.
- **No frame-rate reading on most hardware.** LibreHardwareMonitor exposes one
  frame-rate sensor and only some GPUs provide it. The overlay says `NO FPS SENSOR`
  rather than showing a zero.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- x64 processor
- 4 GB RAM (8 GB recommended)
- 300 MB disk space
- Administrator rights for hardware sensors, HKLM startup entries, and cleaning the
  Windows temp folder

The released build is self-contained - the .NET 8 and Windows App SDK runtimes ship
with it, so there is nothing to install separately.

## Documentation

The user documentation site is at
<https://git-rocky-stack.github.io/sysmonitor-windows/>, built from [`docs/`](docs/).

| Kind | Document |
|---|---|
| Tutorial | [Getting started](docs/tutorial-getting-started.md) |
| How-to | [Free up disk space](docs/howto-free-disk-space.md) |
| How-to | [Securely wipe files](docs/howto-securely-wipe-files.md) |
| How-to | [Back up and restore](docs/howto-back-up-and-restore.md) |
| How-to | [Manage startup programs](docs/howto-manage-startup-programs.md) |
| Reference | [Pages](docs/reference-pages.md) |
| Reference | [Settings and data](docs/reference-settings-and-data.md) |
| Explanation | [What v3.0.0 removed, and why](docs/explanation-honest-reporting.md) |
| Explanation | [The Drive Wiper and solid-state drives](docs/explanation-drive-wiper-and-ssds.md) |

## Installing

Download the installer from
[Releases](https://github.com/Git-Rocky-Stack/sysmonitor-windows/releases) and run it.

## Building from source

Requires Visual Studio 2022 with the Windows App SDK workload, or the .NET 8 SDK.

```bash
# Build everything
dotnet build SysMonitor.sln

# Run it
dotnet run --project src/SysMonitor.App

# Tests
dotnet test

# One test class
dotnet test --filter "FullyQualifiedName~DriveWiperPatternTests"

# Open every page in both themes, save a picture of each and a report, then exit
# (non-zero if a page failed to load; src/SysMonitor.App/Diagnostics/UiSmokeRun.cs)
dotnet run --project src/SysMonitor.App -- --ui-smoke ui-smoke

# Self-contained x64 publish
dotnet publish src/SysMonitor.App/SysMonitor.App.csproj \
  -c Release -r win-x64 --self-contained true -o publish/
```

A full release build, including the Inno Setup installer:

```powershell
.\Build-Release.ps1
```

Code signing reads a certificate from the Windows certificate store by thumbprint -
set `SYSMONITOR_SIGNING_THUMBPRINT`, or pass `-CertificateThumbprint`. No PFX file or
password is used or stored.

## Project structure

```
SysMonitor.sln
+-- src/SysMonitor.App/          # WinUI 3 front end (WinExe)
|   +-- Views/                   # 35 XAML pages + the FPS overlay window
|   +-- ViewModels/              # CommunityToolkit.Mvvm view models
|   +-- Controls/Instruments/    # Console controls, starting with the dialog helper
|   +-- Services/                # Tray icon and the FPS overlay
|   +-- Helpers/                 # Dispatcher and PDF page rendering helpers
|   +-- Converters/              # Value converters
|   +-- Diagnostics/             # The UI smoke run (--ui-smoke)
|   +-- Assets/Fonts/            # The console typefaces and their licences
|   \-- Styles/                  # Colors and styles; Console/ holds the fonts and the palette
|
+-- src/SysMonitor.Core/         # Core library
|   +-- Models/
|   +-- Helpers/
|   +-- Data/                    # EF Core context and entities (SQLite)
|   \-- Services/
|       +-- Monitors/            # CPU, GPU, Memory, Disk, Network, Battery, Temperature
|       +-- Monitoring/          # Operation timings for the Performance page
|       +-- History/             # Readings recorded for the History page
|       +-- Alerts/              # Temperature, memory and battery alerts
|       +-- Settings/            # The one settings store every part of the app reads
|       +-- Cleaners/            # Temp files, browser cache, registry, browser privacy
|       +-- Optimizers/          # Startup and memory
|       +-- Backup/              # Backup, restore, encryption
|       +-- GameMode/            # Game Mode and auto-detection
|       \-- Utilities/           # File, PDF, network and system tools
|
+-- tests/SysMonitor.Tests/      # xUnit tests
+-- scripts/                     # Font build, and the WinUI resource key list the tests use
\-- docs/                        # The user documentation site
```

Architecture notes for contributors are in [CLAUDE.md](CLAUDE.md); the end-user
reference is [FEATURES_AND_USER_GUIDE.md](FEATURES_AND_USER_GUIDE.md), and the same
guide ships inside the app under **User's Guide**.

## Built with

| Component | Version | Purpose |
|---|---|---|
| .NET | 8.0 | Runtime |
| WinUI 3 / Windows App SDK | 1.5 | UI framework |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM source generators |
| LibreHardwareMonitorLib | 0.9.3 | Hardware sensors |
| LiveChartsCore (SkiaSharp/WinUI) | 2.0.0-rc2 | Charts |
| Entity Framework Core (SQLite) | 8.0.31 | Local storage |
| PDFsharp | 6.1.1 | PDF manipulation |
| DocumentFormat.OpenXml | 3.5.1 | Office document handling |
| TaskScheduler | 2.11.0 | Windows Task Scheduler integration |
| Serilog | 4.0.0 | Logging to `%LocalAppData%\SysMonitor\Logs` |
| H.NotifyIcon.WinUI | 2.1.3 | System tray |
| Win2D | 1.2.0 | Canvas rendering |
| Public Sans, Archivo, Departure Mono, Iosevka | - | Typefaces, built by `scripts/build-fonts.py` |

Third-party license terms are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Privacy

Readings stay on your machine. See [PRIVACY_POLICY.md](PRIVACY_POLICY.md).

## Changelog

[CHANGELOG.md](CHANGELOG.md).

## License

**MIT** - see [LICENSE](LICENSE).

In plain terms: use, copy, modify, merge, publish, distribute, sublicense and sell
copies of this software, for any purpose including commercial, provided the copyright
notice and the permission notice go with it (`LICENSE:5-13`). The warranty disclaimer
is `LICENSE:15-21`. This paragraph is a summary; the file is the licence.

There is no separate end-user agreement. The MIT text at [LICENSE](LICENSE) is what the
installer presents (`installer/SysMonitorSetup.iss:39`), what installs beside the
application as `LICENSE.txt` (`installer/SysMonitorSetup.iss:87`), and the whole of the
terms.

Third-party components bundled into the self-contained build keep their own licences -
notably **LibreHardwareMonitor under MPL-2.0**, **Serilog under Apache-2.0**, and the
four typefaces under the **SIL Open Font License 1.1**, whose notice and licence ship
beside each font. These are compatible with MIT but carry their own obligations, all
listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Contributing

Issues and pull requests are welcome at
[github.com/Git-Rocky-Stack/sysmonitor-windows](https://github.com/Git-Rocky-Stack/sysmonitor-windows).
Contributions are accepted under the same MIT terms.
