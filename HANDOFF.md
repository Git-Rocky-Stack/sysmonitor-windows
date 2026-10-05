# Handoff - fields, buttons, page colours, headers, spinners, code colours and instruments are on the console

**Date:** 2026-10-05
**Branch:** `claude/peaceful-mendel-xkext6`, draft PR against `main`
**Previous handoff:** the 2026-09-29 one (2F-2 closed, buttons waiting on a decision) is in git history at `3a659e0`.

---

## 0. Read this first

1. **This session ran on Linux, so nothing WinUI was built or run here.** The Architecture suite (175 tests),
   the token generator's `--check` and a Roslyn syntax parse of every changed C# file ran locally and pass.
   The build, the full suite and the UI smoke run in both shifts are Windows CI's to answer
   (`.github/workflows/windows-ci.yml`). Treat a red CI run on this branch as the first thing to read.
2. **The direction changed mid-session.** The 2026-09-29 handoff left the 41 red buttons as ~10 product calls
   for Rocky. He answered: buttons and everything else should match the System-X app. So the calls were made
   by System-X's own rule (section 2.2), and the table is there to be overridden button by button.
3. **The first CI run crashed the smoke run on the first number box**, and why matters beyond the fix: WinUI
   declares a few resources with `x:Name`, which nothing outside its dictionary can find, and the vendored key
   list counted them as provided. `scripts/list-winui-keys.py` now leaves them out, so ResourceKeyTests fails on
   that class of mistake (section 2.4).
4. **"Everything else" is bigger than one session**, and section 3 lists what is left, counted.

---

## 1. State

```
$ python scripts/generate-tokens.py --check
Tokens.xaml matches its generator.

$ dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 \
      --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"
Passed!  - Failed: 0, Passed: 175
```

The rest of the suite needs Windows (COM, `ping.exe`) and crashes the test host on Linux; CI runs it.

---

## 2. What was done

### 2.1 Phase 2F-3: the field (`.field`, styles.css :2286)

- **TextBox, PasswordBox and NumberBox are recessed wells**, dark in both shifts
  (`src/SysMonitor.App/Styles/Console/Controls.xaml:553`, `:756`, `:942`). WinUI's templates are kept part for
  part; the colour states are the console's. Focused: armed edge and a 2px armed outline 1px off it, drawn by
  the template on any focus. Header: the field label's values, pinned to `FieldLabelTextStyle` by a test.
- **NumberBox** is re-templated only to point `InputBox` at `ConsoleFieldTextBoxStyle`; the field's template
  carries NumberBox's spin-button states, so one template serves both. WinUI's theme dictionaries that
  re-point the spinners were dropped (a theme dictionary belongs in a dictionary App.xaml merges itself), so
  the inline spinners are WinUI's repeat buttons in the dark theme.
- **ComboBox is deliberately not a field**: System-X gives `select` no `.field` rule, only the armed accent.
- **Palette**: `FieldEdge`, `FieldLip`, `FieldForeground`, `FieldPlaceholder`, `FieldFaceBrush`,
  `FieldShadeTopBrush`, and `ConsoleDisabledFieldForegroundBrush` - a locked field's words. The shared locked
  word is `#2E2E2C` on Day Shift and would vanish into the well, so the field keeps its white there and is
  GrayText in High Contrast (`scripts/generate-tokens.py:197`, `:311`, `:373`).
- **Pages**: `SearchTextBoxStyle` (3), `Style="{StaticResource DefaultTextBoxStyle}"` (2) and ProcessesPage's
  local brushes are gone; `InputFieldStyle` and `SearchTextBoxStyle` are deleted.
