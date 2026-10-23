# How to back up and restore files

You will create a backup, optionally encrypt it with a password, confirm the backup
itself is intact, and restore files from it.

## Prerequisites

- STX-1 ULTIMATE System Monitor v3.0.0 or later installed
- Somewhere to write the backup with enough free space
- If you encrypt: a password you will not lose. There is no recovery path

## Before you start, if you have older backups

Two things changed in v3.0.0 that affect backups made by earlier versions.

**Encrypted backups made before v3.0.0 cannot be restored by those versions**, because
no decryption code existed at all. Every backup made with "Encrypt Backup" was
unrestorable. v3.0.0 can still read the legacy format
(`src/SysMonitor.Core/Services/Backup/BackupEncryption.cs:26`), so restore old ones
with this version.

**Backup verification used to check the wrong files.** It hashed the live source files
instead of the backup, so a corrupted backup verified clean and a healthy one failed
once you edited the originals (`src/SysMonitor.Core/Services/Backup/BackupService.cs`).
Re-verify anything you are relying on.

## Steps

1. Open **Backup Manager** from the navigation menu. In QUICK ACTIONS (15A), click
   **NEW BACKUP**.

2. Choose a type:

   | Type | Copies |
   |---|---|
   | FULL BACKUP | Everything you selected |
   | INCREMENTAL BACKUP | Only what is new or changed since the previous backup with the same name, full or incremental (`src/SysMonitor.Core/Services/Backup/BackupService.cs:125`, `:1826`) |

   An incremental builds on the backup before it, and that one on the one before, back to
   a full backup: a chain. If nothing has changed, no backup is made. With no earlier
   backup of that name, or when the earlier one was encrypted with a different password
   (or not encrypted when this one is), the incremental starts a new chain and copies
   everything; the summary says which.

   | SYSTEM IMAGE | Windows Backup's image of the system drive and every partition Windows needs to start (`src/SysMonitor.Core/Services/Backup/SystemImage.cs`) |

   A system image has no sources or options to choose: after the type, pick the drive to
   save it on, which cannot be the drive Windows is on. It must be an NTFS drive (a USB
   drive formatted FAT32 or exFAT is refused, with the reason) or a network folder such
   as `\\nas\backups`. The app shows where it goes and about how much space it needs,
   then Windows asks for administrator permission and Windows Backup (`wbadmin`) writes
   the image to `WindowsImageBackup` at the root of that drive. The progress shown is
   Windows Backup's own, and its report is kept in `%LocalAppData%\SysMonitor\Logs`.
   Later images on the same drive add only what changed; a network folder keeps only the
   newest. Cancelling asks Windows Backup to stop, and Windows asks for permission again.

3. Select the folders to back up, then the destination.

4. Set the options.

   If you tick **Encrypt Backup**, the format is PBKDF2-HMAC-SHA256 with 600,000
   iterations, AES-256-CBC, and encrypt-then-MAC with an HMAC-SHA256 tag that is
   verified before any plaintext is written
   (`BackupEncryption.cs:20-22`, `:31`, `:261`). In practice that means a tampered
   backup fails to restore rather than quietly handing you altered files.

   Compression can be None, Normal or Maximum. With None the backup is a folder rather
   than a single file, which is fine and is no longer a failure case
   (`BackupService.cs`).

   **Use Volume Shadow Copy** (on by default) backs up files other programs hold open,
   such as an open Outlook data file. A file that is merely open copies directly; one
   held exclusively is read from a shadow copy of its drive, which Windows asks
   administrator permission for. The helper that reads it copies only files your own
   account may read, and hands the bytes back to the app rather than writing anything
   itself (`src/SysMonitor.Core/Services/Backup/ShadowCopy.cs`). Refuse the prompt, or
   turn the option off, and those files are listed as not backed up.

   **Repeat** runs the backup now and then daily, weekly or monthly at the hour you pick,
   through Windows Task Scheduler while you are signed in. A run missed while the PC was
   off happens when it is next on. Each run is a new backup in HISTORY - an incremental
   repeat builds on the one before. An encrypted backup's password is kept for its runs,
   protected by Windows for your account; it is never stored in clear text.

5. Click **START BACKUP** and wait for the summary. **CANCEL BACKUP** stops it.

## Schedules

SCHEDULES (15D) lists every backup that repeats, with when it next runs and how its last
run went. **Run now** runs it at once, here. **Stop schedule** asks first, then removes its
task from Task Scheduler; the backups it made stay in HISTORY.

A scheduled run has no window and nobody to answer a prompt. When Windows let its task run
with administrator rights - you are an administrator - files in use are still copied from a
shadow copy; otherwise they are reported in the run's result. Windows shows a notification
when a run finishes.

## Verification

In HISTORY (15B), use the **Verify backup** cap on the completed backup. It hashes the
backup and checks it against what was recorded at the time it was written.

This is worth doing now rather than on the day you need it, because until v3.0.0 it
was checking the wrong thing.

## Restoring

In HISTORY (15B), use the **Restore backup** cap on the backup you want. The app asks
first, saying what will be restored and where. The **Delete backup** cap beside it is
red and asks before it removes anything.

Restore decides where files go, rather than trusting the path recorded inside the
archive. Destinations are planned up front and confined to the folders the backup was
taken from, and anything refused is reported to you rather than silently skipped
(`BackupService.cs`).

That matters because before v3.0.0 an edited backup file could write its contents
anywhere you could write, including your Startup folder.

A system image is not restored from inside Windows. Open Settings > System > Recovery,
choose Advanced startup > **Restart now**, then Troubleshoot > Advanced options >
**System Image Recovery**. It replaces everything on the drives in the image. Its
**Restore backup** cap in HISTORY says the same, **Verify backup** says Windows Backup
checked it as it was written, and **Delete backup** only takes it off the list: Windows
manages its files (Control Panel > Backup and Restore (Windows 7) > **Manage space**).

Restoring an incremental puts back every file as it was when that incremental was taken,
reading each one from the newest backup in its chain that holds it. A file deleted before
the incremental was taken is not restored. An encrypted chain asks for its one password.

A backup that a later incremental builds on cannot be deleted while that incremental
exists: delete the newer incrementals first. "Keep last" never removes one either, so a
long chain keeps its full backup; make a new FULL BACKUP now and then to start a fresh one.

## Troubleshooting

**The password does not work.** There is no recovery. The key is derived from your
password with 600,000 PBKDF2 iterations, which is the point.

**Restore reported refused entries.** Those files pointed outside the folders the
backup covered, so they were not written. The report names them. This is the safety
check doing its job.

**An incremental will not restore: a backup it builds on is missing.** One of the backups
in its chain was deleted or moved outside the app. Nothing is written; restore the newest
full backup instead, or move the missing backup back.

**A system image is refused for a drive.** The message says why: it is the drive
Windows is on, it is not NTFS, it is a mapped network drive (choose its `\\server\share`
folder instead), or it is not connected. If Windows Backup itself fails, the message
quotes its error and names the report in the Logs folder.

**Verification fails on a backup that used to pass.** If the backup was made before
v3.0.0, the original pass may have been meaningless, because verification hashed the
source files. Make a fresh backup.

## Related

- [Reference: pages](reference-pages.md)
- [Securely wipe files](howto-securely-wipe-files.md)
- [What v3.0.0 removed, and why](explanation-honest-reporting.md)
