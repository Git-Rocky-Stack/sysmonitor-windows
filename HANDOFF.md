# Handoff — every open finding from the 2026-09-20 audit, resolved

**Date:** 2026-09-20 (second session)
**Branch:** `fix/review-p0-p1`
**Working tree:** dirty — nothing committed.
**State at handoff:** `dotnet build SysMonitor.sln -c Debug -p:Platform=x64` → **0 Warning(s), 0 Error(s)**;
`dotnet test` → **556 passed, 0 failed** (was 401 at the start of this session).

---

## 0. Read this first

This session worked the open-findings list from the previous handoff. **Every item in it was reached.**
Three things you cannot infer from the code:

1. **Every finding was re-verified before it was fixed.** The previous handoff marked §3.2 as
   `REPORTED:` — subagent claims nobody had checked. Each one was checked here, by execution where a
   test could reach it and by reading where it could not. Two turned out to be wrong; see §4.
2. **One file was deleted:** `installer/SysMonitor.iss`. It was compiled by nothing and declared a
   different `AppId` from the script that *is* compiled, which is how you get two parallel installs
   instead of an upgrade. §2.10 has the reasoning. Nothing else was deleted.
3. **One finding was not fixed, deliberately:** PDF form fields are still lost on save. The save now
   *says so* rather than doing it silently. §3 explains why that is the honest answer and not a cop-out.

Everything below is proven — the command was run and its output read — unless it carries `UNVERIFIED:`.

---

## 1. Ledger

| Was | Now | Proof |
|---|---|---|
| `dotnet test` | 401 → **556** passed, 0 failed | §5 |
| `dotnet build -c Debug -p:Platform=x64` | 0 Warning(s), 0 Error(s) | §5 |
| Silent catches the detector could see | 6 of 89 → **89 of 89** | §2.2 |
| Process factories leaking a kernel handle | 3 → **0** | §2.5 |

---

## 2. What was fixed

Each was written test-first, watched failing, then fixed, then break-checked — the implementation
deliberately broken again and the test confirmed to fail. Where a break-check did *not* fail, that is
recorded here rather than quietly dropped.

### 2.1 Every image inserted into a PDF was silently discarded (was #15)

`XImage.FromStream(new MemoryStream(bytes))` throws: PDFsharp reads the stream through `GetBuffer()`,
which a `MemoryStream` built from a byte array refuses. All four insert paths did this. The editor
caught the throw, painted a grey box captioned `[Image]`, and reported the save as a success.

**Proven before the fix** — the saved PDF's content stream literally contained `[Image]`, and a
rendering of the page had **0** pixels of the inserted colour.

**Fix:** `PdfImageSource.Open` (`src/SysMonitor.Core/Services/Utilities/PdfImageSource.cs:29`),
called from all four sites. An image that cannot be decoded now fails the save with a message instead
of being replaced by a placeholder.

**Honest note:** the helper also keeps the stream open until the document is written. That is *not*
what makes images appear — closing it early was measured and changed nothing in PDFsharp 6.1.1. It
guards against a future version reading lazily, and the comment in the file says exactly that.

Tests: `tests/SysMonitor.Tests/Services/PdfImageInsertionTests.cs` (7).

### 2.2 The swallowed-exception detector saw 6 of 89 (was #1 / #2)

The rule matched only a *literally* empty `catch { }`. A body holding nothing but `// Log in production`
discards the exception just as completely, and there were 83 more of those — every one invisible to the
test that existed to catch them.

**Census, run over `src/`:** 491 catch blocks, **89** with no statement at all: 6 empty, 83
comment-only, 8 of those saying `// Log in production`.

**Fix, in two parts:**
- The rule now judges any catch whose body has no statements
  (`tests/SysMonitor.Tests/TestSupport/SwallowedExceptionRule.cs`), and has a self-test that proves it
  can fail before the repository scan is trusted.
