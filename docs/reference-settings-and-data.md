# Reference: settings and data

Every setting on the Settings page of STX-1 ULTIMATE System Monitor v3.1.0, with its default,
and every location the app writes to.

Defaults are read from `src/SysMonitor.App/ViewModels/SettingsViewModel.cs:24-56`. The
RESET TO DEFAULTS cap restores exactly these values (`SettingsViewModel.cs:143-176`).
It is a red cap and asks first; the question says nothing is saved until you press
SAVE SETTINGS (`SettingsViewModel.cs:145-147`).

The page's sections are faceplates named APPEARANCE (32A), BEHAVIOR (32B), MONITORING
(32C), ALERT THRESHOLDS (32D), DATA & PRIVACY (32E) and ABOUT (32F). Labels below are
as the page writes them.

## Appearance

| Setting | Type | Default | Effect |
|---|---|---|---|
| Theme | System Default, Light, Dark (Strategia-X) | Dark (Strategia-X), index 2 (`:24`) | Applied when you press SAVE SETTINGS and every time the app starts (`src/SysMonitor.App/MainWindow.xaml.cs:182`). Light is Day Shift, Dark is Night Ops, and System Default follows Windows' own light or dark app setting; the choices are listed at `src/SysMonitor.App/Views/SettingsPage.xaml:27-29` |

## Behaviour

| Setting | Type | Default | Effect |
|---|---|---|---|
| Run at Windows startup | On or off | Off (`:27`) | Launches the app when you sign in |
| Minimize to system tray | On or off | On (`:28`) | Closing the window keeps the app running in the notification area. Starting the app again brings that window back rather than a second copy (`src/SysMonitor.App/App.xaml.cs:396-406`) |
| Show notifications | On or off | On (`:29`) | Enables alert toasts, which fire whichever page is showing and while the window is in the notification area (`src/SysMonitor.App/Views/SettingsPage.xaml:76`, `src/SysMonitor.App/Services/TrayIconService.cs:145-149`) |

## Monitoring

| Setting | Type | Default | Effect |
|---|---|---|---|
| Refresh Interval | 1 to 10 seconds | 2 seconds | Saved, but no page reads it yet: each page refreshes on a fixed timer of its own (`:32`, range at `src/SysMonitor.App/Views/SettingsPage.xaml:100`) |
| Warn me when memory usage goes above | 50 to 95 percent | 80 (`:33`, range at `src/SysMonitor.App/Views/SettingsPage.xaml:121`) | The usage level that raises a memory alert |

The memory threshold raises an alert and nothing else: crossing it trims nothing.
Working sets are trimmed by the TRIM MEMORY button on the Dashboard
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:293`) and on the Memory page
(`src/SysMonitor.App/ViewModels/MemoryViewModel.cs:180`), and by Game Mode, whose memory
step is on unless a caller turns it off
(`src/SysMonitor.Core/Services/GameMode/GameModeService.cs:111`,
`src/SysMonitor.Core/Services/GameMode/IGameModeService.cs:31`). That includes Game Mode
switched on by auto mode when it sees a game start, if you have turned auto mode on
(`src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs:345`).

An "Auto-Optimize Memory" setting existed before v3.0.0 and was removed, because
nothing read it.

What the button does is trim process working sets, which moves pages to the standby
list, from which Windows can page them straight back. It does not free RAM in the sense
of making more of it available
(`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs:90-91`). The result message
has said so since v3.0.0: it reads "Trimmed N from background apps"
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:307`). Up to and including v3.0.1
the Memory page's button read OPTIMIZE MEMORY and reported the drop in used memory as
memory freed; it now reads TRIM MEMORY and reports the same count in the same words
(`src/SysMonitor.App/ViewModels/MemoryViewModel.cs:183`).

The button was labelled BOOST RAM up to and including v3.0.0, which was the last piece
of that flow still claiming otherwise. From v3.0.1 it reads TRIM MEMORY
(`src/SysMonitor.App/Views/DashboardPage.xaml:240`), with a tooltip explaining the
standby list. If your copy says BOOST RAM you are on v3.0.0 or earlier; the button does
the same thing either way.

## Temperature alerts

| Setting | Default | Notes |
|---|---|---|
| Enable temperature alerts | On (`:36`) | |
| CPU warning | 75 degrees C (`:37`) | |
| CPU critical | 90 degrees C (`:38`) | |
| GPU warning | 80 degrees C (`:39`) | |
| GPU critical | 95 degrees C (`:40`) | |

