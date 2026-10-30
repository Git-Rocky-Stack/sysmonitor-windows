# Handoff - 3.1.0 is prepared and held for UAT and the build

**Date:** 2026-10-30
**Branch:** `claude/inspiring-darwin-r2oz1f`, PR against `main`. **Do not merge it until section 3 is done**:
it moves the public download page to 3.1.0 files that do not exist yet.
**Previous handoff:** the 2026-10-22 one (the console look complete, four audits resolved) is in git history at
`97d9703`.

---

## 0. Read this first

1. **Linux session; Windows CI is the judge.** The Architecture suite, `scripts/generate-tokens.py --check` and
   `docs/build-docs.py --check` ran here. The WinUI build, the full suite and the UI smoke run in both themes are
   Windows CI's.
2. **This branch is red on purpose.** `ReleaseVersionTests.TheDownloadPagePublishesARealChecksumForEveryFileItOffers`
   fails while `docs/index.html` reads `RECORD-FROM-BUILD` where the two SHA-256s and download sizes go. That is the
   gate: the branch goes green only once the built files' real values are written in. Never "fix" it with a
   stand-in.
3. **Everything else is green**: Architecture suite 186 passed, 1 failed (the gate above); tokens and docs current.

---

## 1. State

```
$ python scripts/generate-tokens.py --check
Tokens.xaml matches its generator.

$ python docs/build-docs.py --check
All 10 generated pages are current.

$ dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 \
      --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"
Failed!  - Failed: 1, Passed: 186
  (TheDownloadPagePublishesARealChecksumForEveryFileItOffers - the release gate, section 0.2)
```

`main` is at `97d9703` (shadow-copy and repeating backups); its Windows CI run 70 is green. There are no open
issues.

---

## 2. What this branch does

### 2.1 Release 3.1.0, prepared (`fbd3fbc`)

RELEASE_CHECKLIST.md section 2, done:

- `SysMonitor.App.csproj` `<Version>3.1.0</Version>`; `Package.appxmanifest` `Identity Version="3.1.0.0"`.
- CHANGELOG: `[Unreleased]` became `## [3.1.0] - 2026-10-30`, with a new empty `[Unreleased]` above it.
  **Move the date to the tag date at the cut.**
- README badge and "What's new"; FEATURES_AND_USER_GUIDE version line and ABOUT THIS BUILD sentence.
- `UserGuidePage.xaml`: the "Changes since v3.0.1" card is "Release Notes - v3.1.0" (LATEST chip kept), and gains
  the shadow-copy and repeating backups, which landed after the card was written.
- `docs/*.md` stamps, `docs/llms.txt` (version and a 3.1.0 paragraph); pages regenerated.
- `docs/index.html` (section 4 of the checklist, partly): title, social and structured data, the "Version ...
  released" line, download links and file names (`STX-1-ULTIMATE-Setup-3.1.0.exe`,
  `STX-1-ULTIMATE-3.1.0-Portable-x64.zip`), the PowerShell example, and a "3.1.0" section above "3.0.1, a
  wording release". The two checksums and the two download sizes read `RECORD-FROM-BUILD`.

### 2.2 The window's title (`c85566c`)

`MainWindow.xaml` was renamed to STX-1 ULTIMATE System Monitor, but the constructor overwrote it with "SysMonitor -
Windows System Monitor & Optimizer", so the taskbar, Alt+Tab and every UI smoke screenshot showed the old name. The
assignment is removed; `WindowTitleTests` holds it. Internal identifiers that still say `SysMonitor` (the
`%LocalAppData%\SysMonitor` folder, the toast AppId, the Run value, the Task Scheduler folder) are deliberate:
renaming them would orphan existing installs' settings, logs and tasks.

### 2.3 Cut-off controls and blank choice boxes

Read off the CI screenshots at the 1100-wide minimum window. Drive Wiper's 13A status line stopped mid-word
beside its three caps, and Browser Privacy's filter row ran off its panel, cutting the High Risk filter to
"High" and pushing SELECT SAFE ONLY out of reach. Status lines now wrap, and the filters sit two by two. Drive
Wiper's Wipe Method and Scheduled Clean's Schedule Type were blank because `SelectedValuePath` and
`DisplayMemberPath` resolve by name at run time, which fails silently on a plain class. They bind `SelectedItem`
with an `x:DataType` template now, as does Game Mode's profile box; `SelectionBindingTests` keeps the string
paths out. **CI compiles this XAML; the Linux session cannot.** Check the four pages (Drive Wiper, Browser
Privacy, Scheduled Clean, Game Mode) in the next UI smoke run's screenshots.

---

## 3. Left, and why

In order. Each step is in [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

| # | Item | Who / where | Checklist |
|---|---|---|---|
| 1 | UAT: every case in [UAT_TEST_PLAN.md](UAT_TEST_PLAN.md) Pass or accepted N/A (none recorded yet) | A person, on the plan's Windows matrix | 1 |
| 2 | Build and sign from this branch: `.\Build-Release.ps1 -SignCode`; smoke both outputs on a clean VM (I-1 to I-5) | Windows with Inno Setup 6 | 3 |
| 3 | Write the two SHA-256s and sizes into `docs/index.html` over `RECORD-FROM-BUILD`; set the release date (CHANGELOG, `index.html` line and `datePublished`) | This branch; CI goes green | 3-4 |
| 4 | Merge, tag `v3.1.0`, create the GitHub release with both files | GitHub | 4 |
| 5 | Store: MSIX, submission, **screenshots recaptured on real hardware** (CI's VM has no sensors, so its pages read 0 and N/A) | Partner Center | 5 |
| 6 | Install from the page and the Store on a clean machine; watch Issues for a week | - | 6 |

---

## 4. Re-running

```
dotnet build SysMonitor.sln -c Debug -p:Platform=x64
dotnet test
python scripts/generate-tokens.py --check
python docs/build-docs.py --check
dotnet run --project src/SysMonitor.App -- --ui-smoke <folder>
```

On Linux, the Architecture suite alone (install the .NET 8 SDK with `dotnet-install.sh --channel 8.0` if absent, and
`pip install markdown` for the docs build):
`dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"`