- All 89 triaged. The 8 that said "log in production" now log, through Serilog, the way `App.xaml.cs`
  already did. Roughly 30 more that were genuinely deliberate now say why, with `// Best effort:` and a
  reason a reader can check. The rest gained real logging — including
  `AutoGameModeService`, `ProfileService` and `HealthCheckService`, which had no logger at all and now
  take the optional `ILogger<T>` every other Core service takes.

### 2.3 Auto Game Mode said ON and watched nothing (was #20)

`LoadAutoModeSetting` wrote the backing field directly, which skipped the property setter — the only
thing that calls `StartMonitoringAsync`. The toggle read ON after a restart and no game was ever
detected until the user turned it off and on again.

**Fix:** `src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs:88`. The settings path is now
injectable so the test never touches the developer's real settings.

Tests: `tests/SysMonitor.Tests/Services/AutoGameModeStartupTests.cs` (5).

### 2.4 Alert cooldown, and the hour that happens twice (was #21)

Two defects in one gate:
- The cooldown required `state.IsActive`, and the automatic clear-down turns that off without touching
  the timestamp. A CPU oscillating around its threshold on a 5-second poll produced a toast every
  10 seconds against a documented 5-minute cooldown.
- It measured elapsed time on `DateTime.Now`. Local time goes backwards once a year; a negative elapsed
  time reads as "inside the cooldown", so **every** alert was suppressed for the whole repeated hour.

**Fix:** `src/SysMonitor.Core/Services/Alerts/AlertService.cs:237` — UTC, no `IsActive` in the gate, and
a clock moved backwards means the alert is raised rather than swallowed. `ClearAlert(type)` remains the
documented way to ask to be told again sooner.

Tests: `tests/SysMonitor.Tests/Services/AlertCooldownTests.cs` (6). Both halves break-checked separately.

### 2.5 Kernel handles leaked by the handful (was #23)

`proc.Handle` forces `OpenProcess`. **Measured:** 300 trims of one process leaked **155** kernel
handles. `AutoGameModeService` did the same thing to every process on the machine every two seconds.

**Fix:** disposal at every site — `MemoryOptimizer` (`:77`), `AutoGameModeService`, `GameModeViewModel`,
and `ElevatedRegistryHelper`, which now uses `Environment.ProcessPath` and opens no handle at all.

**The tests were the other half of this finding.** All three `MemoryOptimizerTests` asserted `Be(0)`
against invalid process ids, so replacing the whole method body with `Task.FromResult(0L)` left them
green. Two tests were added that cannot survive that, and the `return 0L` substitution was applied to
prove it.

Guard: `tests/SysMonitor.Tests/Architecture/ProcessHandleTests.cs`, with a self-test — it had a real
bug of its own (it read only the brace's own line for a type header, so every member of a class looked
like one method and a leak in one was excused by a `Dispose` in another). That is why the rule now
carries tests of its own.

### 2.6 A saturated 100 Mbps link reported as 11.92 (was #24)

`UploadSpeedBps / (1024 * 1024)` — bytes, not bits, and mebibytes under a name that says megabits.
The existing test asserted the wrong answer, certifying the bug.

**Fix:** `src/SysMonitor.Core/Models/SystemInfo.cs:130`. The test was rewritten to pin the unit.

### 2.7 Two architecture tests that could not fail (was #25)

`EveryViewModelUndoesTheSubscriptionsItMakes` had no "did I find anything" guard and searched the whole
file for the matching `-=`, so moving the unsubscribes into a method nobody calls would have satisfied
it. `NoViewModelClaimsToDisposeSomethingWhenItDisposesNothing` found a `Dispose()` body only when its
closing brace sat at exactly four spaces.

**Fix:** both rules moved into `tests/SysMonitor.Tests/TestSupport/ViewModelLifetimeRule.cs`, given
count guards, and given self-tests. **Proven:** making either rule inert now fails the suite with
"found 0", where before it read as a pass. The subscription rule also now rejects lambda
subscriptions, which no `-=` can ever match.

### 2.8 The overlay outlived the app, and its position dropdown did nothing (was #8 / #9)

`FpsOverlayService` was not `IDisposable`, so `_host.Dispose()` could not reach it. Show the overlay,
close the main window, and a WinUI app with a window still open does not exit.

