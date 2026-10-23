# STX.1 System Monitor - Features & User Guide

**Strategic. Excellence. Engineered.**

Version 3.0.1 | Windows Desktop Application | Built with WinUI 3 & .NET 8

---

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Dashboard](#dashboard)
4. [System Monitoring](#system-monitoring)
5. [System Optimization](#system-optimization)
6. [Privacy & Security](#privacy--security)
7. [Utilities](#utilities)
8. [Performance and History](#performance-and-history)
9. [Settings](#settings)
10. [Donate and User's Guide](#donate-and-users-guide)
11. [Keyboard Shortcuts](#keyboard-shortcuts)
12. [Troubleshooting](#troubleshooting)

---

## Overview

STX.1 System Monitor is a comprehensive Windows system utility that provides real-time hardware monitoring (`src/SysMonitor.Core/Services/Monitors/CpuMonitor.cs:18`), system optimization (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs:18`), privacy protection (`src/SysMonitor.Core/Services/Cleaners/BrowserPrivacyCleaner.cs:61`), and productivity tools (`src/SysMonitor.Core/Services/Utilities/PdfTools.cs:12`). Its interface is the Command Console, laid out as an instrument panel so every reading and every control has a fixed place you can name.

### Key Features at a Glance

| Category | Features |
|----------|----------|
| **Monitoring** | CPU Monitor, GPU Monitor, Memory Monitor, Disk Analyzer, Battery, Temperature, Network, Processes, System Info |
| **Optimization** | Directory Cleaner, Registry Cleaner, Startup, Installed Programs, Health Check, Game Mode, Scheduled Cleaning |
| **Privacy** | Browser Privacy, Drive Wiper |
| **Utilities** | Backup Manager, Driver Updater, Large Files, Duplicate Finder, File Tools, PDF Tools and the PDF Editor, Image Tools |
| **Wireless** | Bluetooth, WiFi Analyzer, Network Mapper |
| **App** | Performance, History, Settings, Donate, User's Guide |

Section names in this guide are the names the navigation menu uses
(`src/SysMonitor.App/MainWindow.xaml:57-236`).

### Reading the Console

- **Every page opens on a view header.** Its stripe reads `MOD - <MODULE> - <NN>`, the
  module code and the page's place in the menu: `MOD - CPU - 02` is the CPU Monitor.
  Below it are the page's title and one line of description, and a page's scan and stop
  caps sit at its right
- **Every section is a faceplate.** Its stripe names the section with the module, the
  section and a letter: `CPU - CORE USAGE - 02B` is the second section of the CPU
  Monitor. This guide uses those names, so you can find a section exactly
- **Buttons are caps,** and their words are written in capitals. A red (armed) cap does
  something that cannot simply be undone, and asks before it acts. The question says
  what will happen and names what cannot be recovered. The button that does the thing
  is the red one, **CANCEL** is the plain one, and neither is the default, so Enter arms
  nothing and Escape cancels (`src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:92-93`)
- **A status is a word and a lamp.** Go is green, Hold amber, Warn a bright red that
  blinks until it is dealt with, No-Go a steady red, Exec teal for information or work
  in progress, and Off unlit (`src/SysMonitor.Core/Models/LampState.cs:13-35`)

---

## Getting Started

### System Requirements

- **OS:** Windows 10 version 2004 (build 19041) or later, or Windows 11
  (`src/SysMonitor.App/SysMonitor.App.csproj:5`, `src/SysMonitor.App/Package.appxmanifest:20`)
- **Architecture:** x64. The projects also build for x86 and ARM64, but the released
  installer is x64 only (`Build-Release.ps1:81-82`, `installer/SysMonitorSetup.iss:59`)
- **Runtime:** none to install - the release is self-contained and carries the .NET 8 and
  Windows App SDK runtimes with it (`src/SysMonitor.App/SysMonitor.App.csproj:12`)
- **RAM:** 4 GB minimum (8 GB recommended)
- **Disk Space:** 300 MB for installation

### First Launch

1. Launch STX.1 System Monitor
2. The Dashboard opens. QUICK ACCESS (01A) is at the top, and your system's health
   score is in SYSTEM HEALTH (01B) below it
3. Use the left navigation menu to access different features
4. Configure your preferences in Settings

### One Copy at a Time

The app runs as one copy. With Minimize to system tray on, which is the default,
closing the window hides it in the notification area. Starting the app again brings
that window back rather than starting a second copy
(`src/SysMonitor.App/App.xaml.cs:366-376`). Alerts are checked by that one copy
whichever page is showing, and while the window is in the notification area
(`src/SysMonitor.App/Services/TrayIconService.cs:145-149`).

---

## Dashboard

The Dashboard provides a comprehensive overview of your system's health and performance (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:19`). Its view header reads `MOD - DASH - 01`.

### Health Score

SYSTEM HEALTH (01B) shows a real-time score (0-100) indicating overall system health
(`DashboardViewModel.cs:229-233`):

| Score | Rating | Lamp | Description |
|-------|--------|------|-------------|
| 90-100 | Excellent | Go | System running optimally |
| 75-89 | Good | Go | Minor issues, no action needed |
| 60-74 | Fair | Hold | Some attention recommended |
| 40-59 | Poor | Warn | Action recommended |
| 0-39 | Critical | No-Go | Immediate attention required |

### Dashboard Faceplates

- **QUICK ACCESS (01A)** - A live reading each for network speed, temperature, system
  information, disk, battery and processes. On a machine with no battery, the battery
  reading is replaced by cleanable space
- **SYSTEM HEALTH (01B)** - The health score and the three quick actions below
- **SYSTEM RESOURCES (01C)** - CPU, memory, disk and battery use
- **SYSTEM INFORMATION (01D)** - Operating system, uptime since the last restart, and
  cleanable space

### Quick Actions

| Action | Description |
|--------|-------------|
| **QUICK CLEAN** | A red cap: it asks first. Deletes temporary files from fixed Windows locations and reports how many it removed. Windows log files and the Recycle Bin are never pre-selected, so it leaves them (`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:117`, `:147`). Browser cache is not included; that is Browser Privacy |
| **TRIM MEMORY** | Trims the working sets of background apps. Windows moves those pages to the standby list and can page them back, so this does not free RAM |
| **EXPORT** | Generates a system diagnostic report, saved to Documents |

---

## System Monitoring

### CPU Monitor

Real-time CPU performance monitoring, in PROCESSOR USAGE (02A), CORE USAGE (02B) and
SPECIFICATIONS (02C):

- **Usage Percentage** - Current CPU utilization
- **Core Count** - Physical and logical processor count
- **Clock Speed** - Current and maximum frequency
- **Temperature** - CPU temperature with status indicators
- **Per-Core Usage** - Individual core utilization (where available)

### GPU Monitor

Graphics card monitoring, in GRAPHICS CARD (03A), TEMPERATURE (03B) and TIPS (03C):

- **GPU Model** - Detected graphics adapter name, driver, VRAM and resolution
- **GPU Temperature** - Core, hot spot and memory temperatures where the card reports them
- **Status** - Cool, Normal, Warm, Hot or Critical, each with its lamp. The page's
  thresholds run higher than the Temperature page's: Cool below 122F (50C), Normal
  below 158F (70C), Warm below 185F (85C), Hot below 203F (95C), Critical above
  (`src/SysMonitor.App/ViewModels/GpuViewModel.cs:203-207`)

### Memory Monitor

RAM usage and management, in MEMORY USAGE (04A), MEMORY BREAKDOWN (04B) and VIRTUAL
MEMORY (PAGE FILE) (04C):

- **Total Memory** - Installed RAM capacity
- **Used/Available** - Current memory allocation
- **Usage Percentage** - Memory utilization level
- **Page File** - Page file size and use
- **TRIM MEMORY** - Trims the working sets of background apps. Windows moves those pages
  to the standby list and can page them straight back, so this does not free RAM
  (`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs:90-91`)

### Disk Analyzer

Storage drive information, in STORAGE OVERVIEW (17A), DRIVES (17B) and STORAGE TIPS
(17C):

- **All Drives** - View all connected storage devices
- **Space Usage** - Used/free/total for each drive, and in total
- **Usage Percentage** - Visual bar for each drive

### Battery

Power management (laptops/tablets), in BATTERY LEVEL (19A), POWER STATUS (19B) and
BATTERY HEALTH (19C):

- **Charge Level** - Current battery percentage
- **Charging Status** - Charging, Plugged In, or On Battery
- **Health Alerts** - Low and critical battery warnings, set in Settings
- **Health** - How much of its original capacity the battery still holds, from the figures Windows reports
  for it. Many machines do not report them, and the page says "Not reported" rather than guessing
- **Time Remaining** - Estimated battery life

### Temperature

Hardware temperature tracking, in PRIMARY TEMPERATURES (20A), ALL SENSORS (20B) and
THERMAL TIPS (20C):

- **CPU Temperature** - Processor thermal reading
- **GPU Temperature** - Graphics card thermal reading
- **Temperature Status** - A word and a lamp for each
- **Warning Thresholds** - Configurable alert levels, in Settings

Temperature Status Levels (`src/SysMonitor.App/ViewModels/TemperatureViewModel.cs:144-149`):

| Status | Lamp | CPU Range | GPU Range |
|--------|------|-----------|-----------|
| Cool | Exec | Below 113F (45C) | Below 113F |
| Normal | Go | 113-149F (45-65C) | 113-149F |
| Warm | Hold | 149-176F (65-80C) | 149-176F |
| Hot | Warn | 176-194F (80-90C) | 176-194F |
| Critical | No-Go | Above 194F (90C) | Above 194F |

### Network

Network connectivity and speed, in CONNECTION STATUS (18A), NETWORK SPEED (18B),
NETWORK DETAILS (18C) and NETWORK ADAPTERS (18D):

- **Connection Status** - Connected/Disconnected
- **Connection Type** - WiFi, Ethernet, etc.
- **Download Speed** - Real-time download rate
- **Upload Speed** - Real-time upload rate
- **IP Address** - Current network address
- **Adapter Name** - Active network adapter

### Processes

Running application management, in PROCESS CONTROLS (05A) and RUNNING PROCESSES (05B):

- **Process List** - All running processes, with a search box
- **CPU/Memory Usage** - Per-process resource consumption
- **Process Details** - Name, PID and status
- **END TASK** - A red cap: it asks first, naming the process and its PID, and warns
  that anything it has not saved is lost
  (`src/SysMonitor.App/ViewModels/ProcessesViewModel.cs:220-221`). It refuses the
  processes Windows cannot run without, and says whether the process ended
  (`src/SysMonitor.Core/Services/Monitors/ProcessMonitor.cs:282`)

### System Info

Detailed system specifications, in OPERATING SYSTEM (21A), PROCESSOR (21B) and MEMORY &
STORAGE (21C):

- Operating system (name, version, build, computer name, user, architecture, install
  date, uptime)
- Processor (model, cores and threads, base speed, L2 and L3 cache)
- Memory (installed, used, free)
- Storage (total, used, free)

---

## System Optimization

### Directory Cleaner

Remove junk files to free up disk space. Controls are in CLEANING CONTROLS (06A) and
the results in SCAN RESULTS (06B).

**Scan Categories:**
- Windows Temporary Files
- User Temporary Files
- Windows Prefetch Files
- Thumbnail Cache
- Windows Update Cache
- Memory Dump Files
- Windows Error Reports
- Windows Log Files (not pre-selected)
- Recycle Bin Contents (never pre-selected)
- Browser Cache (each installed browser)

The Recycle Bin is offered but never ticked for you
(`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:147`): emptying it destroys
what you deleted with a way back, so it goes only if you tick it.

**How to Use:**
1. Click **SCAN** to analyze your system
2. Review detected items by category
3. Untick items you want to keep, and tick the Recycle Bin if you mean to empty it
4. Click **CLEAN SELECTED**
5. Confirm with **CLEAN SELECTED**. The question names how many categories and how much
   space are involved, and if the Recycle Bin is ticked, says that what is in it cannot
   be restored afterwards (`src/SysMonitor.App/ViewModels/CleanerViewModel.cs:81-86`)

### Registry Cleaner

Fix Windows Registry issues. Controls are in REGISTRY CONTROLS (07A) and the results in
SCAN RESULTS (07B).

**Issue Types Detected:**
- Invalid file associations
- Orphaned software entries
- Broken shortcuts
- Obsolete startup entries
- Invalid COM/ActiveX entries

An entry counts as broken only when a fully qualified path on a mounted drive is gone,
so a program named by bare name, or an uninstall string that runs `MsiExec.exe`, is not
flagged (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs`).

**Safety Features:**
- **FIX SELECTED** is a red cap and asks first
  (`src/SysMonitor.App/ViewModels/RegistryCleanerViewModel.cs:121-123`)
- Every key a selected fix will modify is exported with `reg.exe` before anything is
  cleaned, and a failed export stops the clean
  (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs:947-949`, `:974`, `:997`)
- Each key is exported on its own, because `reg.exe` exits 0 while silently omitting
  subkeys it cannot read, so exporting a parent would hide a failure
- Selective cleaning: only the issues you tick are fixed
- To undo, use **RESTORE BACKUP**, which asks before it imports the last backup, or
  import the exported `.reg` file yourself

Before v3.0.0 the backup wrote a `.reg` file containing only comment lines, for three
fixed HKCU keys, while the cleaner deletes HKCU and HKLM values and whole subkey trees.
There was no restore code at all.

**How to Use:**
1. Click **SCAN** to analyze the registry
2. Review detected issues
3. Click **FIX SELECTED** and confirm
4. The backup is written before the first change. If the export fails, nothing is cleaned

### Startup

Control programs that launch at startup. The view header reads `MOD - BOOT - 08`.

**Features:**
- View all startup programs, including 32-bit programs set to start for every user
  (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs:68-69`)
- Enable/disable startup items the way Task Manager does, so both show the same state
- Delete an entry outright
- Impact assessment (Low/Medium/High)
- Program location and details

**Impact Levels:**
| Impact | Description |
|--------|-------------|
| High | Significantly slows boot time |
| Medium | Moderate impact on boot |
| Low | Minimal impact on boot |

**How to Use:**
1. View startup programs list
2. Select a program. **DISABLE**, **ENABLE** and **DELETE** stay dimmed until one is
   selected (`src/SysMonitor.App/Views/StartupPage.xaml:38-46`)
3. Click **DISABLE** to prevent auto-start (`src/SysMonitor.App/Views/StartupPage.xaml:39`)
4. Click **Enable** to restore auto-start: the cap reads **ENABLE** (`src/SysMonitor.App/Views/StartupPage.xaml:42`)

**DELETE** is a red cap. It removes the entry from where it is registered, and the
program stays installed. It asks first, and the question says that DISABLE keeps the
entry so it can be turned back on while DELETE does not
(`src/SysMonitor.App/ViewModels/StartupViewModel.cs:68-70`).

### Installed Programs

View and manage installed applications, in PROGRAM CONTROLS (09A) and INSTALLED
PROGRAMS (09B):

**Features:**
- Complete list of installed programs, with version, publisher and size
- Filter by Desktop Apps, Store Apps, System Apps and Frameworks
- Search by name or publisher
- Open a program's install location
- Uninstall: the cap asks first, then starts the program's own uninstaller
  (`src/SysMonitor.App/ViewModels/InstalledProgramsViewModel.cs:78-79`). Some
  uninstallers remove the program without asking again

### Scheduled Cleaning

Automate system maintenance, in SCHEDULE STATUS (14A), SCHEDULE SETTINGS (14B), WHAT TO
CLEAN (14C) and OPTIONS (14D):

- **Schedule Options** - Daily, Weekly, Monthly
- **Time Selection** - Choose when cleaning runs
- **Category Selection** - Temporary Files, Browser Cache, Recycle Bin, Windows Update
  Cache and Thumbnail Cache
- **Notification Options** - Get alerts on completion
- **SAVE SETTINGS** registers the task; **DELETE TASK** is a red cap that asks before it
  removes the task from Task Scheduler
  (`src/SysMonitor.App/ViewModels/ScheduledCleaningViewModel.cs:142-143`)

### Health Check

Comprehensive system analysis, in SYSTEM HEALTH (10A) and DETECTED ISSUES (10B):

**Checks Performed** (`src/SysMonitor.Core/Services/Utilities/HealthCheckService.cs:95-104`):
- Disk space
- Junk files
- Browser data accumulation
- Registry health
- Startup program impact
- Memory usage

**Results Include:**
- Health Score (0-100)
- Health Grade (A through F)
- Critical issues count
- Warning count
- Potential space savings
- Recommended actions

**How to Use:**
1. Click **RUN HEALTH CHECK**
2. Wait for analysis to complete
3. Review issues by severity, and tick the ones to fix. **SELECT FIXABLE** ticks every
   issue the app can fix, and **DESELECT ALL** clears them
4. Click **CREATE RESTORE POINT** before major fixes
5. Click **QUICK FIX ALL** and confirm with **QUICK FIX**. Only the ticked issues are
   fixed, and the registry is backed up before it is touched; if the backup fails, the
   registry is left alone (`src/SysMonitor.App/ViewModels/HealthCheckViewModel.cs:91-93`)

### Game Mode

Make room for a game, in CONTROL (11A), ADVANCED FEATURES (11B), SYSTEM MONITORING
(11C), WHEN ENABLED (11D), LAST SESSION (11E) and TARGET APPLICATIONS (11F).

**When Enabled** Game Mode:
- **Lowers Background Apps** - Browsers, Discord, Spotify and the rest keep running,
  below the game in the processor queue, and are put back afterwards
- **High Performance Mode** - Sets Windows to the High Performance power plan. If the
  app ends without switching Game Mode off, the previous plan is put back the next time
  it starts (`src/SysMonitor.App/App.xaml.cs:387-391`)
- **Trims Memory** - Trims background app working sets; Windows can page them back

Asking the apps to close is a separate opt-in: tick "Ask them to close instead (may
lose unsaved work)". Game Mode then names the apps and asks before it asks any of them
to close, and anything that declines keeps running
(`src/SysMonitor.App/Views/GameModePage.xaml.cs:72-80`).

**Other Controls:**
- **ENABLE GAME MODE** / **DISABLE** - The main cap. While Game Mode is on its state is
  armed, because the app is acting on the machine
- **AUTO MODE** - Starts Game Mode when it sees a game launch
- **PROFILES** - Optimization presets, with **APPLY** and **CLEAR**
- **GAME OVERLAY** - **SHOW** and **HIDE** an overlay of temperatures, load and power on
  top of your game. Frame rate needs a GPU that reports it
- **SYSTEM MONITORING** - Fan speeds and power draw, where the hardware exposes them
- **LAST SESSION** - What the last session trimmed and which apps it closed

---

## Privacy & Security

### Browser Privacy

Clean browser data across all installed browsers. Controls are in PRIVACY CONTROLS
(12A) and the data found in BROWSER DATA (12B).

**Supported Browsers:**
- Google Chrome
- Mozilla Firefox
- Microsoft Edge
- Opera
- Brave
- Vivaldi

**Data Types** (`src/SysMonitor.Core/Services/Cleaners/BrowserPrivacyCleaner.cs`):
| Type | Risk Level | Description |
|------|------------|-------------|
| Cache | Safe | Temporary internet files |
| History | Low | Browsing history. Firefox keeps history and bookmarks in one file, so there it is High |
| Cookies | Medium | Session and tracking data. Clearing them signs you out of sites |
| Passwords | High | Saved login credentials |
| Form Data | High | Auto-fill information (Medium for Firefox) |

**Risk-Based Filtering:**
- Toggle visibility by risk level: Safe (Cache), Low Risk (History), Medium Risk
  (Cookies) and High Risk (Passwords)
- **SELECT SAFE ONLY** ticks only the safe items
- High-risk items require explicit selection

**How to Use:**
1. Click **SCAN** to detect browser data
2. Filter by risk level using toggles
3. Select items to remove
4. Click **CLEAN SELECTED**
5. Confirm with **DELETE**. The question names what cannot be recovered, such as saved
   passwords, before anything is deleted
   (`src/SysMonitor.App/ViewModels/BrowserPrivacyViewModel.cs:94`). The result counts
   what was actually deleted, per browser

### Drive Wiper

Overwrite a file's contents so the old data is gone from the disk. Read the limits
below before relying on it. Controls are in SECURE DELETION (13A) and the list in FILES
TO WIPE (13B).

**Wipe Methods** (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs:27-32`, pass
counts at `:511-518`):

| Method | Passes | Speed |
|--------|--------|-------|
| Quick (1-Pass) | 1 | Fast |
| DoD 3-Pass | 3 | Medium |
| DoD 7-Pass | 7 | Slow |
| Gutmann 35-Pass | 35 | Very Slow |

The Gutmann passes follow the table in Peter Gutmann's 1996 paper
(`DriveWiper.cs:574-579`). Before v3.0.0 the middle passes were filled with
`(pass * 17) % 256`, which is not that method.

**How It Works:**
- Overwrites file data with the pattern set you chose
- Every wipe reads its last pass back off the disk and compares it against the pattern
  that should be there (`DriveWiper.cs:406`, `:602`). A file that cannot be confirmed is
  reported as **unconfirmed** rather than counted as wiped
- Directory wipes stay inside the folder you chose, and do not follow junctions or
  symbolic links out of it
- Works on files and folders, with a progress indicator

**What it does not guarantee:**

On a solid-state drive, overwriting a file cannot promise the flash cells holding the
old contents were written over. The drive's controller decides where writes physically
land, and wear levelling actively avoids reusing the cells your file was on. The page
says so when your selection sits on an SSD (`DriveWiper.cs:20`, `:309`, `:334`).

If you need a guarantee on an SSD, the options are full-disk encryption applied before
the data existed, the drive's own secure-erase command, or physical destruction.
Overwriting a single file is not one of them. See
[the explanation](docs/explanation-drive-wiper-and-ssds.md).

**How to Use:**
1. Click **ADD FILES** or **ADD FOLDER**. **CLEAR ALL** empties the list without
   touching anything on disk
2. Select wipe method based on sensitivity
3. Click **WIPE NOW**
4. Confirm with **WIPE**. The question names how many files and folders are listed and how
   much they hold, and says none of it goes to the Recycle Bin. **WIPE** is armed red,
   **CANCEL** is plain, and neither is the default
   (`src/SysMonitor.App/Views/DriveWiperPage.xaml.cs:37`). This is not reversible
5. Read the result. "Wiped" means the read-back matched. "Unconfirmed" means it did not,
   and you should treat the data as possibly still present

---

## Utilities

### Backup Manager

Create and manage file backups, in QUICK ACTIONS (15A), HISTORY (15B), SYSTEM RESTORE
POINTS (15C) and STATUS (15D).

**Quick Actions:**
- **NEW BACKUP** - Start the backup wizard
- **RESTORE POINT** - Create a system restore point. A created restore point is
  confirmed by Windows giving it a new sequence number, since Windows reports success
  without making one within 24 hours of the last
- **QUICK BACKUP** - One-click Documents backup

**Backup Types:**
- **FULL BACKUP** - Complete copy of selected files
- **INCREMENTAL BACKUP** - Only files new or changed since the previous backup with the
  same name, full or incremental (`src/SysMonitor.Core/Services/Backup/BackupService.cs:125`).
  Restoring one rebuilds every file as it was then, from the full backup and each
  incremental after it; a backup a later incremental builds on cannot be deleted first
- **SYSTEM IMAGE** - Offered by the wizard. In this version it copies everything you
  selected, as a full backup does

**Destination Options:**
- Local Drive
- External Drive
- Network Location

**Features:**
- Encryption support (AES-256)
- Compression options (None, Normal, Maximum)
- Volume Shadow Copy (VSS) support
- Backup verification
- Retention policies (keep last N backups). Retention runs only after a complete
  backup, and never removes the full backup later ones depend on
- Include/exclude patterns
- Hidden files option

**Wizard Steps:**
1. **Select Type** - Choose backup type
2. **Select Source** - Add folders to back up, or pick DOCUMENTS, PICTURES, VIDEOS,
   DESKTOP or DOWNLOADS
3. **Select Destination** - Choose where to save
4. **Options** - Configure encryption and compression
5. **Running** - Progress display, with **CANCEL BACKUP**
6. **Complete** - Results summary

Move between steps with **NEXT** and **BACK**, and leave with **CANCEL**.

**Backup History:**
- View all completed backups
- Restore from any backup. The app asks first, naming the files and the folders they
  go back to
- Delete old backups. The delete cap is red and asks first
- Verify backup integrity

### Driver Updater

Scan and manage system drivers, in DRIVER STATISTICS (16A), QUICK ACTIONS (16B),
ATTENTION REQUIRED (16C) and ALL DRIVERS (16D):

**Features:**
- Full driver inventory scan
- Problem driver detection
- Drivers older than two years flagged by date
- Unsigned driver warnings
- Device Manager integration
- Windows Update access

**Driver Status:**
| Status | Lamp | Description |
|--------|------|-------------|
| Up to Date | Go | Signed, no problem reported, and the driver date is within the last two years |
| Outdated | Hold | Driver date is more than two years old; the row carries an OUTDATED badge (`src/SysMonitor.Core/Services/Utilities/DriverUpdater.cs:100-101`) |
| Problem | No-Go for most problems | Windows reports a problem with the device; the row carries a PROBLEM badge. The status chip names the problem and takes its own lamp: Hold for a configuration or resource problem, Exec for a restart required or a device prepared for removal, Off for a device that is disabled, absent or disconnected (`DriverUpdater.cs:223-261`) |
| Unsigned | Hold | Not digitally signed; the row's chip reads UNSIGNED (`src/SysMonitor.App/ViewModels/DriverUpdaterViewModel.cs:211-212`) |

The app does not compare your drivers against any vendor catalogue, so **Outdated means
the driver is old, not that a newer one exists**. Use Windows Update or the device
maker's own tool to find out whether there is a newer one.

**How to Use:**
1. Click **SCAN** in the view header to inventory, and **STOP** to end it early
2. Review driver list and status
3. Check ATTENTION REQUIRED (16C) for the problem, outdated and unsigned drivers
4. Use **DEVICE MANAGER** for updates
5. Use **WINDOWS UPDATE** for official drivers
6. Use **EXPORT REPORT** to save the driver inventory

### PDF Tools

Comprehensive PDF management. Choose an operation in SELECT OPERATION (25A).

**Merge PDFs** (25B):
1. Click **ADD PDFS** to add multiple PDF files
2. Arrange order with up/down buttons
3. Click **MERGE PDFS** to combine
4. Save the merged PDF. A merge with a part that cannot be read is refused rather than
   saved without it

**Split PDFs** (25D):
- Split all pages into separate files
- Split by page range

**Extract Pages** (25E):
- Select start and end pages
- Extract to new PDF file

**Convert to PDF** (25G):
- Convert images (JPG, PNG, BMP, GIF, TIFF, WebP) to PDF
- Convert text files (TXT, CSV, XML, JSON, MD, HTML) to PDF
- Office documents, where Microsoft Office is installed

**Add Signature** (25F):
- Draw signature with mouse/touchscreen
- Upload signature image
- Type signature in cursive font
- Add signer name and date
- Position signature on page, then **APPLY SIGNATURE**

**EDIT PDF** opens the PDF Editor.

### PDF Editor

Edit a PDF page by page. The PDF Editor is not on the navigation menu: open it with
**EDIT PDF** on the PDF Tools page. Its view header reads `MOD - PDF EDIT - 35`.

**File / Edit:**
- **OPEN** and **SAVE** a PDF, print it, and compress it
- **REPORT** - Export a Word report of the pages and their annotations
- Undo and redo
- **FIND IN ANNOTATIONS** - Searches the notes, sticky notes and stamps on the document.
  The page text itself is not searched

**Annotate:**
- Select, Pen, Text Box, Highlight, Rectangle, Ellipse, Line, Arrow, Sticky Note and
  Signature tools
- Insert Image, Add Stamp and Add Watermark
- Redact. On save, the page becomes an image and whatever is under the box is gone from
  the file
- Colour, size, stroke, bold and italic

**Pages:**
- Rotate **LEFT** and **RIGHT**, **MOVE UP** and **MOVE DOWN**, **INSERT** a blank page
  after the current one, and **DUPLICATE** it
- **DELETE PAGE** - A red cap: it asks first. The page and everything drawn on it leave
  the document
- **CLEAR ALL ANNOTATIONS** - A red cap: it asks first, then removes the annotations on
  the current page

Nothing changes on disk until you save. Keyboard shortcuts are listed under
[Keyboard Shortcuts](#keyboard-shortcuts).

### Image Tools

Image processing and optimization. Select images in SELECT IMAGES (26A) with **SELECT
IMAGE** or **SELECT MULTIPLE**.

**COMPRESS:**
- Reduce file size without visible quality loss
- Adjustable compression quality (1-100%)
- Batch processing support

**CONVERT:**
- Target formats: JPEG, PNG, BMP, GIF, WebP, ICO
  (`src/SysMonitor.Core/Services/Utilities/IImageTools.cs:19-27`)
- Quality settings for lossy formats
- Batch conversion

**RESIZE:**
- Set custom dimensions
- Maintain aspect ratio option

**Image Info:**
- Dimensions (width x height)
- Color depth
- DPI resolution
- File format details

### File Tools

File compression and archiving, in SELECT FILE OR FOLDER (24A), COMPRESSION (24B) and
COMPRESSION TIPS (24C):

**Compression Formats** (`src/SysMonitor.Core/Services/Utilities/IUtilities.cs:94-98`)**:**
- **ZIP** - a file or a whole folder (`FileConverter.cs:216-239`)
- **GZip** (`.gz`) - a single file only. This is plain gzip, not a tar archive, so it
  compresses one file rather than bundling several (`FileConverter.cs:242-247`)

7z is not offered: the code behind it wrote a zip file with a different name.

**EXTRACT** unpacks an archive. It never overwrites a file of the same name beside it.

**How to Use:**
1. Click **SELECT FILE** or **SELECT FOLDER**
2. Choose compression format
3. Click **COMPRESS**
4. View compression results, and use **OPEN OUTPUT FOLDER**

### Duplicate Finder

Find and remove duplicate files, in DUPLICATE ANALYSIS (23A) and DUPLICATES FOUND (23B):

**Features:**
- Duplicates are decided by SHA-256 of the whole file
  (`src/SysMonitor.Core/Services/Utilities/DuplicateFinder.cs:222-224`). Files are
  grouped by size first, but that is only a pre-filter: nothing is called a duplicate
  without a full-file hash match
- One physical file is counted once, so a file reached by two paths is not reported as
  a pair with itself
- The oldest copy is kept (`DuplicateFinder.cs:161`)
- Filter by file type and by minimum size
- Preview duplicates before deletion
- Deletion goes to the Recycle Bin, after a confirmation naming the count and size
  (`DuplicateFinder.cs:16-23`, `:186`). A file Windows cannot recycle, such as one on a
  network or removable drive or one larger than the Recycle Bin is set to hold, is left
  where it is, and the result says how many and why
  (`src/SysMonitor.Core/Services/Utilities/RecycleBin.cs:136`)

There is no faster name-and-size mode. Before v3.0.0, files over 10 MB were judged by
their first megabyte, last megabyte and length, which matches for every file a program
writes with the same header, footer and size, and those were offered up for deletion.

**How to Use:**
1. Click **BROWSE** to select a folder to scan
2. Click **SCAN**
3. Review duplicate groups
4. Select duplicates to remove (the oldest copy is kept), or use **SELECT ALL
   DUPLICATES** and **SELECT NONE**
5. Click **DELETE SELECTED** and confirm with **MOVE TO RECYCLE BIN**

### Large Files

Locate space-consuming files, in SCAN RESULTS (22A) and FILES FOUND (22B):

**Features:**
- Scan any drive or folder, down to a minimum size you set
- Results listed largest first
  (`src/SysMonitor.Core/Services/Utilities/LargeFileFinder.cs:163`)
- Select files one by one, or all or none at once
- Deletion goes to the Recycle Bin, after a confirmation naming the count and size
  (`src/SysMonitor.App/Views/LargeFilesPage.xaml.cs:31`). A file Windows cannot
  recycle, such as one on a network or removable drive or one larger than the Recycle
  Bin is set to hold, is left where it is, and the result says how many and why

**How to Use:**
1. Click **BROWSE** to select a drive or folder
2. Set the minimum file size
3. Click **SCAN**
4. Tick the files to remove, or use **SELECT ALL** and **SELECT NONE**
5. Click **DELETE SELECTED** and confirm with **MOVE TO RECYCLE BIN**

### Network Mapper

Discover devices on your network, in LOCAL NETWORK INFO (29A), SCAN SETTINGS &
STATISTICS (29B), DISCOVERED DEVICES (29C) and DEVICE DETAILS (29D):

**Features:**
- Scan the local network with **SCAN**
- Identify connected devices, with hostname, response time and manufacturer
- Show IP and MAC addresses
- Device type detection
- **SCAN PORTS** and **REFRESH INFO** for the selected device

### WiFi Analyzer

Wireless network analysis, in CONNECTION STATUS (28A), NETWORK STATISTICS (28B) and
AVAILABLE NETWORKS (28C):

**Features:**
- View available networks with **SCAN**
- Current connection details
- Signal strength, channel and frequency
- Security type

The page lists networks; it does not connect to them. When Windows 11 will not let it
scan, the page says which permission to turn on: Location, under Settings > Privacy &
security.

### Bluetooth

Bluetooth device listing, in ADAPTER INFO (27A), STATISTICS (27B) and DISCOVERED
DEVICES (27C):

**Features:**
- View paired devices
- Discover nearby devices with **SCAN**
- Signal strength and connection state
- Adapter information

The page lists devices; it does not connect or pair them.

---

## Performance and History

### Performance

How long the app's own operations take, in MONITORING CONTROLS (30A), SYSTEM OVERVIEW
(30B) and OPERATION METRICS (30C). The view header reads `MOD - PERF - 30`.

- **MONITORING / PAUSED** - A chip shows whether metrics are being collected, beside a
  **PAUSE** or **RESUME** cap
- **SYSTEM OVERVIEW** - The app's working set, a garbage-collection score, and how many
  operations are tracked
- **OPERATION METRICS** - Count, average, minimum, maximum, P95 and P99 time per
  operation, each with a status: Excellent under 16 ms, Good under 50 ms, Fair under
  100 ms, Poor under 200 ms, and Critical above
- **CLEAR** empties the metrics, and **EXPORT CSV** writes them to a file on your
  Desktop. If Windows refuses the write, the page says so
  (`src/SysMonitor.App/ViewModels/PerformanceViewModel.cs:174`)

### History

Your system's readings over time, in TIME RANGE (31A), CPU USAGE (%) (31B), MEMORY
USAGE (%) (31C) and TEMPERATURE (F) (31D). The view header reads `MOD - HIST - 31`.

- Choose 1 Hour, 24 Hours, 7 Days or 30 Days, and click **REFRESH**
- The app records a reading every 30 seconds while it runs and keeps 30 days of them
  (`src/SysMonitor.Core/Services/History/HistoryService.cs:36-38`)
- The history lives in `%LocalAppData%\SysMonitor\history.db`, on your machine

---

## Settings

The view header reads `MOD - SET - 32`.

### Appearance (32A)

- **Theme** - System Default, Light, or Dark (Strategia-X), the default. Day Shift is the
  console's light shift and Night Ops its dark one; System Default follows Windows' own
  light or dark app setting. The choice applies when you press SAVE SETTINGS, and every
  time the app starts (`src/SysMonitor.App/MainWindow.xaml.cs:182`)

### Behavior (32B)

- **Run at Startup** - Launch app when Windows starts. The page labels it "Run at Windows startup"
- **Minimize to Tray** - Keep running in system tray. The page labels it "Minimize to system tray"
- **Show Notifications** - Enable/disable alerts (`src/SysMonitor.App/Views/SettingsPage.xaml:76`). The page labels it "Show notifications". Alerts fire whichever page is showing, and while the window is in the tray

### Monitoring (32C)

- **Refresh Interval** - Saved, but no page reads it yet: each page refreshes on a fixed timer of its own,
  and stops when you move to another page (`src/SysMonitor.App/ViewModels/SettingsViewModel.cs:88`)
- **Warn me when memory usage goes above** - The usage level that raises a memory alert. Crossing it
  trims nothing. Working sets are trimmed only by TRIM MEMORY on the Dashboard or the Memory page, and
  by Game Mode - including when auto mode, if you have turned it on, starts Game Mode because it saw a
  game launch.

### Alert Thresholds (32D)

The page shows temperatures in degrees F.

**Temperature Alerts:**
- CPU Warning Temperature (default: 167F / 75C)
- CPU Critical Temperature (default: 194F / 90C)
- GPU Warning Temperature (default: 176F / 80C)
- GPU Critical Temperature (default: 203F / 95C)

**Battery Alerts:**
- Low Battery Warning (default: 20%)
- Critical Battery Warning (default: 10%)

### Data & Privacy (32E)

- **Clear All Data** - **CLEAR** is a red cap: it asks first, then resets every setting
  to its default and saves that straight away. The history database and logs are kept
- **Privacy Notice** - All data stays local on your device

### Saving

**SAVE SETTINGS** saves the page. **RESET TO DEFAULTS** is a red cap: it asks first, and
puts every setting on the page back to its default without saving until you press
**SAVE SETTINGS** (`src/SysMonitor.App/ViewModels/SettingsViewModel.cs:145-147`).
ABOUT (32F) shows the version of the build you are running.

---

## Donate and User's Guide

### Donate

Support the project, in HELP SUPPORT THIS PROJECT (33A), MAKE A DONATION (33B) and
THANK YOU (33C). **DONATE WITH PAYPAL** opens PayPal's donation page in your browser, and
the page also shows a QR code to scan with the PayPal app. The app itself handles no
payment details.

### User's Guide

The guide inside the app, in GETTING STARTED (34A), FEATURE GUIDE (34B), TIPS & BEST
PRACTICES (34C) and ABOUT THIS BUILD (34D). ABOUT THIS BUILD shows the date the build was
made and what it does differently from v3.0.1.

---

## Keyboard Shortcuts

There are no application-wide keyboard shortcuts. The app registers no
`KeyboardAccelerator` anywhere, so there is no key that opens a page, refreshes a
page or cancels a running operation - use the navigation menu and the on-screen
buttons.

Every key the app handles is in the **PDF Editor** (`src/SysMonitor.App/Views/PdfEditorPage.xaml.cs:84-141`, `:391-398`, `:1431-1443`):

| Key | Where | Action |
|-----|-------|--------|
| `Ctrl+O` | Editor | Open a PDF (`:94`) |
| `Ctrl+S` | Editor | Save the PDF (`:95`) |
| `Ctrl+P` | Editor | Print (`:96`) |
| `Ctrl+Z` | Editor, outside a text field | Undo (`:97`) |
| `Ctrl+Y` | Editor, outside a text field | Redo (`:98`) |
| `V` | Editor, with a document open | Select tool (`:67`) |
| `P` | Editor, with a document open | Pen tool (`:68`) |
| `T` | Editor, with a document open | Text tool (`:69`) |
| `H` | Editor, with a document open | Highlight tool (`:70`) |
| `R` | Editor, with a document open | Rectangle tool (`:71`) |
| `E` | Editor, with a document open | Ellipse tool (`:72`) |
| `L` | Editor, with a document open | Line tool (`:73`) |
| `A` | Editor, with a document open | Arrow tool (`:74`) |
| `N` | Editor, with a document open | Sticky Note tool (`:75`) |
| `S` | Editor, with a document open | Signature tool (`:76`) |
| `X` | Editor, with a document open | Redact tool (`:77`) |
| `I` | Editor, with a document open | Insert an image (`:131-135`) |
| `Delete` | Editor canvas | Delete the selected annotation (`:104-108`) |
| `Esc` | Editor canvas | Clear the current selection (`:109-130`) |
| `Enter` | Search box | Run the annotation search (`:391-398`) |
| `Enter` | Text annotation box | Commit the text annotation (`:1433-1437`) |
| `Esc` | Text annotation box | Cancel the text annotation (`:1438-1442`) |

The letters are ignored while a text field has the keyboard, where they are the words
being typed. The zoom caps' tooltips name `-`, `+` and `0`, but the editor does not
handle those keys; use the caps.

---

## Troubleshooting

### App Won't Start

1. Nothing needs installing beside it: the installer and the portable zip carry the .NET 8 and
   Windows App SDK runtimes themselves
2. If nothing appears, the app may already be running in the notification area. Starting it
   again brings that window back
3. Run as Administrator
4. Check Windows Event Viewer for errors, and `%LocalAppData%\SysMonitor\Logs` for the app's own log

### Temperature Readings Show -- or N/A

1. Ensure LibreHardwareMonitor drivers are loaded
2. Run app as Administrator
3. Some hardware may not support temperature sensors

### Cleaner Not Finding All Files

1. Some system files require Administrator privileges
2. Locked files (in use) cannot be deleted
3. Antivirus may protect certain files

### Settings Not Saving

1. The app keeps every setting in `%LocalAppData%\SysMonitor\settings.json`, in the installed and the
   Microsoft Store builds alike (`src/SysMonitor.Core/Services/Settings/SettingsStore.cs:56`)
2. When a save fails, the Settings page says "Settings could not be saved; the log says why", and the
   reason is in `%LocalAppData%\SysMonitor\Logs`
3. Ensure write permissions to that folder, and check for disk space issues

### High CPU Usage

1. Move off a monitoring page you are not watching: a page reads only while it is on screen, and
   stops when you go to another. The watch-only pages also stop while the window is hidden in the tray
2. Hide the game overlay when you are not playing: it reads the sensors twice a second
   (`src/SysMonitor.App/Services/FpsOverlayService.cs:26`)
3. Turn off Game Mode's auto mode if you do not use it: it watches for games starting

### Network Features Not Working

1. Ensure Windows Firewall allows the app
2. Check network adapter status
3. Run as Administrator for full network access

---

## Support

For issues and feature requests:
- GitHub: [Report Issues](https://github.com/Git-Rocky-Stack/sysmonitor-windows/issues)

---

**STX.1 System Monitor**
*Strategic. Excellence. Engineered.*

Copyright (c) 2024-2026 Strategia / Rocky Stack.

Released under the [MIT License](LICENSE). In plain terms: use, copy, modify, merge,
publish, distribute, sublicense and sell copies of this software, provided the
copyright notice and permission notice go with it (`LICENSE:5-13`).

This summary is not the licence. The binding grant and the warranty disclaimer are
`LICENSE:5-13` and `LICENSE:15-21`.

Third-party components bundled with the application keep their own licences, listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
