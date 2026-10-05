# Changelog

All notable changes to STX.1 System Monitor are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each entry describes a behaviour change and cites the file it lives in.

---

## [Unreleased]

### Changed

- **A button is a machined cap.** Buttons that asked for no style of their own were drawn
  by WinUI. They are now cut from the chassis, to System-X's own measurements: 34 high, 14
  either side of the word, the word itself Archivo at 11.5 tracked .08em on the 4px control
  radius, over the palette's cap face with a key light across the top - going in under the
  finger and dimming to .42 when they are disabled, which is what System-X dims a locked
  control to, except in High Contrast, where the palette leaves the dimming at 1 and greys
  the word with the user's own GrayText instead
  (`src/SysMonitor.App/Styles/Console/Controls.xaml:67`,
  `src/SysMonitor.App/App.xaml:24`). Two caps carry more weight and differ only in their
  face: the armed cap for a command that cannot simply be undone
  (`src/SysMonitor.App/Styles/Console/Controls.xaml:166`) and the chrome cap for a page's
  single call to action (`:173`). The palette had written all three faces and Fonts.xaml
  the cap face since the tokens were generated; until this dictionary existed nothing read
  any of them. The smoke run builds each cap in the live tree, in both shifts, and checks
  the face it came out with against the palette
  (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:603`), and each measurement is pinned to
  the rule it was read from, so a cap cannot be quietly resized
  (`tests/SysMonitor.Tests/Architecture/ConsoleCapTests.cs:131`).

- **A switch is a bat lever.** A toggle was WinUI's pill with a round knob sliding in it. It is
  now a recess cut into the chassis with a machined cap for a thumb: 42 by 22 on the 4px control
  radius, the thumb 16 square on the 3px inset radius, and the shade the top edge casts into the
  recess drawn as a gradient, since WinUI has no inset shadow. Turned on it arms - the track, its
  rim and the thumb all going over to armed red together, the thumb travelling 20
  (`src/SysMonitor.App/Styles/Console/Controls.xaml:194`). Locked, it dims to .42, which is what
  a locked cap does, because System-X writes that rule once and points the switch at it. The palette
  gains the eight colours the lever needs, written for all three shifts by the generator
  (`scripts/generate-tokens.py:192`, `:305`).

  WinUI's template parts are kept name for name, because ToggleSwitch's own code drives them: it
  measures two of them to work out how far the knob travels and how far a drag may go, and writes
  a third while a finger drags it. A template that renames one still builds and still draws, and
  loses what that part drove with nothing to say so, so the list of them is a test
  (`tests/SysMonitor.Tests/Architecture/ConsoleSwitchTests.cs:26`). The header and the on and off
  words stay for the same reason: the pages use both. The smoke run builds a switch in both
  shifts, turns it on and off again, and checks every face against the palette and the travel
  against the number WinUI works out for itself
  (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:665`).
- **A form field is a recessed well.** Text boxes, password boxes and number boxes were WinUI's,
  or a legacy style with a pill radius and hardcoded colours. They are now System-X's `.field`:
  a dark well cut into the plate, dark in both shifts as the stylesheet says, 8 above and below
  the words and 11 either side on the 4px control radius, Public Sans 13 in Night Ops white with
  a grey placeholder, and the shade the top edge casts in drawn as a gradient. Focused, the edge
  goes to armed edge and a 2px armed outline stands 1px off it, drawn by the field itself on any
  focus, not only the keyboard's. A header is a field label - Departure Mono 10, tracked .12em
  (`src/SysMonitor.App/Styles/Console/Controls.xaml:553`, `:756`, `:942`). NumberBox's own text
  box is pointed at the same field, so a form reads as one idiom; ComboBox is not a field, since
  System-X gives `select` only the armed accent. The palette gains the field's colours for all
  three shifts, and a locked field's words keep their white on Day Shift, where the chassis'
  locked word would vanish into the well (`scripts/generate-tokens.py:197`, `:311`, `:373`). The
  pages' legacy `SearchTextBoxStyle`, `InputFieldStyle` and local face overrides are gone, and a
  test keeps them gone (`tests/SysMonitor.Tests/Architecture/ConsoleFieldTests.cs:337`); the smoke
  run builds each field in both shifts and measures its well and its focus against the palette
  (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:805`).

- **Every button is a System-X cap.** The 121 buttons that still asked for the first-generation
  styles - a 24px pill, hardcoded red or outlined, many wrapped in a hand-copied chrome bezel -
  now take the console's caps. Armed red is kept for commands that cannot simply be undone, as
  System-X reserves it: wiping, deleting, ending a task, cleaning, fixing the registry, clearing
  all data, resetting settings and deleting the scheduled task. Each tool page's one main action -
  a scan, a health check, a compress, starting a backup - is the chrome cap, and everything else is
  the plain cap. Words on caps are written in capitals, as System-X sets them. The legacy button
  styles and the bezel brushes they used are deleted, and the chrome bezel ratchet is empty.

- **A confirmation arms the button that does the thing, not Cancel.** WinUI hands a
  dialog's default button the accent style from a visual state, and that state beats any
  style the dialog sets; with the accent armed red since this release, every confirmation
  that made Cancel its default - Drive Wiper, Large Files, Duplicate Finder, and the
  Registry Cleaner and Backup questions - drew **Cancel** in armed red and the destructive
  button plain. The emphasis was backwards on exactly the dialogs where it matters most.
  Confirmations now have no default button at all, so that state never runs: the
  destructive button is the armed cap, Cancel is the plain one, Enter still arms nothing
  and Escape still cancels
  (`src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:92`, `:93`). Because no
  test could have caught this by reading the code, the smoke run now opens a confirmation
  in both shifts and asks both buttons which face they were drawn in
  (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:662`).

