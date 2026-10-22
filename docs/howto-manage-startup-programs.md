# How to manage startup programs

You will see what launches when you sign in, turn off the ones you do not want, and
be able to turn them back on. Changes you make here are visible to Task Manager and
reversible from either place.

## Prerequisites

- STX.1 System Monitor v3.0.0 or later installed
- Administrator rights, if you want to change machine-wide entries under HKLM. Your
  own entries under HKCU do not need it

## Before you start

If you used an earlier version of STX.1 to disable startup items, read this.

Before v3.0.0, **Enable** logged a line and changed nothing, every item was listed as
enabled whatever its real state, and **Disable** moved the entry into a private key
that nothing read again. Disabling from inside the app was one-way, and Task Manager
knew nothing about it (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs`).

Entries now use `Explorer\StartupApproved`, which is the same mechanism Task Manager
writes (`StartupOptimizer.cs:12`, `:57-58`, `:80`). The app, Task Manager and Windows
now agree with each other.

Programs a 32-bit installer set to start for every user are listed too. Windows keeps
them under `WOW6432Node` and records whether they may run under
`StartupApproved\Run32`, which is where Task Manager reads them
(`StartupOptimizer.cs:68-69`). Up to and including v3.0.1 those entries were missing
from the list.

## Steps

1. Open **Startup** from the navigation menu.

2. Read the list. Each entry shows where it is registered and an impact rating for how
   much it costs you at sign-in.

3. Select an entry and click **DISABLE**.

   The caps act on the selected entry, so **DISABLE**, **ENABLE** and **DELETE** stay
   dimmed until you select one (`src/SysMonitor.App/Views/StartupPage.xaml:38-46`).

   The entry stays where it is. What changes is its approval value under
   `Explorer\StartupApproved`, which is how Windows itself records that an entry is
   switched off.

4. To undo, select the entry and click **ENABLE** (`src/SysMonitor.App/Views/StartupPage.xaml:42`).

**DELETE** is different. It is a red cap: it removes the entry from where it is
registered, and the program stays installed. It asks first, and the question says
that DISABLE keeps the entry so it can be turned back on while DELETE does not
(`src/SysMonitor.App/ViewModels/StartupViewModel.cs:68-70`). Click **DELETE** in the
question to go ahead, or **CANCEL**. Use DISABLE unless you mean to lose the entry.

## Verification

Open Task Manager, go to the **Startup apps** tab, and find the entry. It should show
as Disabled there too. That agreement is the point. If the two disagree, the app has
a bug worth reporting.

## Troubleshooting

**An entry will not change state.** Machine-wide entries live under HKLM, the 32-bit
ones included, and need administrator rights. Restart the app as administrator.

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
