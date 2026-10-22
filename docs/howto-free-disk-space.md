# How to free up disk space

You will scan your machine for reclaimable files, decide category by category what
goes, and delete only what you chose.

## Prerequisites

- STX.1 System Monitor v3.0.0 or later installed
- Run the app as administrator if you want the Windows temp folder included. Without
  it, files owned by the system are skipped

## Steps

1. Open **Directory Cleaner** from the navigation menu.

2. In CLEANING CONTROLS (06A), click **SCAN**.

   The results appear in SCAN RESULTS (06B), one row per location that holds
   something (`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:73-80`,
   `:140`, and `src/SysMonitor.Core/Services/Cleaners/BrowserCacheCleaner.cs:78`):

   | Category | What it holds | Ticked after a scan |
   |---|---|---|
   | Windows Temporary Files | The system temporary folder. Needs administrator rights | Yes |
   | User Temporary Files | Your own temporary folder | Yes |
   | Windows Prefetch Files | Windows launch-time optimisation data | Yes |
   | Thumbnail Cache | Explorer thumbnail database | Yes |
   | Windows Update Cache | Downloaded update payloads already applied | Yes |
   | Memory Dump Files | Crash dumps written by Windows | Yes |
   | Windows Error Reports | Windows Error Reporting queues | Yes |
   | Windows Log Files | System logs | No |
   | Recycle Bin | Files already deleted but still recoverable | No |
   | Browser cache | Cached page content, one row per installed browser | Yes |

   Cookies, history and saved passwords are not here. They are on the **Browser
   Privacy** page, which asks before it deletes any of them.

3. Untick anything you want to keep, and tick anything you want gone.

   **Prefetch** makes the next launch of your common applications slower until
   Windows rebuilds it. The **Recycle Bin** is never ticked for you
   (`TempFileCleaner.cs:147`): emptying it destroys what you deleted with a way back,
   so it goes only if you tick it.

4. Click **CLEAN SELECTED**.

   The app asks first, saying how many categories and roughly how much space are
   involved. If the Recycle Bin is ticked, the question says it will be emptied and
   that what is in it cannot be restored afterwards
   (`src/SysMonitor.App/ViewModels/CleanerViewModel.cs:81-86`). Click
   **CLEAN SELECTED** in the question to go ahead, or **CANCEL** to leave everything
   where it is.

   The app reports what it actually removed rather than what it intended to remove,
   and how many files it skipped because they were in use.

## Verification

Check the reported figure against real free space. Before and after, run:

```powershell
Get-PSDrive C | Select-Object Used, Free
```

The difference should be close to the reported total. It will not match to the byte,
because Windows writes to disk while you work.

## If a few large files are taking the space

The Directory Cleaner removes what Windows and your browsers leave behind. When the
space is going to a handful of big files instead, use the **Large Files** page:

1. Choose a folder with **BROWSE**, set the minimum size, and click **SCAN**. The
   results are listed largest first
   (`src/SysMonitor.Core/Services/Utilities/LargeFileFinder.cs:163`).

2. Tick the files you no longer need, or use **SELECT ALL** and **SELECT NONE**, and
   click **DELETE SELECTED**.

3. The app asks first, naming how many files and how much they hold
   (`src/SysMonitor.App/Views/LargeFilesPage.xaml.cs:31`). No button is the default,
   so Enter arms nothing and Escape cancels. Confirm with **MOVE TO RECYCLE BIN**.

The files go to the Recycle Bin, where you can restore them. A file Windows cannot
recycle, such as one on a network or removable drive or one larger than the Recycle
Bin is set to hold, is left where it is, and the result says how many and why
(`src/SysMonitor.Core/Services/Utilities/RecycleBin.cs:136`). A file is only reported
as moved once it has been found in the Recycle Bin.

Up to and including v3.0.1, DELETE SELECTED removed the files without asking, and
Windows deleted any file it could not recycle permanently while the page reported it
as moved to the Recycle Bin.

A file in the Recycle Bin still takes up its space. The space comes back when the
Recycle Bin is emptied, which the Directory Cleaner's Recycle Bin category does when
you tick it. The Dashboard's QUICK CLEAN never empties it.

## If you want this to happen on a schedule

Use the **Scheduled Cleaning** page. It registers a Windows scheduled task that runs
headlessly, writes its outcome to the log, and returns an exit code so Task Scheduler
history shows a failed run as failed
(`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningRun.cs`).

In versions before 3.0.0 this did nothing at all. The task launched the full user
interface as administrator and cleaned nothing. If you set up a schedule on an older
version, open the page and set it again.

## Troubleshooting

**The scan finds far less than you expected.** You are probably not running as
administrator, so the Windows temp folder and the update cache were skipped. Close
the app, right-click it, and choose Run as administrator.

**Some files were not removed.** Files held open by a running program cannot be
deleted. Close the program and scan again. Antivirus software also locks files it is
inspecting.

**The number freed looks wrong.** Report it as an issue with the log from
`%LocalAppData%\SysMonitor\Logs`. Numbers that say the wrong thing are the specific
class of bug v3.0.0 was about, and three of them were fixed in it.

## Related

- [Reference: pages](reference-pages.md)
- [Reference: settings and data](reference-settings-and-data.md)
- [Securely wipe files](howto-securely-wipe-files.md), which is a different operation