- **Every dialog comes from the one helper.** Nine dialogs across four pages were still
  built by hand, so each missed whatever it was not told: the application's dialog style,
  the page's theme, and now the caps. The Registry Cleaner's restore, Game Mode's
  close-these-apps question, sensor list and error, Backup's password prompt, delete and
  restore questions, and the PDF editor's watermark and signature prompts all go through
  it (`src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:52`, `:83`). A dialog that
  only says something now takes one button and arms nothing. The sensor list is read in
  Iosevka, the console's code face, in place of a hard-coded Consolas.

- **Text is set in Public Sans, the first of the Command Console's faces.** STX.1 is
  being restyled to match System-X, and the type comes first: Public Sans for text,
  Archivo at the widths System-X draws for titles, caps and lamps, Departure Mono for live
  numbers and Iosevka for code and paths. They ship as TrueType files built from System-X's
  web fonts (`scripts/build-fonts.py`, `src/SysMonitor.App/Styles/Console/Fonts.xaml`),
  each family with its copyright notice and the SIL Open Font License beside it, and are
  listed in THIRD-PARTY-NOTICES.md. Pages and controls pick up Public Sans now; the other
  faces arrive with the controls that use them.

- **The accent colour is System-X's armed red.** Check boxes, switches, radio buttons,
  sliders, progress bars, selections and accent buttons took whatever accent colour
  Windows was set to, blue unless it had been changed. They now take the armed red
  `#AA2024`, as System-X's form controls do, with its warm white on top where WinUI
  wrote black, and the scroll bars wear System-X's armed thumb
  (`src/SysMonitor.App/Styles/Console/FluentOverrides.xaml:120`, `:33`). High
  Contrast is untouched: its entries restate the system colours WinUI gives those
  controls. The smoke run checks that WinUI's own brushes pick the new colours up.

- **Drive Wiper and Large Files ask before they destroy anything.** WIPE NOW started
  overwriting the moment it was pressed, and DELETE SELECTED sent every ticked file away
  without a question. Both now ask first, naming how many items are involved and how much
  they hold, and no button is the default, so Enter arms nothing and Escape cancels
  (`src/SysMonitor.App/Views/DriveWiperPage.xaml.cs:37`,
  `src/SysMonitor.App/Views/LargeFilesPage.xaml.cs:31`). The wipe's question says none of
  it goes to the Recycle Bin, and repeats the page's SSD warning when it applies. The
  Recycle Bin questions, here and in Duplicate Finder, say what happens to a file Windows
  cannot recycle. One helper builds these dialogs, in the application's dialog style and
  the page's theme (`src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:16`).

- **The memory optimizer logs what it did.** It logged "freed N bytes" for a trim, and
  its locals were named for freeing, which is where wording like the Memory page's
  comes from; they say trimmed now.
  (`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs:65`)

