# How to free up disk space

You will scan your machine for reclaimable files, decide category by category what
goes, and delete only what you chose.

## Prerequisites

- STX.1 System Monitor v3.0.0 or later installed
- Run the app as administrator if you want the Windows temp folder included. Without
  it, files owned by the system are skipped

## Steps

1. Open **Directory Cleaner** from the navigation menu.

2. Click **Scan**.

   The scan sorts what it finds into categories
   (`src/SysMonitor.Core/Models/CleanerModels.cs:56-70`):

   | Category | What it holds |
   |---|---|
   | Windows Temp | The system temporary folder. Needs administrator rights |
   | User Temp | Your own temporary folder |
   | Browser Cache | Cached page content for installed browsers |
   | Browser Cookies | Site cookies. Clearing these signs you out of sites |
   | Browser History | Visited page records |
   | Recycle Bin | Files already deleted but still recoverable |
   | Thumbnails | Explorer thumbnail database |
   | Windows Update Cache | Downloaded update payloads already applied |
   | Prefetch | Windows launch-time optimisation data |
   | Memory Dumps | Crash dumps written by Windows |
   | Error Reports | Windows Error Reporting queues |
   | Log Files | Application and system logs |
   | Registry | Registry issues, handled by the Registry Cleaner page |

3. Uncheck anything you want to keep.

   Two worth thinking about before you tick them. **Browser Cookies** signs you out
   of every site you are signed into. **Prefetch** makes the next launch of your
   common applications slower until Windows rebuilds it.

4. Click **Clean**.

   The app reports what it actually removed, by category, rather than what it
   intended to remove.

## Verification

Check the reported figure against real free space. Before and after, run:

```powershell
Get-PSDrive C | Select-Object Used, Free
```

The difference should be close to the reported total. It will not match to the byte,
because Windows writes to disk while you work.

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
