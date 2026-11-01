# Reference: pages

Every entry in the navigation menu of STX-1 ULTIMATE System Monitor v3.1.1. There are 34,
verified by counting `NavigationViewItem` entries in
`src/SysMonitor.App/MainWindow.xaml`. A 35th page, the PDF Editor, is not on the menu:
it opens from the EDIT PDF cap on the PDF Tools page
(`src/SysMonitor.App/Views/PdfToolsPage.xaml:48`).

The app defines three group headers (`MainWindow.xaml:103`, `:166`, `:193`). The nine
entries above the first header are not under a header of their own, and the five at
the bottom are the menu's footer (`MainWindow.xaml:211`). In the tables, Line is the
entry's line in `MainWindow.xaml`. They are listed in the order the menu shows them.

## How a page is laid out

Every page opens on a view header whose stripe reads `MOD - <MODULE> - <NN>`: the
module code in the Module column below, and the page's place in the menu
(`src/SysMonitor.App/Controls/Instruments/ViewHeader.cs:134`). The header carries the
page's title and one line of description, and a page's scan and stop caps sit at its
right.

Below it, each section is a faceplate whose stripe names it with the module, the
section and a letter: `CPU - CORE USAGE - 02B` is the second section of module 02.
Those names are given here and in the other documents so you can point to a section
exactly.

Buttons are caps, and their words are written in capitals. A red cap does something
that cannot simply be undone, and asks first: the question says what will happen and
what cannot be recovered, Enter arms nothing and Escape cancels
(`src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:92-93`).

## Top of the menu

| Page | Line | Module | What it does |
|---|---|---|---|
| Dashboard | `:57` | `DASH 01` | QUICK ACCESS (01A) shows a live reading each for network, temperature, system, disk, battery and processes. SYSTEM HEALTH (01B) has the health score out of 100 and three caps: QUICK CLEAN, TRIM MEMORY and EXPORT (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:260`, `:293`, `:320`). QUICK CLEAN asks first and leaves the Recycle Bin alone. SYSTEM RESOURCES (01C) and SYSTEM INFORMATION (01D) follow. The middle cap read BOOST RAM up to v3.0.0 and was renamed in v3.0.1, see [Settings and data](reference-settings-and-data.md) |
| CPU Monitor | `:62` | `CPU 02` | Processor usage, core counts, clock speeds, per-core figures where the hardware reports them |
| GPU Monitor | `:67` | `GPU 03` | Graphics adapter load, memory and temperature |
| Memory Monitor | `:72` | `MEM 04` | RAM in use and available, the page file, and working-set trimming with TRIM MEMORY |
| Processes | `:77` | `PROC 05` | Running processes with per-process processor and memory use. END TASK asks first, refuses the processes Windows cannot run without, and says whether the process ended (`src/SysMonitor.Core/Services/Monitors/ProcessMonitor.cs:282`) |
| Directory Cleaner | `:82` | `CLEAN 06` | Scans for reclaimable files and removes the ones you select with CLEAN SELECTED, after a confirmation. The Recycle Bin is offered but never pre-selected (`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:147`) |
| Registry Cleaner | `:87` | `REG 07` | Finds registry issues. FIX SELECTED asks first, then exports every key a selected fix will touch with `reg.exe` before changing anything, and a failed export stops the clean (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs:947-949`, `:974`, `:997`). RESTORE BACKUP puts them back |
| Startup | `:92` | `BOOT 08` | Enables and disables startup entries through `Explorer\StartupApproved`, the mechanism Task Manager uses, including 32-bit machine-wide entries (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs:12`, `:57-58`, `:68-69`). DELETE asks first |
| Installed Programs | `:97` | `APPS 09` | Inventory and uninstall. Uninstall asks before it starts the program's own uninstaller. Exit codes are reported in words, including restart-needed and already-removed, which are not failures |

## Maintenance

Header at `MainWindow.xaml:103`. A separator divides it after Driver Updater
(`MainWindow.xaml:139`); the five entries after it have no header of their own.

| Page | Line | Module | What it does |
|---|---|---|---|
| Health Check | `:104` | `HEALTH 10` | System-wide checks producing a score, a grade, counts of critical issues and warnings, and recommended actions. QUICK FIX ALL asks first, fixes only the issues ticked, and backs the registry up before touching it |
| Game Mode | `:109` | `GAME 11` | Lowers background apps below your game in the processor queue and puts them back afterwards. Asking apps to close is a separate opt-in that only ever asks (`src/SysMonitor.Core/Services/GameMode/GameModeService.cs`) |
| Browser Privacy | `:114` | `PRIVACY 12` | Clears browsing traces across installed browsers, counted per browser actually found. CLEAN SELECTED asks first, naming what cannot be recovered |
| Drive Wiper | `:119` | `WIPE 13` | Overwrites files with the pattern set you choose, reads the last pass back to confirm it, and warns when the target is an SSD (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs:406`, `:602`, `:20`). WIPE NOW asks before it starts, naming the count and size, with no default button (`src/SysMonitor.App/Views/DriveWiperPage.xaml.cs:37`) |
| Scheduled Cleaning | `:124` | `SCHED 14` | Daily, weekly or monthly cleans that run headlessly and return an exit code to Task Scheduler (`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningRun.cs`). DELETE TASK asks first |
| Backup Manager | `:129` | `BACKUP 15` | Full and incremental backups, Windows system images, with optional AES-256 encryption that is authenticated and restorable (`src/SysMonitor.Core/Services/Backup/BackupEncryption.cs:20-22`), and system restore points |
| Driver Updater | `:134` | `DRIVERS 16` | Device driver inventory, with problem and unsigned drivers flagged, and links out to Device Manager and Windows Update |
| Disk Analyzer | `:140` | `DISK 17` | Storage use per drive |
| Network | `:145` | `NET 18` | Live throughput read from the adapter that actually carries your traffic, selected by routed interface rather than claimed link speed (`src/SysMonitor.Core/Services/Monitors/NetworkMonitor.cs`) |
| Battery | `:150` | `BATT 19` | Charge, power state, and health from design capacity against full-charge capacity. Reports "Not reported" when Windows does not supply those figures (`src/SysMonitor.Core/Services/Monitors/BatteryMonitor.cs`) |
| Temperature | `:155` | `TEMP 20` | Processor and graphics thermal sensors and fan speeds, through LibreHardwareMonitor |
| System Info | `:160` | `SYS 21` | Operating system, processor, memory and storage |