- The Dashboard's TRIM MEMORY button says "Trimming memory..." while it runs and "Trim
  failed" when it fails, matching its label and the Memory page.
  (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs:295`)

### Removed

- **The first-generation button and field styles.** `RedButtonStyle`, `OutlinedButtonStyle`,
  `OutlineButtonStyle`, `ExportButtonStyle`, `ChromeBezelButtonStyle`,
  `ChromeBezelSecondaryButtonStyle`, `InputFieldStyle` and `SearchTextBoxStyle`, with the three
  chrome bezel brushes and the primary button gradient, had no consumer once the pages took the
  caps and the field, so they are deleted rather than left to be asked for again.

- **A process count nothing showed.** The Dashboard's view model set `ProcessCount` to
  the number of processor cores times ten, commented as a rough estimate, and no page
  bound it. A property holding a made-up number is one binding away from being shown as
  a measurement. The citations into that file from the tutorial and the two reference
  pages moved with the lines they cite.

### Fixed

- **The Drive Wiper shows the result of a wipe.** Every wipe wrote its outcome to the
  status line and then, in its `finally` block, described the list over it, so the
  outcome was replaced before it was drawn. "Successfully wiped", "N failed" and the
  warning that an overwrite could not be read back - "treat those files as not securely
  erased" - have never reached the screen: a wipe with an unconfirmed overwrite ended on
  "Add files or folders to securely delete", the same line as one that went perfectly.
  The list's counts still update after a wipe, and the result stays until the list
  changes. A single item is no longer "1 items".
  (`src/SysMonitor.App/ViewModels/DriveWiperViewModel.cs:308`, `:338`, `:382`)

- **The Drive Wiper's remove button removes the file.** The X on each row was bound to
  `RemoveFileCommand` through an element named `FilesToWipe` that does not exist, on a
  page that never set a `DataContext`, so the binding found nothing and the button did
  nothing. It now reaches the command the way Installed Programs and PDF Tools do, and
  its tooltip says it removes the file from the list rather than wiping it.
  (`src/SysMonitor.App/Views/DriveWiperPage.xaml:6`, `:151`,
  `src/SysMonitor.App/Views/DriveWiperPage.xaml.cs:17`)

- **Nothing on the Drive Wiper page changes while a wipe runs.** The wipe works through
  a copy of the list taken when it starts. Clear All emptied the list on screen while
  every file on it was still wiped, and adding files moved the count the progress bar
  divides by; with the X working, removing a row mid-wipe would have hidden a file that
  was still destroyed. Add Files, Add Folder, Clear All and each row's X are disabled
  until the wipe ends. So is the method selector: the wipe read it once per file, so
  changing it mid-wipe changed how the remaining files were wiped while the result
  named only the last choice. WIPE NOW also waits for an add to finish - a folder is
  sized before it joins the list, and a wipe started in that gap ran without it.
  (`src/SysMonitor.App/ViewModels/DriveWiperViewModel.cs:25`, `:196`, `:203`,
  `src/SysMonitor.App/Views/DriveWiperPage.xaml:90`)

- **A file the wipe could not finish says why.** Each row has an error line, but the
  entry behind it raised no change notification, so the line was read once, while still
  empty, and a failed file showed nothing beyond the "N failed" count. A retry now clears
  the previous reason before it starts.
  (`src/SysMonitor.App/ViewModels/DriveWiperViewModel.cs:427`, `:242`)

- **Memory sizes carry one unit.** `MBConverter` already appends `MB` or `GB`; the
  Dashboard's Cleanable Space line and the Processes page's Memory column appended
  another, and read "512 MB MB" and "1.2 GB MB".
  (`src/SysMonitor.App/Views/DashboardPage.xaml:493`,
  `src/SysMonitor.App/Views/ProcessesPage.xaml:134`)

- **A missing temperature reads as missing.** With no sensor the monitors report 0 C,
  which the Fahrenheit converter turned into 32F on the Dashboard, the Temperature page
  and the GPU page, beside a status that said N/A. It now shows `--`, as the game overlay
  already did. The exported report printed the same missing reading as "0F (N/A)" and now
  says N/A. `FormatHelper` returned "0" for a missing reading when asked for the number
  without its unit - the form a caller with its own unit label uses - and a NaN reading
  came out as "NaN"; the first is now `--`, NaN counts as no reading, and tests cover both.
  (`src/SysMonitor.App/Converters/Converters.cs:335`,
  `src/SysMonitor.App/ViewModels/DashboardViewModel.cs:439`,
  `src/SysMonitor.Core/Helpers/FormatHelper.cs:111`)

- **The game overlay labels its temperatures in the unit they are in.** Since v2.2.0 it
  has computed Fahrenheit (`src/SysMonitor.App/Views/FpsOverlayWindow.xaml.cs:187`) and
  labelled the number C, so a CPU at 60 C read "140 C", while the in-app guide says the
  overlay shows Fahrenheit. The label now reads F.
  (`src/SysMonitor.App/Views/FpsOverlayWindow.xaml:150`, `:192`)

- **The Memory page stops claiming a trim frees memory.** Its button runs the same
  working-set trim as the Dashboard's TRIM MEMORY, but the page subtracted used memory
  after from used memory before - a number every other app on the machine moves - and
  reported the difference as memory freed, or said memory was already optimized when the
  difference was not positive. That is the claim v3.0.1 removed from the Dashboard and
  Game Mode. The page now reports the optimizer's own count in the Dashboard's words,
  and its button reads TRIM MEMORY. After a failed trim the banner showed a green tick
  and "MEMORY OPTIMIZED" beside the failure; the tick is now a red cross on failure, and
  the badge appears only once a trim has worked.
  (`src/SysMonitor.App/ViewModels/MemoryViewModel.cs:175`, `:51`,
  `src/SysMonitor.App/Views/MemoryPage.xaml:113`, `:136`, `:149`)

- **The in-app guide describes Game Mode's session stats as they read.** It listed
  "memory freed" for a figure the Game Mode page labels Trimmed. The in-app claim check
  knew only the RAM wordings of this claim, which is how both this line and the Memory
  page's message got past it; it now covers "memory" as well as "RAM".
  (`src/SysMonitor.App/Views/UserGuidePage.xaml:947`,
  `tests/SysMonitor.Tests/Architecture/InAppGuideClaimTests.cs:45`)

- **The documentation says what trims memory.** The user guide and the settings
  reference said the Dashboard's button is the only thing that trims working sets. The
  Memory page's button does too, and so does Game Mode, including when auto mode switches
  it on by itself because a game started
  (`src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs:345`,
  `src/SysMonitor.Core/Services/GameMode/IGameModeService.cs:31`). The getting-started
  tutorial said Quick Clean clears browser cache and tells you how much it freed; it
  empties eight fixed Windows locations, none of them a browser's
  (`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:72-79`), and tells you how
  many files it removed. The settings reference cited the comment above the Dashboard's
  trim message instead of the line that prints it.
  (`FEATURES_AND_USER_GUIDE.md`, `docs/reference-settings-and-data.md`,
  `docs/tutorial-getting-started.md`)

- **Every CPU reading covers its reader's own interval.** Usage is the share of the time
  between two readings that the processors were busy, and the monitor kept one baseline for
  the whole application: the page on screen, the FPS overlay's 500 ms loop, the tray tooltip
  and the history recorder all read through it on their own timers, so each call cut short
  the interval the next caller measured, and two calls a few milliseconds apart measured
  nothing and reported 0. The baseline was also four fields updated with no lock. A
  `CpuSampler` now owns its baseline
  (`src/SysMonitor.Core/Services/Monitors/CpuSampler.cs:22`); the overlay, the tray and the
  history recorder each hold one (`ICpuMonitor.CreateSampler`,
  `src/SysMonitor.Core/Services/Monitors/IMonitors.cs:16`), and the monitor's own shared
  sampler is locked and hands back its last reading when asked again within 250 ms
  (`src/SysMonitor.Core/Services/Monitors/CpuMonitor.cs:56`). A sampler takes its baseline
  when it is created, so the history recorder no longer writes a 0% point at start-up.

- **Every part of the app reads the settings the Settings page saved, in both builds.** In
  the packaged (MSIX) build the page saved to `LocalSettings`, while the alert service, the
  minimize-to-tray check and Auto Game Mode read `settings.json`, which nothing in that
  build wrote: switching notifications off, changing an alert threshold or switching
  minimize-to-tray off was saved and then ignored. In the unpackaged build, Save wrote the
  whole file back from a copy the page took when it opened, erasing any custom game Auto
  Game Mode had added since. One store now serves every reader and writer in both builds
  (`src/SysMonitor.Core/Services/Settings/SettingsStore.cs:25`, registered at
  `src/SysMonitor.App/App.xaml.cs:114`). A save reads the file again and writes only its
  own changes on top, and a file it could not read is never written over. On its first
  start the packaged build copies across what an earlier version left in `LocalSettings`.
  Auto Game Mode's saves are written in the order they were made, and the last one lands
  before the app closes. Clear All Data resets every setting in both builds; it used to
  clear `LocalSettings` alone, which changed nothing unpackaged. When a save fails, the
  Settings page now says so instead of reporting success.

- **The PDF editor's sticky note opens instead of closing the app.** Clicking the page with
  the Sticky Note tool built the note's Save button from `AccentButtonStyle`, asked of the
  page's own resources, which do not look in App.xaml where the Fluent styles are merged. The
  lookup threw, and nothing caught it (`src/SysMonitor.App/Views/PdfEditorPage.xaml.cs:668`).
  A new check reads every resource the XAML and the code ask for and fails when one cannot be
  reached (`tests/SysMonitor.Tests/Architecture/ResourceKeyTests.cs`).

- **Large Files and Duplicate Finder never delete a file permanently.** They asked
  Windows to recycle without confirmation, and when Windows could not recycle a file -
  on a network or removable drive, on a drive whose Recycle Bin is turned off, or larger
  than the Recycle Bin is set to hold - it deleted the file permanently, while the page
  said it had moved it to the Recycle Bin. Large Files lists files of 100 MB and more,
  so the last case was not rare. Each of those cases now leaves the file where it is
  (`src/SysMonitor.Core/Services/Utilities/RecycleBin.cs:136`), links are still never
  followed, and a file is only reported as moved once it has been found in the Recycle
  Bin afterwards (`RecycleBin.cs:126`). The result names how many files went, how many
  were left where they are and why (`src/SysMonitor.Core/Services/Utilities/RecycleReport.cs:11`).
  Duplicate Finder also stops taking a file off its list when it was not removed, and
  stops calling what went to the Recycle Bin "freed": the space comes back when the
  Recycle Bin is emptied.

### Documentation

- **The README says what changed.** A What's new section lists the changes since v3.0.1 for
  anyone arriving at the repository, and the project tree, the components table and the
  licence notes now include the typefaces and the folders this release adds - the four
  families ship with their licences beside them
  (`src/SysMonitor.App/Assets/Fonts/OFL-PublicSans.txt:1`,
  `src/SysMonitor.App/Assets/Fonts/OFL-Archivo.txt:1`,
  `src/SysMonitor.App/Assets/Fonts/OFL-DepartureMono.txt:1`,
  `src/SysMonitor.App/Assets/Fonts/OFL-Iosevka.txt:1`), and every one of them is
  compared byte for byte with what the release ships (`.github/workflows/windows-ci.yml:191`).

- **The in-app guide describes this build.** A "Changes since v3.0.1" card sits above the
  v3.0.1 release notes; the Drive Wiper, Large Files and Duplicate Finder lines say that
  each asks first and what reaches the Recycle Bin; and the licence card names the
  typefaces (`src/SysMonitor.App/Views/UserGuidePage.xaml`). Its longer lines now wrap:
  24 of them ran off the edge of their card.

- **File Tools is described as it is.** The in-app guide said it renamed files in batches
  and converted formats, and the README said it converted files. It compresses a file or
  folder to ZIP, or a file to GZip, and extracts archives
  (`src/SysMonitor.App/ViewModels/FileToolsViewModel.cs:142`, `:203`).

- **Refresh Interval is described as it is.** The user guide and the settings reference
  said it sets how often readings update. It is saved and nothing reads it: each page
  refreshes on a fixed timer of its own
  (`src/SysMonitor.App/ViewModels/SettingsViewModel.cs:79`). The troubleshooting steps
  built on it, and on an auto-refresh switch the app does not have, are replaced by steps
  that work, and the advice to install the .NET runtime is gone: the released builds carry
  it.

- **Every document is plain ASCII.** Every document in the repository, the contributor
  notes included, and every word of the in-app guide is written without em dashes, emoji
  or other decoration, which read as mojibake wherever a file is opened as anything but
  UTF-8. A test finds the documents by walking the repository, so a new one is covered
  from the day it appears
  (`tests/SysMonitor.Tests/Architecture/PublishedDocumentEncodingTests.cs`).

---

## [3.0.1] - 2026-09-21

A wording release. Every change here is a sentence the application or its documentation
showed the user that the code did not support. No feature was added or removed, which is
what keeps it a patch: 3.0.0 corrected the behaviour behind the RAM claims and left the
labels on top of them, and this release finishes that.

### Changed

- **The memory button says what it does.** The Dashboard's middle button read `BOOST RAM`
  while the operation trims process working sets, which moves pages to the standby list
  where Windows can page them straight back. The result message has read "Trimmed N from
  background apps" since 3.0.0, and the optimizer's own wording was corrected then; the
  button label was the last part of that flow still claiming otherwise. It now reads
  `TRIM MEMORY`, with a tooltip saying what happens to the pages.
  (`src/SysMonitor.App/Views/DashboardPage.xaml:263`)

- **Game Mode stops claiming it frees RAM.** Game Mode's memory step calls the same
  `IMemoryOptimizer.OptimizeMemoryAsync` the Dashboard button does
  (`src/SysMonitor.Core/Services/GameMode/GameModeService.cs:111`), so it trims working
  sets and frees nothing - but the page said `Frees Up RAM`, subtitled it "Optimizes
  memory for gaming", and labelled the result `RAM Freed`
  (`src/SysMonitor.App/Views/GameModePage.xaml:451`, `:452`, `:487`). Three more copies
  of the claim the Dashboard button had just been corrected for. They now read
  `Trims Memory` and `Trimmed`. The field behind the label was called `MemoryFreedBytes`,
  which is where each of those labels came from; it is now `MemoryTrimmedBytes`
  (`src/SysMonitor.Core/Services/GameMode/IGameModeService.cs:60`).

- **The in-app guide describes Quick Clean rather than praising it.** "One-click cleanup
  to free space and boost performance instantly" promised a speed-up nothing in the
  application measures. Quick Clean deletes temporary files from eight fixed Windows
  locations (`src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:72-79`) and reports
  a file count, so that is what the guide now says. The card subtitle "Free space and
  boost performance" headed six tools of which a registry cleaner and a startup manager
  free no space; it now names what the tools under it do.
  (`src/SysMonitor.App/Views/UserGuidePage.xaml:84`, `:183`)

- **The Game Mode target list says what happens to the apps on it.** It was captioned
  "These apps will be closed when Game Mode is enabled", above twenty-one named
  applications including every browser. Neither path closes them: the default action is
  `LowerPriority` (`src/SysMonitor.Core/Services/GameMode/IGameModeService.cs:26`), which
  moves them down the processor queue, and ticking "Ask them to close instead" asks and
  accepts a refusal. The caption now says which of those happens and that an app which
  declines keeps running. (`src/SysMonitor.App/Views/GameModePage.xaml:537`)

- **The markdown guide's Quick Actions table matches the buttons.** It listed "Quick
  Clean - Instantly removes temporary files and browser cache", and Quick Clean does not
  touch browser cache: that is Browser Privacy, a separate tool behind a separate page.
  It listed "Optimize Memory - Frees up RAM by clearing unused memory" for a button that
  now reads TRIM MEMORY and never freed RAM. All three rows now carry the button's own
  label. (`FEATURES_AND_USER_GUIDE.md:93-95`)

### Fixed

- **The markdown user guide describes the app that shipped.** Five sections of
  `FEATURES_AND_USER_GUIDE.md` were missed by the 3.0.0 documentation pass and still
  described 2.x behaviour: the Secure File Wiper as putting files "beyond recovery",
  with no mention of the read-back check or the solid-state limit; a Duplicate Finder
  "name and size (fast)" matching mode that does not exist, since duplicates are decided
  by SHA-256 of the whole file (`src/SysMonitor.Core/Services/Utilities/DuplicateFinder.cs:222-224`);
  registry "Undo capability via backup restore" rather than the `reg.exe` export that
  actually happens; a Driver Updater "Outdated" status glossed as "Newer version may
  exist", when the app only compares the driver's date against two years and never
  consults any catalogue (`src/SysMonitor.Core/Services/Utilities/DriverUpdater.cs:99-100`,
  `:277`); and "One-click RAM cleanup". Nothing reads the markdown file at runtime, so no
  shipped build showed these five. The in-app guide was correct on all five
  (`src/SysMonitor.App/Views/UserGuidePage.xaml:216`, `:855`, `:863`, `:867`) but wrong on
  three others, which are the three Changed entries above; this entry said the in-app
  guide "was already correct" without qualification, and that was not true of the file as
  a whole.

- **Every `path:line` citation in `docs/` resolves.** Thirteen of them were written
  relative to `src/SysMonitor.App` or `src/SysMonitor.Core` rather than to the repository
  root, so a citation of `ViewModels/DashboardViewModel.cs` with a line number after it
  named no file a reader could open. They were checkable by hand and by
  `DocumentationAnchorTests`, and failing for both. All are now repository-relative.

- **The documentation index is served by the documentation site.** `docs/README.md` was
  not in the generator's page list, so no HTML was produced for it, and the footer link
  called "All documentation" sent the reader to the GitHub source view of a markdown file
  instead. It is now published at `documentation.html`, is in the sitemap and `llms.txt`,
  and the footer points at it. (`docs/build-docs.py:40-46`, `docs/index.html:560`)

- **The published documents are plain ASCII.** `README.md` carried box-drawing characters
  in its project tree and `FEATURES_AND_USER_GUIDE.md` carried degree signs in its
  temperature tables; both, and `CHANGELOG.md` and `THIRD-PARTY-NOTICES.md`, carried em
  dashes. Read as anything but UTF-8 - a console under the OEM code page, an editor
  guessing Windows-1252 - each of those becomes two or three stray letters, and the
  project tree stops lining up as a tree. `PublishedDocumentEncodingTests` keeps them out.

### Added

- **A documentation site.** Ten documents under `docs/`, split tutorial / how-to /
  reference / explanation, served from GitHub Pages at
  https://git-rocky-stack.github.io/sysmonitor-windows/.

---

## [3.0.0] - 2026-09-21

A correctness release. Every item below is a behaviour that did not match what the
app told the user it was doing. Three features were removed rather than documented,
which is what makes this a major version.

### Security

- **Elevated operations no longer take instructions from a file any process can rewrite.**
  Registry cleaning wrote the selected fixes to `%LocalAppData%\SysMonitor\Temp` and
  relaunched elevated against that path; a second process could replace the file while the
  UAC prompt was open, and the user's own "Yes" then carried it out as administrator against
  any key in any hive. Store-app uninstall had the same shape via a generated `.ps1`.
  (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs`,
  `src/SysMonitor.Core/Services/Utilities/InstalledProgramsService.cs`)
