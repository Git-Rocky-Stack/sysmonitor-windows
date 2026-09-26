# Reference: pages

Every entry in the navigation menu of STX.1 System Monitor v3.0.1. There are 34,
verified by counting `NavigationViewItem` entries in
`src/SysMonitor.App/MainWindow.xaml`.

The app defines three group headers (`MainWindow.xaml:119`, `:182`, `:209`). The nine
entries above the first header, and the five below the Wireless group, are not under a
header of their own. They are listed here in the order the menu shows them.

## Top of the menu

| Page | Line | What it does |
|---|---|---|
| Dashboard | `:73` | Health score out of 100, live cards, and three buttons: QUICK CLEAN, TRIM MEMORY and EXPORT (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:273`, `:298`, `:325`). The middle button read BOOST RAM up to v3.0.0 and was renamed in v3.0.1, see [Settings and data](reference-settings-and-data.md) |
| CPU Monitor | `:78` | Processor usage, core counts, clock speeds, per-core figures where the hardware reports them |
| GPU Monitor | `:83` | Graphics adapter load, memory and temperature |
| Memory Monitor | `:88` | RAM in use and available, and working-set trimming |
| Processes | `:93` | Running processes with per-process processor and memory use, and the ability to end one |
| Directory Cleaner | `:98` | Scans thirteen categories of reclaimable files and removes the ones you select (`src/SysMonitor.Core/Models/CleanerModels.cs:56-70`) |
| Registry Cleaner | `:103` | Finds registry issues. Exports every key a selected fix will touch with `reg.exe` before changing anything, and a failed export stops the clean (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs:913-915`, `:940`, `:973`) |
| Startup | `:108` | Enables and disables startup entries through `Explorer\StartupApproved`, the mechanism Task Manager uses (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs:12`, `:52-53`) |
| Installed Programs | `:113` | Inventory and uninstall. Exit codes are reported in words, including restart-needed and already-removed, which are not failures |

## Maintenance

Header at `MainWindow.xaml:119`.

| Page | Line | What it does |
|---|---|---|
| Health Check | `:120` | System-wide checks producing a score, a grade, counts of critical issues and warnings, and recommended actions |
| Game Mode | `:125` | Lowers background apps below your game in the processor queue and puts them back afterwards. Asking apps to close is a separate opt-in that only ever asks (`src/SysMonitor.Core/Services/GameMode/GameModeService.cs`) |
| Browser Privacy | `:130` | Clears browsing traces across installed browsers, counted per browser actually found |
| Drive Wiper | `:135` | Overwrites files with the pattern set you choose, reads the last pass back to confirm it, and warns when the target is an SSD (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs:406`, `:589`, `:20`). It asks before it starts, naming the count and size, with Cancel as the default (`src/SysMonitor.App/Views/DriveWiperPage.xaml.cs:35`) |
| Scheduled Cleaning | `:140` | Daily, weekly or monthly cleans that run headlessly and return an exit code to Task Scheduler (`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningRun.cs`) |
| Backup Manager | `:145` | Full, incremental and differential backups, with optional AES-256 encryption that is authenticated and restorable (`src/SysMonitor.Core/Services/Backup/BackupEncryption.cs:20-22`) |
| Driver Updater | `:150` | Device driver inventory, with problem and unsigned drivers flagged, and links out to Device Manager and Windows Update |
| Disk Analyzer | `:156` | Storage use per drive and drive health where SMART is available |
| Network | `:161` | Live throughput read from the adapter that actually carries your traffic, selected by routed interface rather than claimed link speed (`src/SysMonitor.Core/Services/Monitors/NetworkMonitor.cs`) |
| Battery | `:166` | Charge, power state, and health from design capacity against full-charge capacity. Reports "Not reported" when Windows does not supply those figures (`src/SysMonitor.Core/Services/Monitors/BatteryMonitor.cs`) |
| Temperature | `:171` | Processor and graphics thermal sensors and fan speeds, through LibreHardwareMonitor |
| System Info | `:176` | Hardware and operating system inventory |

## Utilities

Header at `MainWindow.xaml:182`.

| Page | Line | What it does |
|---|---|---|
| Large Files | `:183` | Finds large files, largest first. Deletion goes to the Recycle Bin after a confirmation naming the count and size, and does not follow links; a file Windows cannot recycle is left where it is, and the result says so (`src/SysMonitor.App/Views/LargeFilesPage.xaml.cs:31`, `src/SysMonitor.Core/Services/Utilities/RecycleBin.cs:136`) |
| Duplicate Finder | `:188` | Duplicates decided by SHA-256 of the whole file, never by sampling. One physical file is counted once, the oldest copy is kept, and deletion goes to the Recycle Bin after a confirmation naming the count and size; a file Windows cannot recycle is left where it is, and the result says so (`src/SysMonitor.Core/Services/Utilities/DuplicateFinder.cs:161`, `:224`) |
| File Tools | `:193` | ZIP compression of a file or a folder, and GZip of a single file. See the note on formats below |
| PDF Tools | `:198` | Merge, split, extract pages, convert images and text to PDF, and sign |
| Image Tools | `:203` | Compress, convert, resize, and read image metadata |

## Wireless

Header at `MainWindow.xaml:209`.

| Page | Line | What it does |
|---|---|---|
| Bluetooth | `:210` | Paired and discoverable device listing |
| WiFi Analyzer | `:215` | Nearby networks, channels, signal strength and security type |
| Network Mapper | `:220` | Devices on the local network with IP and MAC addresses |

## Below the groups

These five have no header of their own.

| Page | Line | What it does |
|---|---|---|
| Performance | `:228` | Operation metrics and timing, with P95 and P99 figures and CSV export |
| History | `:233` | Thirty days of stored readings, charted |
| Settings | `:238` | See [Settings and data](reference-settings-and-data.md) |
| Donate | `:243` | Support the project |
| User's Guide | `:248` | The full guide, inside the app |

## Compression formats

File Tools offers two formats
(`src/SysMonitor.Core/Services/Utilities/IUtilities.cs:94-98`):

- **ZIP**, for a file or a whole folder
- **GZip** (`.gz`), for a single file only. This is plain gzip, not a tar archive, so
  it compresses one file rather than bundling several

7z is not offered. It was removed in v3.0.0 because the code behind it wrote a ZIP and
changed the extension, so asking for 7z produced a ZIP named `.7z`.

## Keyboard input

There are no application-wide keyboard shortcuts. The app registers no
`KeyboardAccelerator` anywhere. All five keys it handles are in the PDF editor
(`src/SysMonitor.App/Views/PdfEditorPage.xaml.cs:63-74`, `:314-321`, `:1360-1372`):

| Key | Where | Action |
|---|---|---|
| Delete | Editor canvas | Delete the selected annotation |
| Esc | Editor canvas | Clear the current selection |
| Enter | Search box | Run the annotation search |
| Enter | Text annotation box | Commit the text annotation |
| Esc | Text annotation box | Cancel the text annotation |

Note that the PDF editor's search looks at annotations, not page text. The app does
not extract or search text inside a PDF.

## Related

- [Settings and data](reference-settings-and-data.md)
- [What v3.0.0 removed, and why](explanation-honest-reporting.md)
