# What v3.0.0 removed, and why

If you used STX.1 before version 3.0.0 and something you relied on has disappeared,
this page explains the reasoning. It is not a list of features cut for size. Every one
of them was a thing the app said it did and did not do.

## The problem

A system utility is trusted with irreversible operations. It deletes files, edits the
registry, overwrites disks and terminates processes. The only thing a user has to go
on is what the application reports back.

An audit of the 2.x line found a consistent shape of defect: the operation completed,
the interface said it succeeded, and the underlying thing had not happened. Examples
that were all live in 2.2.2:

- The Drive Wiper's Gutmann 35-pass option filled its middle passes with a formula
  that is not that method, and no pass was ever read back. Every wipe reported success
  (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs`).
- Registry backup wrote a `.reg` file containing only comment lines, for three fixed
  keys, while the cleaner deleted values and whole subkey trees across two hives.
  There was no restore code at all
  (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs`).
- Every backup made with Encrypt Backup was unrestorable, because no decryption code
  existed (`src/SysMonitor.Core/Services/Backup/BackupEncryption.cs`).
- Scheduled cleaning ran the app with a switch nothing read, so at every scheduled time
  Windows opened the full interface as administrator and cleaned nothing
  (`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningRun.cs`).
- PDF Redact drew a black rectangle over text and imported the page content unchanged,
  so the text underneath stayed in the file and could be selected and copied
  (`src/SysMonitor.Core/Services/Utilities/PdfEditor.cs`).

A user cannot detect any of these from the interface. That is what makes them worse
than crashes.

## The approach

Every such defect got one of two treatments.

**Fix it, if the feature can do what its label says.** Drive Wiper passes now follow
the 1996 paper's table and every wipe reads its last pass back and compares. Registry
cleaning exports every key a selected fix will touch with `reg.exe`, and a failed
export stops the clean. Encrypted backups got a real format, and the legacy one stayed
readable so old backups are not stranded.

**Remove it, if it cannot.** Rewording a feature so the label matches a weaker reality
leaves the user with a control that looks useful and is not. Four were removed:

| Removed | What it actually was |
|---|---|
| RAM Cache | A folder on disk plus a `TEMP` variable set inside this one process that no other program could see. Its own code switched itself off on the next start. Doing it for real needs a RAM-disk driver |
| 7z compression | Wrote a ZIP and changed the extension. Asking for 7z gave you a ZIP named `.7z` |
| Auto-Optimize Memory setting | Nothing read it. The related threshold is now described as what it drives, which is a memory alert |
| Performance page frame-rate card | Displayed a hard-coded 60 that nothing set, next to memory and garbage-collection figures that are measured |

Removing four user-visible features is what makes this a major version rather than a
patch.

## Trade-offs

This was not free.

**Users lose controls they were using.** Someone who had a cleaning schedule set up had
a schedule that did nothing, but they also had the belief that their machine was being
cleaned. Telling them the truth is worse in the short term than leaving it alone.

**The app now says no more often.** Battery health reads "Not reported" on machines
where Windows does not publish design capacity. The FPS overlay reads `NO FPS SENSOR`
where LibreHardwareMonitor has no frame-rate sensor to read, which is most hardware.
A wipe can come back unconfirmed. These are less satisfying than a number, and they
are the honest answer.

**The feature count went down.** For a product compared on feature lists, that is a
commercial cost. It was accepted.

## What this means for you as a user

Treat the app's reports as load-bearing. When the Drive Wiper says a file is wiped, it
means it wrote the patterns and read the last one back and it matched. When it says
unconfirmed, it could not prove that and you should act accordingly.

Where the app cannot guarantee something, it now tells you, rather than staying quiet.
The clearest example is
[the Drive Wiper on solid-state drives](explanation-drive-wiper-and-ssds.md).

## Where to read the detail

[CHANGELOG.md](../CHANGELOG.md) lists every behaviour change in v3.0.0 with the file it
lives in. It is long, and it is specific on purpose.

## Related

- [The Drive Wiper and solid-state drives](explanation-drive-wiper-and-ssds.md)
- [Back up and restore](howto-back-up-and-restore.md), if you have pre-3.0.0 backups
- [Manage startup programs](howto-manage-startup-programs.md), if you disabled items in an earlier version