- **Signing material removed from the repository.** A signing PFX and its hard-coded password
  were published here. Signing now selects a certificate by thumbprint from the Windows
  certificate store and refuses the two retired thumbprints.
  (`signing/SigningCommon.ps1`, `Build-Release.ps1`)
- **PDF redaction removes what is under the box.** "Redact" drew a black rectangle and
  imported the page content unchanged, so the text beneath stayed in the file and could be
  selected, copied or extracted with any tool. A redacted page is now rasterised at 200 dpi
  with the boxes painted into the pixels.
  (`src/SysMonitor.Core/Services/Utilities/PdfEditor.cs`)
- **Drive Wiper never follows links and stays inside the selection.** Directory wipes
  enumerated with `AllDirectories`, which follows junctions and symlinks, so files outside
  the chosen folder were overwritten and deleted. A file symbolic link also caused the
  link's target to be truncated while the wipe reported success.
  (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs`)
- **Restoring a backup decides where files go, not the archive.** Restore used the path
  recorded inside the backup, so an edited backup could write chosen content anywhere the
  user could write, including the Startup folder. Destinations are now planned up front and
  confined to the folders the backup was taken from, and refused entries are reported rather
  than silently skipped. (`src/SysMonitor.Core/Services/Backup/BackupService.cs`)
- **Encrypted backups are authenticated and restorable.** Every backup made with "Encrypt
  Backup" was previously unrestorable, because no decryption code existed. The format is now
  PBKDF2 (600,000 iterations), AES-256-CBC, encrypt-then-MAC with HMAC-SHA256 verified before
  any plaintext is written. The legacy format is still readable.
  (`src/SysMonitor.Core/Services/Backup/BackupEncryption.cs`)
- **No vulnerable packages remain in the dependency tree.** Four High-severity advisories
  arrived transitively through EntityFrameworkCore.Sqlite and DocumentFormat.OpenXml; both
  were raised and the affected transitive packages with them.
  (`src/SysMonitor.Core/SysMonitor.Core.csproj`)

### Removed

- **RAM Cache.** It was a folder on disk plus a `TEMP` variable set inside this one process
  that no other program could see, and its own code switched itself off on the next start.
  Doing it for real needs a RAM-disk driver. The card, toggle, usage bar and Clear Cache
  button are gone from Game Mode.
- **7z compression.** The code behind it wrote a ZIP and changed the extension, so a user
  asking for 7z received a ZIP named `.7z`.
- **"Auto-Optimize Memory" setting.** Nothing read it. The related threshold is now described
  as what it actually drives: a memory alert.
- **Performance page frame-rate card.** It displayed a hard-coded 60 that nothing set, beside
  memory and GC figures that are measured.
- **Dead code with no callers:** `ExtractTextAsync` (returned annotation text under a name
  promising page text), `LazyServiceWrapper<T>`, `MemoryOptimizer.ClearStandbyListAsync`,
  `BytesToImageConverter`, and `UiThreadUtilization`.

### Fixed

- **Uninstalling actually uninstalls.** An `UninstallString` beginning `MsiExec.exe` was cut
  exactly seven characters in, handing msiexec `.exe /X{...}` - it showed its usage dialog and
  removed nothing, and after 60 seconds the app read an exit code from a process still running.
  The command is now parsed into program and arguments, the wait is five minutes, and exit
  codes are reported in words, including 3010/1641 (restart needed) and 1605/1614 (no longer
  installed), which are not failures.
  (`src/SysMonitor.Core/Services/Utilities/InstalledProgramsService.cs`)
- **Startup items turn off the way Windows turns them off.** "Enable" logged a line and changed
  nothing, every item was listed as enabled regardless of its real state, and "Disable" moved
  the value into a private key nothing read again - so disabling from inside the app was one-way
  and Task Manager knew nothing about it. Entries now use `Explorer\StartupApproved`, the same
  mechanism Task Manager writes, so all three views agree and every change is reversible.
  (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs`)
