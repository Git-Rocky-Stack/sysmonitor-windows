# How to back up and restore files

You will create a backup, optionally encrypt it with a password, confirm the backup
itself is intact, and restore files from it.

## Prerequisites

- STX.1 System Monitor v3.0.0 or later installed
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

1. Open **Backup Manager** from the navigation menu and start a new backup.

2. Choose a type:

   | Type | Copies |
   |---|---|
   | Full | Everything you selected |
   | Incremental | Only what changed since the last backup of any kind |
   | Differential | Everything changed since the last full backup |

3. Select the folders to back up, then the destination.

4. Set the options.

   If you tick **Encrypt Backup**, the format is PBKDF2-HMAC-SHA256 with 600,000
   iterations, AES-256-CBC, and encrypt-then-MAC with an HMAC-SHA256 tag that is
   verified before any plaintext is written
   (`BackupEncryption.cs:20-22`, `:31`, `:218`). In practice that means a tampered
   backup fails to restore rather than quietly handing you altered files.

   Compression can be None, Normal or Maximum. With None the backup is a folder rather
   than a single file, which is fine and is no longer a failure case
   (`BackupService.cs`).

5. Run it and wait for the summary.

## Verification

Use the **Verify** command on the completed backup. It hashes the backup and checks it
against what was recorded at the time it was written.

This is worth doing now rather than on the day you need it, because until v3.0.0 it
was checking the wrong thing.

## Restoring

Select a backup from the history and restore it.

Restore decides where files go, rather than trusting the path recorded inside the
archive. Destinations are planned up front and confined to the folders the backup was
taken from, and anything refused is reported to you rather than silently skipped
(`BackupService.cs`).

That matters because before v3.0.0 an edited backup file could write its contents
anywhere you could write, including your Startup folder.

## Troubleshooting

**The password does not work.** There is no recovery. The key is derived from your
password with 600,000 PBKDF2 iterations, which is the point.

**Restore reported refused entries.** Those files pointed outside the folders the
backup covered, so they were not written. The report names them. This is the safety
check doing its job.

**Verification fails on a backup that used to pass.** If the backup was made before
v3.0.0, the original pass may have been meaningless, because verification hashed the
source files. Make a fresh backup.

## Related

- [Reference: pages](reference-pages.md)
- [Securely wipe files](howto-securely-wipe-files.md)
- [What v3.0.0 removed, and why](explanation-honest-reporting.md)