`FpsOverlayWindow.Position` was an auto-property that was written and never read; the window was
hard-placed at `MoveAndResize(100, 100, 220, 260)` whatever the label said.

**Fix:** `IFpsOverlayService : IDisposable` (`src/SysMonitor.Core/Services/GameMode/IFpsOverlayService.cs:44`),
`FpsOverlayService.Dispose` (`src/SysMonitor.App/Services/FpsOverlayService.cs:117`), and
`ApplyPosition` (`src/SysMonitor.App/Views/FpsOverlayWindow.xaml.cs:70`) driven by
`OverlayPlacement.Place` (`src/SysMonitor.Core/Services/GameMode/OverlayPlacement.cs:16`) — the
arithmetic lives in Core because the cases that matter (a monitor left of the primary has a negative X;
a side taskbar moves the work area's origin) cannot be seen on one screen.

**This turned up two more of the same bug.** The new rule
(`tests/SysMonitor.Tests/Architecture/SingletonDisposalTests.cs`) found `BackupService` and
`AutoGameModeService` holding a `CancellationTokenSource` with no way for the host to shut them down.
Both are now disposable.

Tests: `OverlayPlacementTests` (9), `SingletonDisposalTests` (2).

### 2.9 The guide said Game Mode kills processes (was #6)

It does not. `GameModeService` lowers a priority and puts it back, or sends `CloseMainWindow()` — the
same request as clicking X — and leaves the process running if it declines. `grep` finds no `Kill()`
anywhere in `Services/GameMode`.

**Fix:** five claims corrected in `UserGuidePage.xaml` and `V2_ENHANCEMENTS.txt`.
`tests/SysMonitor.Tests/Architecture/GameModeClaimTests.cs` holds it, **in both directions** — if Game
Mode is ever given the power to kill, the test fails until the guide says so.

### 2.10 The installer shipped 1.0.0 for a 2.2.2 app (was #4 / #5)

The version was written by hand in four places and three were stale. `Build-Release.ps1` named the
installer and the portable zip 1.0.0; `installer/SysMonitorSetup.iss` — the script all three build paths
actually compile — declared 1.0.0, so Add/Remove Programs and `HKLM\SOFTWARE\…\Version` both reported a
release that never existed.

**Fix, at the root rather than the symptom:**
- `installer/SysMonitorSetup.iss:11` reads the version out of the executable it is packaging, so it
  cannot disagree with the binary it ships.
- `Build-Release.ps1:54` reads it from the csproj. **Proven:** running that line resolves to `2.2.2`.
- `installer/SysMonitor.iss` **deleted**. Nothing compiled it, and it carried a different `AppId` —
  Inno Setup's upgrade identity — so installing from both would have left two copies side by side.
  It was read first; the only thing it had that the compiled script lacked (an uninstall-time prompt
  for user data) the compiled script already has, in a better form.
- `installer/INSTALLER_README.txt` rewritten: it named the deleted script as "Main", gave an output
  path that does not exist, and documented editing a version that is no longer written by hand.

Tests: `tests/SysMonitor.Tests/Architecture/ReleaseVersionTests.cs` (5).

### 2.11 Saving a PDF lost its description, its bookmarks, and sometimes its page order (was #16 / #17)

Both were `REPORTED:` by a subagent. **Both confirmed here by execution**: after a plain save, `Title`
was empty and `Outlines.Count` was 0. And reordering pages, saving over the source, then saving again
put page 2 where page 3 belonged — the page numbers the document holds point into the *source* file,
and after a save-over they point into the file that was just rewritten.

**Fix:** `PdfEditor.cs:201-202` carries the description and rebuilds the bookmarks against the pages
actually written, dropping any whose page the user deleted; `RenumberToSavedLayout` (`:232`) makes the
second save a no-op.

Tests: `tests/SysMonitor.Tests/Services/PdfSaveFidelityTests.cs` (6).

### 2.12 The signature tool was an orphan (was #18)

`FinalizeSignature` turned the strokes the user drew into an annotation. `grep` found **one** occurrence
of the name in the whole repository: its own declaration. Draw a signature, save, and nothing was
written — the save reported success because as far as it knew there was nothing to write. A comment
next to it said signatures are finalised "when user clicks Save Signature or changes tool". Neither
happened.

**Fix:** `SelectToolAsync` (`src/SysMonitor.App/Views/PdfEditorPage.xaml.cs:152`) finishes a pending
signature before switching tool, and saving does the same. `async void` became `async Task`.

Guard: `tests/SysMonitor.Tests/Architecture/OrphanHandlerTests.cs`. This one needed care — a
`<see cref="FinalizeSignatureAsync"/>` in the documentation of the method that is *supposed* to call it
reads, to a text search, exactly like a call. The rule strips comments first, and the break-check that
caught that is the reason it does.

### 2.13 Game Mode's power plan could be recorded as High Performance (was #19)

`_isEnabled` was a plain `bool` with no lock and three callers on three threads. A second activation
while the first is running reads the *already changed* plan as "previous".

**Proven by execution:** after two activations and a deactivation the machine was left on High
Performance — and the crash-recovery note on disk said High Performance too, so it would have been
re-applied on **every launch from then on**.

**Fix:** `GameModeService.cs:50` — a `SemaphoreSlim` serialising the transitions, and an idempotence
check so a second activation does not re-read the plan.

Tests: `tests/SysMonitor.Tests/Services/GameModeReentryTests.cs` (5).

### 2.14 TemperatureMonitor: one object, four timers, no lock

All three subagent claims confirmed by reading. One `TemperatureMonitor` is shared by the 5-second
dashboard timer, the 30-second history timer, the alert check and the overlay loop; every read calls
`IHardware.Update()`, which rewrites the sensor tree in place. `_isInitialized` was a plain field, so
two callers could each open a `Computer` and the second would replace the first. And
`ITemperatureMonitor` declared `Dispose()` without extending `IDisposable`, so nothing could see there
was a kernel driver to give back — `grep` found no caller.

**Fix:** `TemperatureMonitor.cs:25` gates every touch of the hardware; `IMonitors.cs:100` makes the
interface disposable.

**Honest limit:** LibreHardwareMonitor needs administrator rights to open its driver, and the test shell
does not have them — measured, **0 temperature sensors and 0 fans** on this machine. So
`TemperatureMonitorConcurrencyTests` exercised only the no-sensor path here. It is still a real test,
and on an elevated machine it drives the real sensor tree from a dozen threads. The test says this in
its own documentation rather than implying more.

### 2.15 The large-file scan walked everything twice

`{ root } ∪ root's subfolders`, each passed to a recursive walk. **Proven:** a tree with four large
files returned seven rows and a total of exactly double the bytes on disk.

**Fix:** `LargeFileFinder.cs:102` — the root's own files are their own unit of work, not a second walk
of everything below it. `FileScanning.EnumerateFilesIn` added for that.

### 2.16 A user with 400 GB in OneDrive got no results

`FileScanning` skipped every entry carrying a reparse point. Right for a junction or a symbolic link.
Wrong for the two other things that carry one and are not links: a OneDrive Files On-Demand
placeholder, and a file stored by Data Deduplication. Both are real files, at that path, with a real
size — and the scan silently returned nothing, with nothing on screen to say why.

**Fix:** `FileScanning.IsLinkToElsewhere` (`:71`) — `LinkTarget` is what tells them apart. Neither a
placeholder nor a deduplicated file can be created in a test, which is exactly why the decision is a
function of the attributes and the link target rather than something buried in the walk; the existing
junction and symlink tests prove it is wired in.

### 2.17 Wi-Fi: a padlock on networks nobody had checked

Three places filled the security in with `"WPA2" // Assumed`, and the page's test for a padlock was
"not empty and not Open" — so a network whose encryption was never read was shown to the user as
encrypted. A fourth set `IsSecured = true` outright.

**Fix:** all four now record `Unknown`. The page has three states instead of two
(`src/SysMonitor.App/ViewModels/WiFiViewModel.cs:261`): encrypted, open, and not known, with its own
grey icon. The "open networks" count no longer includes the ones it could not read, which would have
overstated how much of the air around the user is unencrypted.

Also fixed: the 6 GHz arithmetic was out by one (`(mhz - 5950) / 5 + 1` calls 5955 MHz channel 2 when
it is channel 1), there was no 6 GHz case in the reverse conversion, and any channel above 14 was
labelled "5 GHz" — including 6 GHz channels, which start again at 1.
`WiFiChannels` (`src/SysMonitor.Core/Services/Utilities/WiFiChannels.cs:53`) now says "Unknown" where a
channel number genuinely does not settle the band. **Proven:** the shipping formulas fail 9 of the
37 new tests.

### 2.18 "Battery Critical! Battery is at 0%" on a desktop

`GetSystemPowerStatus` says "I do not know" two ways and both were read as facts. `BatteryFlag` 255
means unknown — only 128 means no battery. `BatteryLifePercent` 255 also means unknown, and the reading
was `percent <= 100 ? percent : 0`, so unknown became **0%** — which the alert service compares against
the critical threshold.

**Fix:** `BatteryStatusReading.Read` (`src/SysMonitor.Core/Services/Monitors/BatteryStatusReading.cs:36`).

### 2.19 The wiper would have overwritten the kernel

`SecureDeleteDirectoryAsync` refuses Windows, Program Files and the rest. `SecureDeleteFileAsync` never
asked: pick `C:\Windows\System32\ntoskrnl.exe` in the file dialog and it went straight through to the
overwrite. **Proven by execution** before the fix.

**Fix:** `DriveWiper.cs:91`, asked before anything else — including whether the file is there, so the
test can exercise it without ever naming a file it could destroy.

Also: `WipeResult.FailedPaths` records overwrites the wiper could not read back to confirm, and had
**zero readers** anywhere in the app. "Successfully wiped" was printed over the top of them.
`DriveWiperViewModel.cs:203` now reports them.

### 2.20 A scheduled clean that deleted files the moment you saved it

The trigger was `DateTime.Today.Add(timeOfDay)` — this morning, already past for most of the day. Task
Scheduler treats a start in the past as overdue, and with "run missed schedules" on it starts one within
minutes. Setting up a 2 AM clean at four in the afternoon deleted files immediately.

**Fix:** `ScheduledCleaningService.FirstRunAfter` (`:179`). `DayOfMonthWithin` also added: the
configured day was passed through untouched, and Task Scheduler rejects anything outside 1–31, which
left the user believing a schedule had been created when none had.

### 2.21 The backup password, in clear text, next to the backup

`ScheduleBackupAsync` serialises the whole schedule to a JSON file in the user's profile — including
`BackupJob.EncryptionPassword` and `NetworkPassword`. The password protecting the backup, in a file
beside it.

Not currently reachable: nothing in the UI calls `ScheduleBackupAsync`. Fixed anyway —
`IBackupService.cs:115` marks both `[JsonIgnore]`.

Also: backup verification said "all N files match" while silently excluding files the backup stored
without a checksum. The message now says how many could not be checked.

### 2.22 Work that outlived the page that started it

`RegistryCleanerViewModel` and `DriveWiperViewModel` had no `CancellationTokenSource`, no `IDisposable`
and no cleanup; `BackupViewModel` had a token source but nothing disposed it, and none of the three
pages had an `OnNavigatedFrom`. A registry scan walks tens of thousands of keys — leaving the page left
it running, still writing into a collection bound to a page that was gone, holding the page alive.

`IRegistryCleaner.ScanAsync` took no cancellation token at all, so there was nothing to give it.

**Fix:** the token threaded through `IRegistryCleaner` (checked between locations, and between entries
in a clean — never part way through one, because a half-applied registry change is worse than one that
was not started); all three view models disposable; all three pages disposing on the way out.
`BackupViewModel`'s backup token is now linked to the page's, so leaving cancels the backup as surely as
pressing Cancel.

Tests: `tests/SysMonitor.Tests/Services/PageWorkCancellationTests.cs` (6).

---

## 3. Not done

**PDF form fields are still lost when a document is saved from the editor.** Confirmed by probe: the
source has an `/AcroForm`, the saved file does not.

Saving builds a new document and re-imports each page. The widget annotations come across with their
pages, but the catalog-level `/AcroForm` that ties them into fields does not, and rebuilding it
correctly for fields with child widgets is more than this editor can promise. **A form that looks
present and does not work is worse than one the user was told about**, so `SavePdfAsync` now returns a
warning and the page shows it (`PdfEditor.cs:205`, `PdfOperationResult.Warnings`). If form editing is
ever wanted, this is the piece of work.

Two further limits worth knowing:

- **`TemperatureMonitorConcurrencyTests` proved less on this machine than it will on yours.** See §2.14.
- **`ReorderingPagesThenSavingTwice` passes with or without the fix.** It saves to two different paths,
  where no renumbering is needed. It is a genuine regression guard for that case, but it was not the
  test that caught the bug — `SavingOverTheSourceThenSavingAgain` was.

---

## 4. Findings that were wrong

Both were `REPORTED:` by subagents in the previous session and are recorded here so nobody re-raises them.

- **`BackupService` restore-to-original is already bounded.** The claim was that `SourceRoots` is read
  from inside the archive and therefore trusted. It is read from inside the archive, but the plan
  refuses any destination outside those roots *and* exposes `HasRecordedSourceRoots` so the caller can
  tell when there are none — which `BackupPage.xaml.cs:243` reads and warns on. Working as designed.
- **`GameModeService` disposes the processes it enumerates.** The first version of the
  process-handle rule flagged `GameModeService.cs:182`; that was the rule's bug, not the code's — the
  processes are disposed in a `finally` inside the loop. The rule was fixed, not the code.

---

## 5. How to verify the current state

```bash
cd sysmonitor-windows

dotnet build SysMonitor.sln -c Debug -p:Platform=x64   # expect: 0 Warning(s), 0 Error(s)
dotnet test                                            # expect: Passed: 556, Failed: 0
```

The rules that judge the source now carry self-tests, so a rule that stops working fails the build
instead of reading as a clean bill of health:

```bash
dotnet test --filter "FullyQualifiedName~SwallowedExceptionTests"
dotnet test --filter "FullyQualifiedName~ProcessHandleTests"
dotnet test --filter "FullyQualifiedName~ViewModelLifetimeTests"
dotnet test --filter "FullyQualifiedName~SingletonDisposalTests"
dotnet test --filter "FullyQualifiedName~OrphanHandlerTests"
dotnet test --filter "FullyQualifiedName~ReleaseVersionTests"
dotnet test --filter "FullyQualifiedName~GameModeClaimTests"
dotnet test --filter "FullyQualifiedName~DocumentationAnchorTests"
```

Every `path:line` anchor in this document is checked by `DocumentationAnchorTests`. It catches an anchor
that points at nothing, which is the failure that happens by itself when code moves. It cannot tell you
a line still *says* what the document claims — only a reader can.

**One test class runs on its own** (`SerialCollection`): `MemoryOptimizerTests` and
`TemperatureMonitorConcurrencyTests` measure handle counts belonging to the whole test process, and a
dozen other tests opening files at the same time drowns the signal. That is why they are serialised, and
it is worth knowing before anyone "tidies up" the attribute.

---

## 6. Process notes

- **Two of this session's own detectors had bugs that made them silent**, and both were caught only by
  break-checking them against the bug they were written for. A source-scanning rule that has never been
  seen to fail is not evidence of anything. Every rule added here has a self-test that feeds it code
  whose verdict is known.
- **The registry-restore caveat from the previous handoff still stands** and was not revisited: the
  fingerprint sidecar sits in the same user-writable folder as the backup, so same-user malware could
  forge both. Closing it properly needs an Administrators-only location. It is still not "fixed".
- **No test in this session wrote anything to a machine-wide location.** The wiper test names a file
  inside the real Windows folder and asserts it does not exist before going near it; the Game Mode
  tests use a stand-in power-plan controller and a temporary state file.