- **Tests**: `tests/SysMonitor.Tests/Architecture/ConsoleFieldTests.cs` (parts, states, metrics, faces, focus,
  label, the dark parts, NumberBox's single dimming, no page restyling a field). The locked rule admits the
  field's twin only on `ContentElement.Foreground`; the presenter rule's exemption gains the three fields;
  the cap rule reads only the dictionary's own styles, not the clear button inside the field.
- **Smoke run**: `UiSmokeRun.CheckFieldsAsync` (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:805`) builds the
  three in each shift, measures each well against the palette, focuses the text box and measures the armed
  edge and outline. A refused focus is a note, not a failure. **Not yet run** - CI is its first run.

### 2.2 The button migration

All 121 buttons on 28 pages that used `OutlinedButtonStyle`, `OutlineButtonStyle`, `RedButtonStyle` or
`AccentButtonStyle`, and every hand-copied chrome bezel (55 on 24 pages), now take the caps. Local faces,
radii, padding and heights that fought the cap were stripped; literal words were put in capitals. The legacy
button styles, the bezel brushes and the primary button gradient are deleted, and the bezel ratchet is empty.

**Armed** (cannot simply be undone): WIPE NOW, both DELETE SELECTED, END TASK, Startup DELETE, both CLEAN
SELECTED, QUICK CLEAN, FIX SELECTED, Scheduled Cleaning DELETE TASK, Settings CLEAR (all data) and RESET TO
DEFAULTS.

**Chrome** (the page's one call to action): SCAN on Bluetooth, Driver Updater, Wi-Fi and Network Mapper; the
scans on Duplicate Finder and Large Files; RUN HEALTH CHECK; TRIM MEMORY on the Memory page; COMPRESS on File
Tools and Image Tools; DONATE WITH PAYPAL; START BACKUP; the Performance monitoring toggle; the Game Mode
toggle; the PDF editor's empty-state OPEN PDF.

**Plain**: everything else, including Next/Back/Done, REFRESH, SAVE SETTINGS, SCAN PORTS, the PDF operations.

**Backup's tiles** - the three quick-action tiles, four backup types and the drive picker - are card-like
buttons. They keep the cap face with their own padding, icon sizes, and their descriptions as Public Sans
prose in silver mute. They are really pressable plates (`.plate-hit`, :1231), which `Plate` already draws but
is not a Button; moving them onto it means re-wiring their Click handlers.

### 2.3 Cleanups found on the way

Stale comments in `Controls.xaml` (the cap's disabled note still said Graphite; the switch said it used the
reposition animation; two comments named a `ConsoleSwitch` class that does not exist), a stale duplicate
summary and broken indentation in `UiSmokeRun.cs`, a CS0162 warning in `ConsolePinAnchorTests.cs`, and 22
CHANGELOG anchors re-pointed through a line diff after the pages moved.

### 2.4 The pages: styles, colours, headers, spinners

- **Styles.xaml is a bridge.** Every name a page still asks for maps onto the console: `PageHeaderStyle` the
  view title, `SectionHeaderStyle` a placard beside the plate's armed light pipe,
  labels and values the kicker and telemetry faces, progress bars a well with an LED. Twenty unused styles and
  all of `Colors.xaml` are deleted.
- **1,016 legacy brush references** moved to `{ThemeResource}` tokens by role (words: Platinum, Silver, Silver
  Mute, ArmedLit, the State colours; surfaces: the carbon ramp; status fills: the rails).
- **All 35 pages open on `ViewHeader`**, numbered by the rail: Dashboard `MOD - DASH - 01` to User's Guide
  `34`, PDF Editor `25B`. The per-page logo is gone; System-X's header has none. Scan/stop caps that lived in a
  banner are its Actions.
- **Hardcoded colours** are tokens by role; words on a status fill are `ArmedFgBrush` (HighlightText on the
  Highlight the rails become in High Contrast). The colour ratchet is down from 39 files to 3, each kept as data:
  PDF Editor's ink swatches, PayPal's blue, and the FPS overlay.
- **No ProgressRing remains.** Loading and scanning regions are `BusyWell`s carrying their status line; rings
  beside an action are a small armed `EXEC` lamp. The ratchet is empty.
- **MainWindow**: the title bar stays black on Void and asks for the dark theme; the rail's three backgrounds
  moved into FluentOverrides, written per shift.
- **The resource check now knows `x:Name` from `x:Key`** (`scripts/list-winui-keys.py:75`); nine names left the
  list, and NumberBox restates WinUI's two spin button styles where its buttons can reach them.

### 2.5 Code colours are lamp states

Services and view models carry a `LampState` beside each status word instead of a Material hex string;
pages colour by state with `StateBrush` (`src/SysMonitor.App/Controls/Instruments/StateBrush.cs:30`), which reads
the palette through `ConsolePalette` for the element's own shift, High Contrast included. Words take the State
colours, fills the rails, washes the soft tints. Category colours are removed; Game Mode on is armed. The four
hex/bool brush converters are replaced by one `BoolToLampStateConverter`. Guarded by `ConsoleStateBrushTests` and
the smoke run's `CheckStateBrushAsync`. Mappings that follow the old colour over the word, worth a look: driver
code 45 "Disconnected" is Off and 51 "Unknown Problem" NoGo; a disk at 50-75% reads "Normal" on a Hold lamp; an
unplugged battery is NoGo.

### 2.6 Instruments

- **Cards are `Plate`s.** All 199 `CardStyle`/`StatCardStyle`/`PremiumStatCardStyle`/`HeroStatCardStyle` Borders
  on 34 pages are `instruments:Plate`, which draws the lip, the shade and the `0 1px 2px` shadow a Border could
  not. Each keeps the old style's padding (16, 20 premium, 24 hero) and stat minimums, and stretches its content
  vertically as the Border did; Background, BorderBrush, BorderThickness and CornerRadius overrides were dropped,
  since the plate draws its own. The four styles are deleted. Plates carry no `State` yet: a card whose whole
  subject has a condition could light its rail.
- **Dots are `LedDot`s** (18), **badges are `Chip`s** (9, words through `UpperCaseConverter`, or
  `BoolToWordConverter` for a yes or no), and **result toasts are `Banner`s** (Backup, PDF Editor, PDF Tools,
  Network Mapper).
- **Performance** said "Status: Pause" in green while monitoring. It is now a MONITORING/PAUSED chip (Go/Hold) beside
  a PAUSE/RESUME cap; `BoolToMonitoringTextConverter` is a `BoolToWordConverter` instance.

---

## 3. What is left to match System-X, counted

| Surface | Left | Where it is tracked |
|---|---|---|
| Colour strings in code | 2 files, both PDF annotation colours (document data, kept on purpose) | `ConsoleRecipeTests.CodeColourLiterals` |
| Font names in XAML | 7 files | `ConsoleRecipeTests.FontFamilyLiterals` |
| Major sections as `Faceplate`s (bolts, stripe, deep shadow) rather than `Plate`s; plates lit by `State` | every page that groups plates under a heading | not tracked |
| Card-like and icon buttons with local overrides | Backup tiles and history icons, Driver Updater quick actions, Installed Programs row icons, Registry Cleaner's folder icon | not tracked |
| CheckBox, RadioButton, Slider, ComboBox | System-X gives them only the armed accent, which FluentOverrides applies: nothing more to transcribe | - |
| Excellent and Good thresholds share one green | Performance and User's Guide legends | not tracked |
| `saturate(.5)` on locked controls; NumberBox spinners as caps | not drawn | Controls.xaml comments |

Cards, dots, badges and toasts are instruments (section 2.6). The next step with the most reach is the
faceplates: System-X groups a view's plates under faceplates with bolts and a stripe, where the pages still set a
section header over loose plates. After that, the seven files that still name a font family, and the card-like
buttons on Backup and Driver Updater.

---

## 4. Re-running

```
dotnet build SysMonitor.sln -c Debug -p:Platform=x64
dotnet test
python scripts/generate-tokens.py --check
dotnet run --project src/SysMonitor.App -- --ui-smoke <folder>
```

On Linux, the Architecture suite alone:
`dotnet test tests/SysMonitor.Tests -p:EnableWindowsTargeting=true -p:Platform=x64 --filter "FullyQualifiedName~SysMonitor.Tests.Architecture"`
