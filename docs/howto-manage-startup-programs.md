# How to manage startup programs

You will see what launches when you sign in, turn off the ones you do not want, and
be able to turn them back on. Changes you make here are visible to Task Manager and
reversible from either place.

## Prerequisites

- STX.1 System Monitor v3.0.0 installed
- Administrator rights, if you want to change machine-wide entries under HKLM. Your
  own entries under HKCU do not need it

## Before you start

If you used an earlier version of STX.1 to disable startup items, read this.

Before v3.0.0, **Enable** logged a line and changed nothing, every item was listed as
enabled whatever its real state, and **Disable** moved the entry into a private key
that nothing read again. Disabling from inside the app was one-way, and Task Manager
knew nothing about it (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs`).

Entries now use `Explorer\StartupApproved`, which is the same mechanism Task Manager
writes (`StartupOptimizer.cs:12`, `:52-53`, `:61`). The app, Task Manager and Windows
now agree with each other.

## Steps

1. Open **Startup** from the navigation menu.

2. Read the list. Each entry shows where it is registered and an impact rating for how
   much it costs you at sign-in.

3. Select an entry and click **Disable**.

   The entry stays where it is. What changes is its approval value under
   `Explorer\StartupApproved`, which is how Windows itself records that an entry is
   switched off.

4. To undo, select the entry and click **Enable** (`src/SysMonitor.App/Views/StartupPage.xaml:81`).

## Verification

Open Task Manager, go to the **Startup apps** tab, and find the entry. It should show
as Disabled there too. That agreement is the point. If the two disagree, the app has
a bug worth reporting.

## Troubleshooting

**An entry will not change state.** Machine-wide entries live under HKLM and need
administrator rights. Restart the app as administrator.

**An entry you enabled points at a program that is not installed any more.** Earlier
versions could restore a stale copy of an entry that an older version of this app had
set aside, overwriting a newer one Windows already had. That is fixed
(`StartupOptimizer.cs`), but an entry set aside by an old version can still be stale.
Disable it and add the current program yourself.

**A program keeps coming back.** Some applications rewrite their own startup entry
every time they run. Turn the behaviour off inside that application's own settings.

## Related

- [Reference: pages](reference-pages.md)
- [Free up disk space](howto-free-disk-space.md)