- **A stale set-aside startup entry no longer overwrites the live one.** Enabling an item
  restored whatever copy an older version of this app had set aside, even when Windows already
  had a newer entry under that name, pointing Windows at a version no longer installed.
  (`src/SysMonitor.Core/Services/Optimizers/StartupOptimizer.cs`)
- **Scheduled cleaning cleans.** The scheduled task ran the app with `--scheduled-clean` and
  switches nothing in the app read, so at every scheduled time Windows opened the full UI as
  administrator and cleaned nothing. The run is now headless, writes its outcome to the log,
  notifies unless the schedule asked for silence, and returns an exit code so Task Scheduler
  history shows a failed run as failed.
  (`src/SysMonitor.Core/Services/Utilities/ScheduledCleaningRun.cs`, `src/SysMonitor.App/App.xaml.cs`)
- **Network speed is read from the adapter carrying the traffic.** The monitor picked whichever
  adapter claimed the fastest link, which on any machine with WSL, Hyper-V or Docker is a
  virtual switch claiming 10 Gb/s and carrying nothing - so the dashboard showed 0 B/s with the
  wrong adapter name beside it, and history recorded those zeros. Selection now follows the
  routed interface, then a real default gateway, then link speed. The counters read are the
  interface's own, so a machine on IPv6 no longer looks idle.
  (`src/SysMonitor.Core/Services/Monitors/NetworkMonitor.cs`)
