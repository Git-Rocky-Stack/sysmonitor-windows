# How to securely wipe files

You will overwrite a file's contents so the old data is gone from the disk, choose how
thoroughly, and get a straight answer about whether the app could confirm it.

Read [The Drive Wiper and solid-state drives](explanation-drive-wiper-and-ssds.md)
first if the files are on an SSD or an NVMe drive. The short version is at the bottom
of this page, but the explanation matters.

## Prerequisites

- STX.1 System Monitor v3.0.0 installed
- The files you intend to destroy, and a second copy of anything you might want back.
  This operation is not reversible by design

## Steps

1. Open **Drive Wiper** from the navigation menu.

2. Add the files or the folder you want to destroy.

   Directory wipes stay inside the folder you chose. They do not follow junctions or
   symbolic links out of it. Before v3.0.0 they did, which meant files outside your
   selection were overwritten and deleted
   (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs`).

3. Choose a method (`DriveWiper.cs:27-32`, pass counts at `:498-505`):

   | Method | Passes | Use it when |
   |---|---|---|
   | Single pass | 1 | The file is merely private, and you want it gone quickly |
   | DoD 3-pass | 3 | A reasonable default for sensitive personal data |
   | DoD 7-pass | 7 | Data you would be in trouble over |
   | Gutmann | 35 | You have a specific reason and time to spare |

   The Gutmann passes now follow the table in Peter Gutmann's 1996 paper
   (`DriveWiper.cs:561-566`). Before v3.0.0 the middle passes were filled with
   `(pass * 17) % 256`, which is not that method.

4. Start the wipe and let it finish.

## Verification

This is the part that changed in v3.0.0, and it is the reason to trust the result.

Every wipe reads its last pass back off the disk and compares it against the pattern
that was supposed to be written (`DriveWiper.cs:406`, `:589`). A file that cannot be
confirmed is reported as **unconfirmed** rather than counted as wiped.

So read the result. "Wiped" means the app wrote the patterns and then read the final
one back and it matched. "Unconfirmed" means it could not prove that, and you should
treat the data as possibly still present.

Before v3.0.0 no pass was ever read back, and every wipe reported success.

## What this does not guarantee

On a solid-state drive, overwriting a file cannot promise that the flash cells holding
the old contents were written over. The drive's controller decides where writes
physically land, and it generally does not put them back on the same cells. The app
detects this and says so on the page when your selection sits on an SSD
(`DriveWiper.cs:20`, `:309`, `:334`).

That warning is not boilerplate. If you need a guarantee on an SSD, the options are
full-disk encryption from before the data existed, or the drive's own secure-erase
command, or physical destruction. Overwriting one file is not one of them.

## Troubleshooting

**The result says unconfirmed.** The read-back did not match. Common causes are a file
held open by another program, a drive that is failing, or a filesystem that moved the
file. Do not assume the data is gone.

**The file is still visible afterwards.** Wiping overwrites contents and then removes
the entry. If the name is still there, the removal failed. Check the log at
`%LocalAppData%\SysMonitor\Logs`.

**It is taking a very long time.** Gutmann writes your file 35 times. On a large file
on a mechanical disk, hours is normal. Single pass is 35 times less work.

## Related

- [The Drive Wiper and solid-state drives](explanation-drive-wiper-and-ssds.md)
- [Back up and restore](howto-back-up-and-restore.md), for the opposite problem
- [Reference: pages](reference-pages.md)
