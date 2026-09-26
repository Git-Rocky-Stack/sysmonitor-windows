# Reference: settings and data

Every setting on the Settings page of STX.1 System Monitor v3.0.1, with its default,
and every location the app writes to.

Defaults are read from `src/SysMonitor.App/ViewModels/SettingsViewModel.cs:20-52`. The
Reset command restores exactly these values (`SettingsViewModel.cs:260-286`).

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

The memory threshold raises an alert and nothing else: crossing it trims nothing.
Working sets are trimmed by the TRIM MEMORY button on the Dashboard
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:292`) and on the Memory page
(`src/SysMonitor.App/ViewModels/MemoryViewModel.cs:175`), and by Game Mode, whose memory
step is on unless a caller turns it off
(`src/SysMonitor.Core/Services/GameMode/GameModeService.cs:111`,
`src/SysMonitor.Core/Services/GameMode/IGameModeService.cs:31`). That includes Game Mode
switched on by auto mode when it sees a game start, if you have turned auto mode on
(`src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs:374`).

An "Auto-Optimize Memory" setting existed before v3.0.0 and was removed, because
nothing read it.

What the button does is trim process working sets, which moves pages to the standby
list, from which Windows can page them straight back. It does not free RAM in the sense
of making more of it available
(`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs:90-91`). The result message
has said so since v3.0.0: it reads "Trimmed N from background apps"
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:306`). Up to and including v3.0.1
the Memory page's button read OPTIMIZE MEMORY and reported the drop in used memory as
memory freed; it now reads TRIM MEMORY and reports the same count in the same words
(`src/SysMonitor.App/ViewModels/MemoryViewModel.cs:178`).

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
(`SettingsViewModel.cs:87`), so it reports what is actually installed rather than a
number written by hand. For this release it reads 3.0.1.0.

## Where the app writes

Everything the application stores about you is under one folder:

```
%LocalAppData%\SysMonitor
```

| Path | Contents |
|---|---|
| `%LocalAppData%\SysMonitor` | Settings, the SQLite history database, and application state (`src/SysMonitor.App/App.xaml.cs:72`) |
| `%LocalAppData%\SysMonitor\Logs` | Serilog output |
| `%LocalAppData%\SysMonitor\Logs` | Crash reports. These were written to the Desktop before v3.0.0 |

Deleting `%LocalAppData%\SysMonitor` resets the application completely. That statement
is true as written, which is why crash reports were moved into it.

The install folder holds the application, plus `LICENSE.txt` and
`THIRD-PARTY-NOTICES.txt` (`installer/SysMonitorSetup.iss:87-88`).

Exported reports go to your Documents folder
(`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:327-335`).

## What leaves your machine

Nothing. Readings stay local. See [PRIVACY_POLICY.md](../PRIVACY_POLICY.md).

The app does not check for updates and cannot download a new version of itself. To
update, download the new release and run it.

## Clearing data

The Settings page has a Clear All Data command
(`SettingsViewModel.cs:289`). It resets settings and stored data. Deleting
`%LocalAppData%\SysMonitor` by hand does the same thing.

## Related

- [Pages reference](reference-pages.md)
- [Getting started](tutorial-getting-started.md)
