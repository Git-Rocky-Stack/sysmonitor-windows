# Release Checklist - STX.1 System Monitor

How a version goes from a green branch to people's machines: the GitHub release (Setup and
portable zip), the public page, and the Microsoft Store. Work top to bottom; each step names the
file or command it touches. The next release is **3.1.0**: the Command Console look and the audit
fixes, listed in [CHANGELOG.md](CHANGELOG.md) under Unreleased.

## 1. Before the cut

- [ ] The release branch is merged to `main`, and Windows CI is green on `main`'s head
      (build, the full test suite, the UI smoke run in both themes with animations on and off,
      `scripts/generate-tokens.py --check`, `docs/build-docs.py --check`).
- [ ] [UAT_TEST_PLAN.md](UAT_TEST_PLAN.md) is signed off: every case Pass or an accepted N/A.
- [ ] No open issue is labelled as blocking the release.

## 2. Cut the version

One number drives everything. `ReleaseVersionTests` fails if any of these disagree.

- [ ] `src/SysMonitor.App/SysMonitor.App.csproj`: `<Version>3.1.0</Version>`.
- [ ] `src/SysMonitor.App/Package.appxmanifest`: `Identity Version="3.1.0.0"`.
- [ ] `CHANGELOG.md`: rename `## [Unreleased]` to `## [3.1.0] - <date>` and open a new empty
      `## [Unreleased]` above it. Drop the "planned as 3.1.0" sentence.
- [ ] `README.md`: the version badge; "What's new" says "in 3.1.0" rather than "since v3.0.1".
- [ ] `src/SysMonitor.App/Views/UserGuidePage.xaml`: the "Changes since v3.0.1" card becomes
      "Release Notes - v3.1.0"; the LATEST chip moves to it.
- [ ] `docs/*.md` version stamps (`docs/README.md`, `docs/reference-pages.md`,
      `docs/reference-settings-and-data.md`, `docs/tutorial-getting-started.md` download name,
      `docs/llms.txt`), then `python docs/build-docs.py`.
- [ ] Run the tests; commit "Release 3.1.0".

## 3. Build and sign

- [ ] On Windows with the .NET 8 SDK and Inno Setup 6: `.\Build-Release.ps1 -SignCode`
      (certificate by thumbprint in `SYSMONITOR_SIGNING_THUMBPRINT`; see `signing/`). Without a
      code-signing certificate, build unsigned and keep the SmartScreen note in the tutorial.
- [ ] Outputs: `publish\installer\STX1-SystemMonitor-Setup-3.1.0.exe` and the portable
      `STX1-SystemMonitor-3.1.0-Portable-x64.zip`.
- [ ] Smoke the two outputs by hand on a clean VM: UAT cases I-1 to I-5.
- [ ] Record SHA-256 of both: `Get-FileHash <file> -Algorithm SHA256`.

## 4. Publish on GitHub

- [ ] Tag `v3.1.0` on the release commit and create the release from it, attaching both files.
      Body: the CHANGELOG 3.1.0 section, and both SHA-256s.
- [ ] `docs/index.html` (hand-written, not generated): every `3.0.1` in the download links,
      the structured data, the title and description, the "Version ... released" line, and the
      two checksums under "SHA-256 checksums". Put in the checksums you recorded - never a
      placeholder. Add a short "3.1.0" section beside "3.0.1, a wording release".
- [ ] Push; GitHub Pages redeploys. Check the page's buttons download the new files.

## 5. Microsoft Store

- [ ] Partner Center: the package identity in `Package.appxmanifest` (Name, Publisher) matches
      the reserved app. The placeholder `PhoneIdentity` is gone; `MaxVersionTested` is 26100.
- [ ] Build the MSIX from Visual Studio (Publish > Create App Packages) for x64 and ARM64, or
      x64 only if ARM64 has not passed UAT on an ARM64 device.
- [ ] Submission:
  - [ ] **Restricted capability `runFullTrust`** justification: "A desktop system utility. It
        reads hardware sensors through LibreHardwareMonitor, enumerates processes, startup
        entries, drivers and installed programs, and cleans, backs up and wipes files the user
        selects - none of which the AppContainer allows."
  - [ ] Privacy policy URL: the published `PRIVACY_POLICY.md` (or a page on the Pages site with
        the same text).
  - [ ] Age rating questionnaire (no user content, no purchases, no network features for
        strangers).
  - [ ] Screenshots: recapture on real hardware in the Command Console look, Night Ops and Day
        Shift - Dashboard, CPU, Directory Cleaner, Backup, Driver Updater, Game Mode. The
        `App Screenshots/v 2.2.0.0 (Microsoft store)` set shows the old look; the two in
        `App Screenshots/Command Console/` come from a CI virtual machine with no sensors and
        are stand-ins.
  - [ ] Description and features list agree with `README.md` - especially the features that
        were removed in 3.0.0 (RAM Cache, 7z, Auto-Optimize Memory, the Performance FPS card).
- [ ] One name and one publisher across the listing, the manifest (`STX-1 ULTIMATE System
      Monitor`, Strategia-X), the installer (`STX1 System Monitor`, Rocky Stack) and the README
      (`STX.1 System Monitor`). Choose one and align the others in the next release.

## 6. After release

- [ ] Install from the public page and from the Store on a clean machine; About shows 3.1.0
      and the User's Guide shows this build's date.
- [ ] Watch GitHub Issues for a week. Support asks for the log folder,
      `%LocalAppData%\SysMonitor\Logs`, and the page's kicker (`DASH - SYSTEM HEALTH - 01B`) to
      say exactly where something happened.

## Repository housekeeping (do once)

These are tracked in the repository but are not part of the Windows app; confirm and remove, or
move out of the repo:

- `App Bundle/app-release.aab` (an Android bundle, about 20 MB)
- `v.2.0.1 Exec/`
- `Microsot Store Submission.png`
- `STX-1 ULTIMATE System Monitor - LinkedIn.txt`
