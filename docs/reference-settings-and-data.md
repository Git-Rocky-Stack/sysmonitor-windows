# Reference: settings and data

Every setting on the Settings page of STX.1 System Monitor v3.0.1, with its default,
and every location the app writes to.

Defaults are read from `src/SysMonitor.App/ViewModels/SettingsViewModel.cs:20-52`. The
Reset command restores exactly these values (`SettingsViewModel.cs:133-159`).

## Appearance

| Setting | Type | Default | Effect |
|---|---|---|---|
| Theme | Light, Dark, System | Dark (index 2) | Application colour scheme (`:20`) |

## Behaviour

| Setting | Type | Default | Effect |
|---|---|---|---|
| Run at Startup | On or off | Off (`:23`) | Launches the app when you sign in |
| Minimize to Tray | On or off | On (`:24`) | Closing the window keeps the app running in the notification area |
| Show Notifications | On or off | On (`:25`) | Enables alert toasts (`src/SysMonitor.App/Views/SettingsPage.xaml:98`) |

## Monitoring

| Setting | Type | Default | Effect |
|---|---|---|---|
| Refresh Interval | 1 to 10 seconds | 2 seconds | How often readings update (`:28`, range at `src/SysMonitor.App/Views/SettingsPage.xaml:122`) |
| Memory Threshold | Percent | 80 (`:29`) | The usage level that raises a memory alert |

The memory threshold raises an alert and nothing else. Nothing optimises memory on its
own. The Dashboard's TRIM MEMORY button is the only thing that trims working sets,
and you start it (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:298`).

An "Auto-Optimize Memory" setting existed before v3.0.0 and was removed, because
nothing read it.

What the button does is trim process working sets, which moves pages to the standby
list, from which Windows can page them straight back. It does not free RAM in the sense
of making more of it available
(`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs:90-91`). The result message
has said so since v3.0.0: it reads "Trimmed N from background apps"
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:311`).

The button was labelled BOOST RAM up to and including v3.0.0, which was the last piece
of that flow still claiming otherwise. From v3.0.1 it reads TRIM MEMORY
(`src/SysMonitor.App/Views/DashboardPage.xaml:286`), with a tooltip explaining the
standby list. If your copy says BOOST RAM you are on v3.0.0 or earlier; the button does
the same thing either way.

## Temperature alerts

| Setting | Default | Notes |
|---|---|---|
| Enable temperature alerts | On (`:32`) | |
| CPU warning | 75 degrees C (`:33`) | |
| CPU critical | 90 degrees C (`:34`) | |
| GPU warning | 80 degrees C (`:35`) | |
| GPU critical | 95 degrees C (`:36`) | |

Temperature readings need hardware sensor access. Run the app as administrator if the
values read as unavailable.

## Battery alerts

| Setting | Default | Notes |
|---|---|---|
| Enable battery alerts | On (`:50`) | |
| Low battery warning | 20 percent (`:51`) | |
| Critical battery warning | 10 percent (`:52`) | |

A machine with no battery is handled as a machine with no battery. Before v3.0.0 the
alert service dereferenced a null reading, so every battery alert check threw and was
swallowed (`src/SysMonitor.Core/Services/Alerts/AlertService.cs`).

## Version

The Settings page reads the version from the running assembly
(`SettingsViewModel.cs:65`), so it reports what is actually installed rather than a
number written by hand. For this release it reads 3.0.1.0.

## Where the app writes

Everything the application stores about you is under one folder:

```
%LocalAppData%\SysMonitor
```

| Path | Contents |
|---|---|
| `%LocalAppData%\SysMonitor` | The SQLite history database, `history.db` (`src/SysMonitor.Core/Data/HistoryDbContext.cs:26`), and application state |
| `%LocalAppData%\SysMonitor\settings.json` | Settings (`src/SysMonitor.Core/Services/Settings/SettingsStore.cs:56`) |
| `%LocalAppData%\SysMonitor\Logs` | Serilog output (`src/SysMonitor.App/App.xaml.cs:82`) |
| `%LocalAppData%\SysMonitor\Logs` | Crash reports. These were written to the Desktop before v3.0.0 |

Deleting `%LocalAppData%\SysMonitor` resets everything the application keeps in files.
That statement is true as written, which is why crash reports were moved into it.

Two settings also leave an entry outside that folder, and deleting it does not remove
them. Run at Startup adds a `SysMonitor` value under
`HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
(`src/SysMonitor.App/ViewModels/SettingsViewModel.cs:202`), and a cleaning schedule is a
Task Scheduler task named `SysMonitor Scheduled Cleaning`
(`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningService.cs:56`). Switch both off
in the app before deleting the folder, and nothing is left behind.

The install folder holds the application, plus `LICENSE.txt` and
`THIRD-PARTY-NOTICES.txt` (`installer/SysMonitorSetup.iss:87-88`).

Exported reports go to your Documents folder
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:333-341`).

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

The Settings page has a Clear All Data command
(`SettingsViewModel.cs:162`). It resets every setting to its default and saves that
straight away (`SettingsViewModel.cs:168-169`). It clears settings only: the history
database and the logs stay where they are.

Up to and including v3.0.1 it cleared the packaged build's `LocalSettings` and nothing
else, so in the unpackaged build it changed nothing on disk. Later versions reset every
setting in both builds.

Deleting `%LocalAppData%\SysMonitor` by hand, with the app closed, removes the history
database and the logs as well.

## Related

- [Pages reference](reference-pages.md)
- [Getting started](tutorial-getting-started.md)
