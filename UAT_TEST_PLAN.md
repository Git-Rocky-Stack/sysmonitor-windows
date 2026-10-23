# UAT Test Plan - STX-1 ULTIMATE System Monitor

The acceptance pass a person runs on real Windows before a release ships. Automated CI already
builds the app, runs the whole test suite, and opens every page in both themes with animations
on and off (`.github/workflows/windows-ci.yml`, `src/SysMonitor.App/Diagnostics/UiSmokeRun.cs`).
This plan covers what CI cannot: real hardware, real files, real elevation, and how it feels to
use.

Record each case as **Pass**, **Fail** (with what happened and a screenshot) or **N/A** (with
why). A release needs every case Pass or an accepted N/A.

## 0. Test matrix

Run sections 1-3 on every row; sections 4-9 on at least the first row.

| Machine | Windows | Account | Build | Theme |
|---|---|---|---|---|
| x64 desktop or laptop with a dedicated GPU | Windows 11 24H2 | Standard user | Setup.exe | Night Ops (Dark) |
| x64 laptop with a battery | Windows 11 23H2 | Administrator | Microsoft Store (MSIX) | Day Shift (Light) |
| Any | Windows 10 22H2 | Standard user | Portable zip | System Default |
| Any | Windows 11 | Any | Any | High Contrast (Settings > Accessibility > Contrast themes) |

Prepare: a throwaway browser profile with a saved password and a bookmark; a folder of test
files including two identical copies and one file over 100 MB; a USB stick (FAT32 if you have
one); a PDF of several pages.

## 1. Install, upgrade, uninstall

| ID | Steps | Expected |
|---|---|---|
| I-1 | Run Setup on Windows 10 1909 or older (a VM is fine) | Setup refuses: the Windows version is too old |
| I-2 | Install with 3.0.1 already installed | One "STX-1 ULTIMATE System Monitor" entry in Apps; settings kept; About shows the new version |
| I-3 | Fresh install | Licence page is MIT; `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` sit next to the exe |
| I-4 | Start the app, close the window (minimize to tray on), start it again from Start | The first window comes back; one tray icon; no second copy in Task Manager |
| I-5 | Uninstall | App and Start entry gone; `%LocalAppData%\SysMonitor` left (it is user data) |
| I-6 | Store build: install, open Settings, change a value, restart | The value survives the restart |

## 2. Every page opens

| ID | Steps | Expected |
|---|---|---|
| P-1 | Open each of the 34 menu entries, and PDF Tools > open the editor | Each opens on a view header `MOD - <MODULE> - <NN>`; sections are faceplates whose kickers read `<MODULE> - <SECTION> - <NN><LETTER>` in order A, B, C |
| P-2 | Resize the window as small as it goes | It stops at about 1100 x 700; no page clips a word or button at that size |
| P-3 | Repeat P-1 at 150% display scale and at text size 125% | Nothing overlaps; long kickers clip their serial, not the kicker |
| P-4 | Switch theme in Settings between Light, Dark and System Default | Every page follows; wells stay dark in both |
| P-5 | High Contrast on | Your contrast colours are used; nothing is dimmed or unreadable |
| P-6 | Tray menu: open Dashboard, CPU, Memory, History | The page opens once; the rail highlights the right entry (History is in the footer) |
| P-7 | In PDF Tools open the editor, then press PDF Tools in the rail | You are back in PDF Tools |

## 3. Every armed action asks first

For each: press the red cap, read the dialog, press Escape (or CANCEL), confirm nothing changed;
then repeat and confirm.

