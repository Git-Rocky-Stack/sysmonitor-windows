# The Drive Wiper and solid-state drives

The Drive Wiper page warns you when the files you selected sit on a solid-state drive,
and tells you that overwriting cannot promise the flash holding the old contents was
written over. This page explains why that warning exists and what to do instead.

## The problem

Secure deletion by overwriting rests on one assumption: that writing to a file's
logical location puts new bytes on the same physical medium that held the old ones.

On a spinning hard disk that assumption holds. A logical block maps to a physical
sector, and writing that block magnetises that sector.

On a solid-state drive it does not hold, for three reasons built into how flash works.

**Flash cannot overwrite in place.** A NAND cell must be erased before it can be
written again, and erasing happens in blocks much larger than the page being written.
So a write goes to an already-erased page somewhere else, and the old page is marked
stale.

**Wear levelling moves data deliberately.** Flash cells wear out after a bounded number
of erase cycles, so the controller spreads writes across the whole device. It will
actively avoid reusing the cells your file was on.

**Over-provisioning hides capacity.** Drives ship with more flash than they advertise.
Those spare cells are not addressable from the operating system at all, so nothing you
write through the filesystem can reach them.

The result: you ask to overwrite a file, the drive writes your patterns to fresh cells,
and the cells holding the original contents keep holding them until the controller
happens to erase that block. You have no way to make it do so, and no way to check.

## The approach

The app detects the media type of the physical disk behind your selection and, when
Windows reports solid state, says so on the page
(`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs:20`, `:309`, `:334`). In that
reading, 4 means solid state and 3 means spinning (`DriveWiper.cs:334`).

The wipe still runs. It is not useless: the logical copy is gone, the file is no longer
readable through the filesystem, and casual recovery tools will not find it. What
changes is the claim. The app will not tell you the data is unrecoverable, because it
cannot know that.

The read-back check still applies on every wipe. Every wipe reads its last pass back
and compares it against the pattern that should be there (`DriveWiper.cs:406`, `:589`).
That confirms the logical write landed. It does not and cannot confirm that the
physical cells holding the original were erased.

## Trade-offs

**The honest answer is less useful than a false one.** "Securely wiped, unrecoverable"
is what a user wants to read. It would be a lie on an SSD, and a user acting on it
could hand over a drive believing it is clean.

**There is no real alternative we can offer in-app.** The operations that genuinely
work on flash are the drive's own secure-erase command and full-disk encryption, and
both sit outside what this app does.

**Multi-pass methods are close to pointless on SSDs and we still offer them.** Writing
your file 35 times with the Gutmann patterns wears the drive and does not improve the
guarantee. The methods remain available because the page tells you the situation and
the choice is yours.

## What to do instead

If you need a real guarantee on a solid-state drive, in rough order of practicality:

1. **Encrypt the whole drive before the sensitive data exists.** Turn on BitLocker, or
   the drive's own hardware encryption. Destroying the key then destroys access to
   everything, including the stale copies in cells you cannot address. This is the only
   approach that handles over-provisioning.

2. **Use the drive's secure-erase command.** ATA Secure Erase, or NVMe Format with a
   secure erase setting, instructs the controller itself to erase every cell including
   spares. It wipes the entire drive rather than one file, and it is run from the drive
   manufacturer's tool or a boot utility.

3. **Destroy the drive physically**, if the data warrants it and the drive does not
   need to survive.

Overwriting a single file is not on this list. That is the point of the warning.

## On mechanical drives

On a spinning disk, overwriting does what it says, and the read-back check confirms it
landed. A single pass is sufficient against software recovery. The multi-pass methods
address a threat model involving magnetic force microscopy on drive platters, which is
a laboratory operation and is generally considered impractical against modern drive
densities.

Choose the method to match who you think is coming for the data, and be aware that
Gutmann on a large file takes hours.

## Related

- [How to securely wipe files](howto-securely-wipe-files.md)
- [What v3.0.0 removed, and why](explanation-honest-reporting.md)
