# Performance work in SysMonitor for Windows

## What this document is

Each section below names one change and the file and line it lives at, so you can go and read it.

**It contains no before-and-after timings.** The version of this document that did — "startup time 2–5
seconds → <1 second", "30–40 FPS → 55–60 FPS", "150ms freeze → <20ms", and twenty-odd more — had no
benchmark behind it. There is no benchmark in this repository, nothing recorded a number before the changes
were made, and one of the headline claims was not even mechanically possible: it credited the startup gain to
moving from `services.AddSingleton<ICpuMonitor, CpuMonitor>()` to a two-line registration through a factory,
and both of those construct the service on first resolve, not at registration. The `LazyServiceWrapper<T>`
that document leaned on sat in `App.xaml.cs` under a summary claiming it reduced startup by 2–5 seconds, with
zero references anywhere in the app. It has been deleted.

The changes below are real and worth describing. What they gained is not known, so it is not claimed.

---

## 1. CPU usage is read from the kernel, not through a performance counter

`src/SysMonitor.Core/Services/Monitors/CpuMonitor.cs:159`

`GetSystemTimes` (kernel32) returns idle, kernel and user tick counts. Usage is the change in those between
two calls, so the first reading of a session primes the counters and returns 0. The previous implementation
called `PerformanceCounter.NextValue()`, which is kept as a fallback if `GetSystemTimes` fails (`:161`).

Covered by `CpuMonitorTests.GetUsagePercentAsync_NoticesWorkTheMachineIsDoing`, which makes a core busy and
requires the reading to notice.

## 2. Temperature and per-core readings are cached briefly

`src/SysMonitor.Core/Services/Monitors/CpuMonitor.cs:35` — temperature for 2 seconds, since it comes from a
WMI query (`MSAcpi_ThermalZoneTemperature`) and the dashboard asks more often than the value changes.
Per-core usage is cached the same way.

## 3. The process cache is bounded

`src/SysMonitor.Core/Services/Monitors/ProcessMonitor.cs:43` — at most 300 entries, held in a
`ConcurrentDictionary`, with dead processes and entries older than five minutes dropped before the oldest are
evicted. It was previously unbounded.

## 4. The process list is updated in place

`src/SysMonitor.App/ViewModels/ProcessesViewModel.cs` — the collection is reconciled against the new list
rather than cleared and refilled, so the ListView keeps its scroll position and each refresh raises far fewer
change notifications. Typing in the search box is debounced by 300ms (`:24`).

## 5. Scanning is done in parallel

`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs` and
`src/SysMonitor.Core/Services/Utilities/LargeFileFinder.cs:94` both fan out over top-level directories with
`Parallel.ForEach`.

## 6. WMI answers that do not change are cached

`src/SysMonitor.Core/Services/Monitors/DiskMonitor.cs:14` caches per-drive facts, including whether a drive is
solid state. `src/SysMonitor.Core/Services/SystemInfoService.cs` does the same for operating-system details.

## 7. There is a performance monitor in the app

`src/SysMonitor.Core/Services/Monitoring/PerformanceMonitor.cs` records how long named operations take and
`src/SysMonitor.App/ViewModels/PerformanceViewModel.cs` shows the count, average, minimum, maximum and 95th
percentile per operation, with CSV export. This is the thing that could produce the numbers this document
used to assert: it measures the app as it runs, on the machine it runs on.

---

## If you want the numbers

Take them from the Performance page while using the app, or write a benchmark and commit it next to the
result. A figure in a document that nothing produced is worse than no figure at all, because it is quoted
later as though someone measured it.