| ID | Page | Cap | Expected dialog says |
|---|---|---|---|
| A-1 | Dashboard | QUICK CLEAN | Temporary files are deleted; the Recycle Bin is left |
| A-2 | Directory Cleaner | CLEAN SELECTED | How many categories and how much; warns when the Recycle Bin is ticked |
| A-3 | Registry Cleaner | FIX SELECTED | Keys are backed up first |
| A-4 | Startup | DELETE | The entry is removed; DISABLE keeps it. DISABLE, ENABLE, DELETE are greyed until a row is selected |
| A-5 | Processes | END TASK | Names the process and PID. Selecting `csrss` or `lsass` and pressing END TASK says it is part of Windows and is not ended |
| A-6 | Installed Programs | Uninstall (red row icon) | Names the program |
| A-7 | Browser Privacy | CLEAN with Saved Passwords ticked | Lists the unrecoverable items by browser |
| A-8 | Drive Wiper | WIPE NOW | Count, size, and an SSD warning on an SSD |
| A-9 | Large Files / Duplicate Finder | DELETE SELECTED | Files go to the Recycle Bin |
| A-10 | Scheduled Cleaning | DELETE TASK | The task leaves Task Scheduler |
| A-11 | Settings | CLEAR, RESET TO DEFAULTS | What is reset; CLEAR saves at once, RESET waits for SAVE |
| A-12 | Backup | Delete (red history icon) | Names the backup |
| A-13 | Health Check | QUICK FIX | What is deleted and that the registry is backed up first |
| A-14 | PDF Editor | DELETE PAGE, CLEAR ALL ANNOTATIONS | The page number; that the file changes only on save |

In every dialog: no button is highlighted as default, Enter does nothing, the confirm word is in
capitals.

## 4. Monitoring

| ID | Steps | Expected |
|---|---|---|
| M-1 | Dashboard, CPU, GPU, Memory, Disk, Network, Battery, Temperature, System Info | Values move; no reading shows a made-up value - an unavailable temperature reads `--` |
| M-2 | Run without administrator rights | Temperatures may read `--`; nothing errors |
| M-3 | Settings: set a CPU temperature warning just above idle, open Game Mode, load the CPU | A notification fires, while the Dashboard is not open |
| M-4 | Same with the window in the tray | The notification still fires |
| M-5 | Leave Processes open, hide to tray, watch Task Manager for 30 s | The app's CPU use drops to near idle |
| M-6 | Performance: EXPORT CSV | The page names the file on the Desktop; the CSV opens in Excel with numbers in their own columns |
| M-7 | History after an hour | Charts show readings, once each (no doubled points) |

## 5. Cleaning and the registry

| ID | Steps | Expected |
|---|---|---|
| C-1 | Directory Cleaner: SCAN | Recycle Bin is listed but not ticked |
| C-2 | CLEAN SELECTED with Temp ticked while an app has a temp file open | Clean finishes; the open file is left; result does not claim it |
| C-3 | Registry Cleaner: SCAN on a machine with MSI-installed programs (Office, Visual C++ runtimes) | None of them is listed as an invalid uninstall entry |
| C-4 | FIX SELECTED, then RESTORE BACKUP | The `.reg` backup is under `%LocalAppData%\SysMonitor\RegistryBackups`; restore puts the values back |
| C-5 | Health Check: SCAN, QUICK FIX | Only ticked items are fixed; result lists errors if any |
| C-6 | Browser Privacy on the throwaway profile, browser closed: clean Cache only | Cache emptied; passwords and bookmarks intact |
| C-7 | Same, ticking Firefox "History and Bookmarks" | Warned as unrecoverable; afterwards Firefox has no history or bookmarks |
| C-8 | Startup: DISABLE an entry, check Task Manager > Startup apps | Shown Disabled there too; ENABLE reverses it |
| C-9 | Startup on a machine with a 32-bit program set to start for all users | It is listed as `HKLM Run (32-bit)` and can be disabled (needs admin) |

## 6. Backup and restore