## Utilities

Header at `MainWindow.xaml:166`.

| Page | Line | Module | What it does |
|---|---|---|---|
| Large Files | `:167` | `LARGE 22` | Finds large files, largest first. Deletion goes to the Recycle Bin after a confirmation naming the count and size, and does not follow links; a file Windows cannot recycle is left where it is, and the result says so (`src/SysMonitor.App/Views/LargeFilesPage.xaml.cs:31`, `src/SysMonitor.Core/Services/Utilities/RecycleBin.cs:136`) |
| Duplicate Finder | `:172` | `DUPES 23` | Duplicates decided by SHA-256 of the whole file, never by sampling. One physical file is counted once, the oldest copy is kept, and deletion goes to the Recycle Bin after a confirmation naming the count and size; a file Windows cannot recycle is left where it is, and the result says so (`src/SysMonitor.Core/Services/Utilities/DuplicateFinder.cs:161`, `:224`) |
| File Tools | `:177` | `FILES 24` | ZIP compression of a file or a folder, and GZip of a single file. See the note on formats below |
| PDF Tools | `:182` | `PDF 25` | Merge, split, extract pages, convert images and text to PDF, and sign. EDIT PDF opens the PDF Editor, module `PDF EDIT 35` |
| Image Tools | `:187` | `IMAGE 26` | Compress, convert, resize, and read image metadata |

## Wireless

Header at `MainWindow.xaml:193`.

| Page | Line | Module | What it does |
|---|---|---|---|
| Bluetooth | `:194` | `BT 27` | Paired and discoverable device listing |
| WiFi Analyzer | `:199` | `WIFI 28` | Nearby networks, channels, signal strength and security type |
| Network Mapper | `:204` | `MAP 29` | Devices on the local network with IP and MAC addresses |

## The footer

These five sit at the bottom of the menu, under `MainWindow.xaml:211`.

| Page | Line | Module | What it does |
|---|---|---|---|
| Performance | `:212` | `PERF 30` | Operation metrics and timing, with P95 and P99 figures and CSV export |
| History | `:217` | `HIST 31` | Thirty days of stored readings, charted |
| Settings | `:222` | `SET 32` | See [Settings and data](reference-settings-and-data.md) |
| Donate | `:227` | `DONATE 33` | Support the project |
| User's Guide | `:232` | `GUIDE 34` | The full guide, inside the app |

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
`KeyboardAccelerator` anywhere, so no key opens a page or works outside the PDF Editor.
Every key the app handles is in the PDF Editor
(`src/SysMonitor.App/Views/PdfEditorPage.xaml.cs:84-141`, `:391-398`, `:1431-1443`):

| Key | Where | Action |
|---|---|---|
| Ctrl+O | Editor | Open a PDF |
| Ctrl+S | Editor | Save the PDF |
| Ctrl+P | Editor | Print |
| Ctrl+Z | Editor, outside a text field | Undo |
| Ctrl+Y | Editor, outside a text field | Redo |
| V, P, T, H, R, E, L, A, N, S, X | Editor, with a document open, outside a text field | Select, Pen, Text, Highlight, Rectangle, Ellipse, Line, Arrow, Sticky Note, Signature and Redact tools (`:64-77`) |
| I | Editor, with a document open, outside a text field | Insert an image |
| Delete | Editor canvas | Delete the selected annotation |
| Esc | Editor canvas | Clear the current selection |
| Enter | Search box | Run the annotation search |
| Enter | Text annotation box | Commit the text annotation |
| Esc | Text annotation box | Cancel the text annotation |

The tool letters are ignored while a text field has the keyboard, where they are the
words being typed. The zoom caps' tooltips name -, + and 0, but the editor does not
handle those keys; use the caps.

Note that the PDF editor's search looks at annotations, not page text. The app does
not extract or search text inside a PDF.

## Related

- [Settings and data](reference-settings-and-data.md)
- [What v3.0.0 removed, and why](explanation-honest-reporting.md)