- **Duplicate Finder no longer deletes files that are not duplicates.** Files over 10 MB were
  judged by their first megabyte, last megabyte and length, which matches for every file a
  program writes with the same header, footer and size. It also followed junctions, so one file
  reached by two paths was reported as a pair and deleting "the duplicate" deleted the only copy.
  Duplicates are now decided by SHA-256 of the whole file, one physical file is counted once, the
  oldest copy is kept, and deletion goes to the Recycle Bin after a confirmation naming the count
  and size. (`src/SysMonitor.Core/Services/Utilities/DuplicateFinder.cs`,
  `src/SysMonitor.Core/Services/Utilities/FileScanning.cs`)
- **Large File Finder deletion also goes to the Recycle Bin** and no longer follows links.
  (`src/SysMonitor.Core/Services/Utilities/LargeFileFinder.cs`)
- **Drive Wiper writes the patterns it names.** The "Gutmann 35-pass" option filled its middle
  passes with `(pass * 17) % 256`, which is not that method, and no pass was ever read back.
  Passes now follow the 1996 paper's table, every wipe reads its last pass back and compares,
  and a file that cannot be confirmed is reported as unconfirmed rather than counted as wiped.
  When the selection sits on a solid-state drive the page says that overwriting cannot promise
  the flash holding the old contents was written over.
  (`src/SysMonitor.Core/Services/Utilities/DriveWiper.cs`)