| ID | Steps | Expected |
|---|---|---|
| B-1 | Back up Documents (zip, verify on) | Succeeds even with the `My Music` junction present |
| B-2 | Back up two folders both named `Photos` from different places | Both appear in the backup (`Photos`, `Photos-2`); restore puts each back where it came from |
| B-3 | Encrypted backup, then restore with the wrong password | Restore refuses and writes nothing |
| B-4 | Encrypted backup to a nearly full USB stick | If it fails, no unencrypted `.zip` is left on the stick |
| B-5 | Set "Keep last" to 1, make a Full then an Incremental | The Full is kept |
| B-5a | Full; change one file and add one; Incremental; delete a file, change another; Incremental | Each incremental's file count is only what changed; restoring the last gives every file at its newest, without the deleted one |
| B-5b | Incremental with nothing changed since the last backup | Says nothing has changed; no new backup in HISTORY |
| B-5c | Delete the Full of a chain while its incrementals exist | Refused, saying later incrementals build on it |
| B-8 | SYSTEM IMAGE to C: and to a FAT32 stick | Each refused before anything starts, saying why |
| B-9 | SYSTEM IMAGE to an NTFS USB drive; accept the permission prompt | Progress follows Windows Backup's percent; `E:\WindowsImageBackup` appears; HISTORY shows "System image of C:" |
| B-10 | SYSTEM IMAGE, then refuse the permission prompt | Says no image was made; nothing in HISTORY |
| B-11 | Restore, Verify and Delete on the system image in HISTORY | Restore gives the System Image Recovery steps; Delete only takes it off the list and the folder stays |
| B-6 | Create restore point twice within a few minutes | The second time says Windows did not create a new one |
| B-7 | Restore points list | Shows the machine's restore points with dates |

## 7. Files and PDFs

| ID | Steps | Expected |
|---|---|---|
| F-1 | File Tools: compress then decompress a file next to its original | The original is untouched; the unpacked copy is `name (2).ext` |
| F-2 | Large Files before scanning | The empty plate says to choose a folder and press SCAN |
| F-3 | Duplicate Finder on the test folder | The two identical files are found; the original cannot be selected |
| F-4 | Drive Wiper: Wipe Free Space on the FAT32 stick | Either covers the free space or fails saying how much it reached |
| F-5 | PDF Tools: merge the PDF with a renamed `.txt` as `.pdf` | Refused, naming the bad file |
| F-6 | PDF Editor: annotate with keys V, P, T, H, R, E, L, A, N, S, X; Ctrl+Z / Ctrl+Y; Ctrl+S | Each key picks its tool (not while typing in a text box); undo/redo/save work |
| F-7 | PDF Editor: go to the last page | NEXT is greyed |
| F-8 | PDF Tools: typed signature | Preview and stamped PDF use the same script face |

## 8. Wireless and network

| ID | Steps | Expected |
|---|---|---|
| W-1 | WiFi Analyzer with Windows location off (Windows 11 24H2) | The page says location is needed, not an empty list |
| W-2 | With location on | Nearby networks with channel and signal |
| W-3 | Bluetooth | Nearby devices listed |
| W-4 | Network Mapper: SCAN your own LAN | Devices found; selecting one shows its open common ports |

## 9. Game Mode and accessibility

| ID | Steps | Expected |
|---|---|---|
| G-1 | ENABLE GAME MODE, start a game, end it | Background apps lowered and restored; power plan restored |
| G-2 | Game overlay SHOW with no supported GPU sensor | The overlay says it has no FPS sensor, rather than inventing a number |
| X-1 | Narrator: tab through Settings, Scheduled Cleaning, PDF Tools | Every slider, combo box, switch and number box is announced with its name |
| X-2 | Narrator: on any page press H (next heading) | Moves section by section through the faceplate kickers |
| X-3 | Narrator on icon-only buttons (Backup history, Installed Programs rows, PDF Editor toolbar) | Each is announced by name |
| X-4 | Keyboard only: reach and operate every cap on Dashboard and Settings | Focus is visible; Space/Enter activates |

## Sign-off

| Role | Name | Date | Result |
|---|---|---|---|
| Tester | | | |
| Owner (Rocky) | | | |