The page shows each threshold in degrees F, converted from the stored Celsius value
(`SettingsViewModel.cs:43-46`).

Temperature readings need hardware sensor access. Run the app as administrator if the
values read as unavailable.

## Battery alerts

| Setting | Default | Notes |
|---|---|---|
| Enable battery alerts | On (`:54`) | |
| Low Warning | 20 percent (`:55`) | |
| Critical Warning | 10 percent (`:56`) | |

A machine with no battery is handled as a machine with no battery. Before v3.0.0 the
alert service dereferenced a null reading, so every battery alert check threw and was
swallowed (`src/SysMonitor.Core/Services/Alerts/AlertService.cs`).

## Version

The Settings page reads the version from the running assembly
(`SettingsViewModel.cs:74`), so it reports what is actually installed rather than a
number written by hand. For this release it reads 3.1.0.0.

## Where the app writes

Everything the application stores about you is under one folder:

```
%LocalAppData%\SysMonitor
```

| Path | Contents |
|---|---|
| `%LocalAppData%\SysMonitor` | The SQLite history database, `history.db` (`src/SysMonitor.Core/Data/HistoryDbContext.cs:26`), and application state |
| `%LocalAppData%\SysMonitor\settings.json` | Settings (`src/SysMonitor.Core/Services/Settings/SettingsStore.cs:56`) |
| `%LocalAppData%\SysMonitor\Logs` | Serilog output (`src/SysMonitor.App/App.xaml.cs:112`) |
| `%LocalAppData%\SysMonitor\Logs` | Crash reports. These were written to the Desktop before v3.0.0 |

Deleting `%LocalAppData%\SysMonitor` resets everything the application keeps in files.
That statement is true as written, which is why crash reports were moved into it.

Two settings also leave an entry outside that folder, and deleting it does not remove
them. Run at Startup adds a `SysMonitor` value under
`HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
(`src/SysMonitor.App/ViewModels/SettingsViewModel.cs:224`), and a cleaning schedule is a
Task Scheduler task named `SysMonitor Scheduled Cleaning`
(`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningService.cs:56`). Each repeating
backup is a task named `Backup <id>` in the same `SysMonitor` folder, and its settings are in
`%LocalAppData%\SysMonitor\Backups\schedules` (an encrypted backup's password there is
protected with Windows' data protection for your account). Switch these off in the app -
Settings, Scheduled Cleaning, and **Stop schedule** in Backup - before deleting the folder,
and nothing is left behind.

The install folder holds the application, plus `LICENSE.txt` and
`THIRD-PARTY-NOTICES.txt` (`installer/SysMonitorSetup.iss:89-90`).

Exported reports go to your Documents folder
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:328-336`).

### The packaged build

The same paths hold for the packaged (MSIX) build, as the app sees them. Windows
redirects what a packaged app writes under `%LocalAppData%` into that app's own package
data, under `%LocalAppData%\Packages`, and removes it when the app is uninstalled. For
that build, uninstalling is what clears those files.

Up to and including v3.0.1, the packaged build kept its settings somewhere else: in the
package's `LocalSettings`, which the Settings page wrote and nothing else read. The alert
service, the minimize-to-tray check and Auto Game Mode read `settings.json`, so in that
build switching notifications off, changing an alert threshold or switching
minimize-to-tray off was saved and then ignored. Later versions keep settings in
`settings.json` in both builds, and every part of the app reads them from one shared store
(`src/SysMonitor.Core/Services/Settings/SettingsStore.cs:25`). On its first start, a later
version copies across whatever an earlier packaged version left in `LocalSettings`, so
updating loses nothing.

## What leaves your machine

Nothing. Readings stay local. See [PRIVACY_POLICY.md](../PRIVACY_POLICY.md).

The app does not check for updates and cannot download a new version of itself. To
update, download the new release and run it.

## Clearing data

The Settings page has a Clear All Data row in DATA & PRIVACY (32E), with a red
**CLEAR** cap (`SettingsViewModel.cs:179`). It asks first, then resets every setting to
its default and saves that straight away (`SettingsViewModel.cs:181-182`,
`:191-192`). It clears settings only: the history
database and the logs stay where they are.

Up to and including v3.0.1 it cleared the packaged build's `LocalSettings` and nothing
else, so in the unpackaged build it changed nothing on disk. Later versions reset every
setting in both builds.

Deleting `%LocalAppData%\SysMonitor` by hand, with the app closed, removes the history
database and the logs as well.

## Related

- [Pages reference](reference-pages.md)
- [Getting started](tutorial-getting-started.md)