- **The PDF editor claims only what it does.** Search looked only at annotations while being
  called `SearchText`, and its button toggled a panel no XAML bound. "Export to Word" wrote an
  outline containing none of the document's text. Compression offered image recompression and
  font subsetting it does not perform, and dropped title and author whether or not asked. Undo,
  redo and page navigation cleared the canvas and never redrew annotations, so they were still
  in the document, still saved, and invisible.
  (`src/SysMonitor.Core/Services/Utilities/PdfEditor.cs`)
- **PDF annotations land where they are drawn.** Canvas pixel coordinates were handed to
  PDFsharp as points, so at 100% zoom every annotation saved 1.33x away from where it was drawn,
  and further still on pages the file rotates or crops - a redaction box did not cover what the
  user covered. Page identity was also confused between source page number and document
  position, so annotations on a document whose pages had been moved or deleted were dropped
  without a word. (`src/SysMonitor.Core/Services/Utilities/PdfEditor.cs`)
- **Text-to-PDF conversion works at all.** PDFsharp 6 ships almost no fonts and does not consult
  the system, so drawing in Consolas or Segoe UI threw on every machine, including machines with
  those fonts installed. A font resolver now reads the fonts Windows lists under HKLM and HKCU;
  a family that genuinely is not installed falls back to Arial rather than losing the document.
  (`src/SysMonitor.Core/Services/Utilities/WindowsFontResolver.cs`)
- **Registry cleaning takes a real backup, and restore works.** The backup wrote a `.reg` file
  containing only comment lines - no keys, no values - for three fixed HKCU keys, while the
  cleaner deletes HKCU and HKLM values and whole subkey trees. There was no restore code at all.
  Every key a selected fix will modify is now exported with `reg.exe` before anything is cleaned,
  and a failed export stops the clean.
  (`src/SysMonitor.Core/Services/Cleaners/RegistryCleaner.cs`)
- **Backup verification checks the backup.** It hashed the live source files instead, so a
  corrupted backup verified and a healthy one failed once the originals were edited. The "Verify
  backup after completion" option recorded hashes and checked nothing, and the Verify command had
  no button in the UI. (`src/SysMonitor.Core/Services/Backup/BackupService.cs`)
- **Uncompressed backups no longer fail after copying every file.** With compression set to None
  the backup is a folder, and reading its size as a file threw, so the job ended in failure with
  no catalog entry after all the work was done.
  (`src/SysMonitor.Core/Services/Backup/BackupService.cs`)
- **The Game Mode page releases its view model.** It subscribed four handlers to app-lifetime
  services and never unsubscribed, and the page never disposed it, so every visit to Game Mode
  leaked a view model, a page and its bindings. Its state was also loaded by an `async void`
  called from the constructor, where a failure surfaced on a thread with nobody waiting on it and
  ended the process. (`src/SysMonitor.App/Views/GameModePage.xaml.cs`,
  `src/SysMonitor.App/ViewModels/GameModeViewModel.cs`)
- **The overlay reads frame rate where it actually is.** It searched Load sensors by name;
  LibreHardwareMonitor exposes exactly one frame-rate sensor and it is a Factor sensor, so the
  readout could not produce a number on any machine, including those able to measure it. A
  reading, an idle sensor and no sensor at all are now three distinct answers, shown as the
  number, `---`, and `NO FPS SENSOR`.
  (`src/SysMonitor.Core/Services/Monitors/TemperatureMonitor.cs`)
- **Three numbers that said the wrong thing.** "Freed 524288000 MB of memory" printed bytes with
  MB after them. Battery health was computed from charge percentage, so a battery at 15% read as
  "Critical"; health is now design capacity against full-charge capacity, and "Not reported" when
  Windows does not supply them. Browser privacy scans reported findings "across 0 browsers"
  because the count came from a collection nothing filled.
  (`src/SysMonitor.App/ViewModels/DashboardViewModel.cs`,
  `src/SysMonitor.Core/Services/Monitors/BatteryMonitor.cs`,
  `src/SysMonitor.Core/Services/Cleaners/BrowserPrivacyCleaner.cs`)
- **A caught failure now leaves a trace.** 210 empty catch clauses and 13 services with no logger
  meant a cleaner, an uninstall, a backup or an alert could fail leaving nothing in
  `%LocalAppData%\SysMonitor\Logs` and nothing on screen. Twenty-one classes gained a logger; six
  catches remain deliberately empty and each carries a line saying why.
- **A machine with no battery is not a battery at 0%.** The alert service dereferenced a null
  battery reading, so every battery alert check threw and was swallowed by a bare catch.
  (`src/SysMonitor.Core/Services/Alerts/AlertService.cs`)

### Changed

- **Game Mode moves background apps out of the way instead of killing them.** Enabling it asked
  twenty-one apps - browsers, Teams, Discord, Slack, Zoom, OneDrive, Dropbox - to close and killed
  whatever had not gone one second later; anything without a window was killed outright, and
  unsaved work went with it. Background apps are now lowered below the game in the processor queue
  and put back exactly where they were when Game Mode ends, when the app closes, or on the next
  start after a crash. Asking apps to close is a separate opt-in checkbox that only ever asks,
  with a ten-second wait, and auto mode never closes anything.
  (`src/SysMonitor.Core/Services/GameMode/GameModeService.cs`,
  `src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs`)
- **The power plan is always put back** - when Game Mode ends, when the app closes with it on, and
  on the next start after a crash. The plan being replaced is written down before it is changed.
  (`src/SysMonitor.Core/Services/GameMode/GameModeService.cs`)
