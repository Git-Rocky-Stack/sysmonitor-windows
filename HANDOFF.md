# Handoff - the console look is complete, the audit is resolved, and the release is ready to cut

**Date:** 2026-10-22
**Branch:** `claude/serene-albattani-lababk`, draft PR #98 against `main`
**Previous handoff:** the 2026-10-05 one (fields, buttons, headers, lamp states, instruments) is in git history at `b9df9ce`.

---

## 0. Read this first

1. **Linux session; Windows CI is the judge.** The Architecture suite (183 tests), the service tests that run
   on Linux, `scripts/generate-tokens.py --check` and `docs/build-docs.py --check` ran here. The WinUI build,
   the full suite (junction, registry and WMI tests included) and the UI smoke run in both themes are Windows
   CI's; every pushed commit up to `5fda861` came back green or was superseded by one that did.
2. **The version is still 3.0.1 on purpose.** The work is under CHANGELOG's Unreleased, planned as 3.1.0.
   `docs/index.html` lists the SHA-256 of the published 3.0.1 files; a 3.1.0 page needs the checksums of files
   that do not exist until the release is built. [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) is the cut.
3. **Four audits were run** (architecture and lifetime, Core service safety, UI/UX and accessibility, QA and
   release) and their confirmed findings are fixed, below. What was left, and why, is in section 3.

---

## 1. State

```
$ python scripts/generate-tokens.py --check
Tokens.xaml matches its generator.

$ dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 \
      --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"
Passed!  - Failed: 0, Passed: 183
```

---

## 2. What was done

### 2.1 The look is complete (`559cfd0`, `5fda861`)

- **Every major section is a `Faceplate`**, 106 across the pages plus Backup's status sidebar (15E). The stripe's
  kicker is `<MODULE> - <SECTION> - <INDEX><LETTER>`, the module and index from the page's `ViewHeader`, letters in
  document order, serial from `SerialFor`. `FaceplateKickerTests` holds it. Gated sections carry their gate on the
  faceplate; footers stay on the chassis; a narrow stripe cuts its serial, not its kicker.
- **Caps for tiles and icons**: `TileCapButtonStyle` (stack spans the tile, words wrap), `IconCapButtonStyle`,
  `ArmedIconCapButtonStyle` (`src/SysMonitor.App/Styles/Console/Controls.xaml`). 28 hand-painted insets are `Well`s.
- No XAML names a font family; the signature face is `SignatureInkFontFamily`, shared with the PDF renderer.
- `SectionHeaderStyle` / `SectionHeaderWithAccentStyle` are deleted; `AccentBarStyle` remains for sub-headings
  inside a panel (PDF Editor).
- Network Mapper's columns are restacked; the PDF Editor is module 35 (its 25B collided with PDF Tools' 25B).

### 2.2 Data safety (`6051e09`, `792cf18`)

Registry scan (bare names, unmounted drives, type-library versions), Quick Fix backup-first, temp/browser cleans
that never follow junctions and remove only emptied folders, Recycle Bin opt-in, backups (source folders, whole-
folder matching, unreadable folders, `.git`, encryption leftovers, retention), restore points (listed, confirmed),
free-space wipe (FAT32, shortfall), PDF merge, unpack without overwrite, Browser Privacy (freed bytes, SQLite
sidecars, Firefox cache and profile, bookmarks at high risk), 32-bit startup entries, used-space. Each is in
CHANGELOG with its file; tests are in `tests/SysMonitor.Tests/Services/` (`HealthQuickFixTests`,
`FileToolsOutputTests`, `BackupServiceUncompressedTests`, `RegistryComScanTests`, `FileScanningTests`).

### 2.3 Confirmation (`7e085e8`)

A view model asks through `Confirmation` (`src/SysMonitor.App/ViewModels/Confirmation.cs`); its page wires it to
`ConsoleDialog.ConfirmAsync`; an unwired one answers no. `ArmedActionConfirmationTests` checks both ends. Dialog
buttons read in capitals. END TASK refuses Windows' own processes and reports the outcome.

### 2.4 Lifetime and navigation (`82cf47a`, `957d310`)

Single instance (`App.HandedToRunningCopyAsync`), alerts from `TrayIconService`, `HistoryService.Dispose` idempotent,
refresh loops not started after dispose, the rail following the frame, watch-only pages parked while hidden, the
window's minimum size (`Helpers/WindowMinimumSize.cs`), status lines that keep failures.

### 2.5 Accessibility and polish (`957d310`)

Accessible names on every unlabelled input, result checkbox and icon cap; kickers as headings; text >= 11;
empty states on Large Files and Duplicate Finder; Startup's actions wait for a selection; PDF Editor shortcuts.

### 2.6 Release and docs (`cec4a1f`, this commit)

Setup's `MinVersion`, manifest PhoneIdentity removed and `MaxVersionTested` 26100, build date stamped in the
assembly, smoke run failing on binding failures and unhandled exceptions (the last run had none), unused installer
readmes removed. Docs: README, FEATURES_AND_USER_GUIDE, docs/*.md, the in-app guide's "Changes since v3.0.1",
PRIVACY_POLICY (network activity, browser profiles, Wi-Fi, where data lives), and two new files:
[UAT_TEST_PLAN.md](UAT_TEST_PLAN.md) and [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

---

## 3. Left, and why

| Item | Why it is not done | Where it is tracked |
|---|---|---|
| Cut 3.1.0 | Needs the built files' checksums for `docs/index.html` | RELEASE_CHECKLIST.md 2-4 |
| Store screenshots in the new look | Need real hardware; CI's VM has no sensors | RELEASE_CHECKLIST.md 5 |

---

## 4. Re-running

```
dotnet build SysMonitor.sln -c Debug -p:Platform=x64
dotnet test
python scripts/generate-tokens.py --check
python docs/build-docs.py --check
dotnet run --project src/SysMonitor.App -- --ui-smoke <folder>
```

On Linux, the Architecture suite alone:
`dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"`