- **The memory optimizer describes what it does.** Trimming a working set moves pages to the
  standby list, from which Windows can page them straight back; it does not free RAM, and the
  wording no longer says it does. (`src/SysMonitor.Core/Services/Optimizers/MemoryOptimizer.cs`)
- **Crash reports are written to the log folder** rather than the user's Desktop, which makes the
  privacy policy's "delete `%LocalAppData%\SysMonitor`" statement true. (`src/SysMonitor.App/App.xaml.cs`)
- **The repository no longer tracks build output.** 560 MB of MSIX packages, publish DLLs, `.vs/`
  state and local settings had been force-added past `.gitignore`. Tracked files went from 954 to
  381 and tracked binaries to zero. This stops them accumulating from here on; the existing history
  is unchanged. (`.gitignore`)
- **The installer reads its version from the executable it packages.** It was written by hand and
  said 1.0.0 for a 2.2.2 build, so Add/Remove Programs reported a release that never existed.
  (`installer/SysMonitorSetup.iss`)

### Licensing

- **The app says it is MIT, everywhere, for the first time.** The MIT text at `LICENSE`
  is what the installer has presented since 2025-12-15 (`installer/SysMonitorSetup.iss:39`),
  but six other places told the user the opposite - the Settings page read
  "All rights reserved", and `installer/LICENSE.rtf` was a proprietary end-user agreement
  forbidding copying, modification, distribution and reverse engineering.
- **`installer/LICENSE.rtf` deleted.** It was never wired into the build - the installer
  reads `..\LICENSE` - so it contradicted the actual terms while being documented as the
  agreement shown during installation. `installer/INSTALLER_README.txt` now points at the
  real file.
- **A License card was added to the in-app User's Guide**
  (`src/SysMonitor.App/Views/UserGuidePage.xaml`), stating what the MIT grant permits and
  requires, and listing the third-party licences bundled with the app. The Settings page
  says the same (`src/SysMonitor.App/Views/SettingsPage.xaml:363`).
- **`THIRD-PARTY-NOTICES.md` added.** The build is self-contained, so it redistributes its
  dependencies, and none were attributed. LibreHardwareMonitor is MPL-2.0 and carries a
  source-availability obligation that was not being met; Serilog is Apache-2.0.
- **The licence now installs with the application.** `Build-Release.ps1` stages only the
  publish output, and the installer copied only that folder, so `LICENSE` never reached
  the installed app - while MIT requires the copyright and permission notice to travel
  with every copy. `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` are now installed beside
  the executable (`installer/SysMonitorSetup.iss`).
- Copyright lines read 2024-2026 rather than 2024 or 2024-2025.

### Documentation

- The keyboard-shortcut table has been removed from the user guide. None of the six shortcuts it
  listed were implemented - the app has no `KeyboardAccelerator` anywhere, and the only key
  handling is Delete, Escape and Enter inside the PDF editor.
- Compression formats are described as ZIP and GZip (`.gz`, single file). The previous "TAR.GZ"
  claim had no tar step behind it.
- The minimum OS is stated as Windows 10 version 2004 (build 19041) throughout, matching
  `TargetPlatformMinVersion`. Documents previously said 1903 in three places and "build 22621+"
  in two, the latter being the maximum version tested rather than the floor.
- The shipped build is self-contained; the feature guide previously also required the user to
  install the .NET 8 Desktop Runtime, which contradicted the installer text.
- Support and repository links point at `github.com/Git-Rocky-Stack/sysmonitor-windows`. Three
  different incorrect URLs were in circulation.
- Version strings across the installer text, the feature guide and the in-app release notes now
  track the shipped version.

---

## [2.2.2] - 2026-01-03

### Changed
- Publisher updated to Rocky Stack.
- Privacy policy added.

## [2.2.0]

### Added
- Advanced Game Mode.
- Auto Game Mode - detects a running game and enables optimisation
  (`src/SysMonitor.Core/Services/GameMode/AutoGameModeService.cs:237`).
- Performance Profiles - save and switch between optimisation presets.
- Game overlay showing live temperatures, load and power, repositionable by dragging.
- Fan speed and power draw monitoring widgets.
- Hardware sensor diagnostic viewer.
- Administrator rights requested at launch for full hardware sensor access.

### Changed
- Temperatures display in Fahrenheit.

## [2.1.1]

### Added
- Game Mode - one-click gaming optimisation.
- Session statistics covering background apps moved aside, apps that declined to close, and
  memory trimmed.

## [2.1.0]

### Added
- System tray mode with a live CPU/RAM tooltip and a right-click menu.
- Real-time alerts with toast notifications.
- History page with interactive LiveCharts2 graphs.
- 30-day historical data storage in SQLite.

## [2.0.1]

### Added
- Performance Monitor page with operation metrics and timing.
- System overview cards, P95/P99 percentiles, and CSV export.
- Window icon and global crash logging.

## [1.0.0]

### Added
- System monitoring dashboard with real-time statistics.
- CPU, GPU, memory, disk, network, battery and temperature monitoring.
- Directory cleaner, registry cleaner and browser privacy cleaner.
- Startup manager, large file finder and duplicate file detector.
- PDF tools, image tools, WiFi analyzer, Bluetooth scanner and network mapper.
- Secure drive wiper, health check, backup manager and scheduled cleaning.

[3.0.0]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/compare/v2.2.2...v3.0.0
[2.2.2]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/compare/v2.2.0...v2.2.2
[2.2.0]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/compare/v2.1.1...v2.2.0
[2.1.1]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/compare/v2.1.0...v2.1.1
[2.1.0]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/compare/v2.0.1...v2.1.0
[2.0.1]: https://github.com/Git-Rocky-Stack/sysmonitor-windows/releases/tag/v2.0.1
