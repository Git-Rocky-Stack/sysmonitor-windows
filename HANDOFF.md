# Handoff - Phase 2F-3 (fields) and the button migration are done; the rest of "match System-X" is mapped

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
3. **"Everything else" is bigger than one session**, and section 3 lists what is left, counted.

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

---

## 3. What is left to match System-X, counted

| Surface | Left | Where it is tracked |
|---|---|---|
| CheckBox 26, RadioButton 14, Slider 12, ComboBox 14 | recoloured by FluentOverrides, not re-templated | 2F-4 |
| Instruments (`Lamp`, `Plate`, `Faceplate`, `Display`, `ViewHeader`, `BusyPanel`...) | used by no page | the 2026-09-29 handoff, 4.2 |
| Colour literals in XAML | 39 files | `ConsoleRecipeTests.XamlColourLiterals` |
| ProgressRings | 18 pages | `ConsoleRecipeTests.ProgressRings` |
| Font names in XAML | 7 files | `ConsoleRecipeTests.FontFamilyLiterals` |
| Colour strings in code | 32 files | `ConsoleRecipeTests.CodeColourLiterals` |
| Card-like and icon buttons with local overrides | Backup tiles and history icons, Driver Updater quick actions, Installed Programs row icons, Registry Cleaner's folder icon | not tracked |
| Words from converters in mixed case | Performance's Pause/Resume (`Converters.cs:522`, also feeds a status line) | not tracked |
| `saturate(.5)` on locked controls; NumberBox spinners as caps | not drawn | Controls.xaml comments |

The biggest visible step after this is the pages themselves: cards, headers and status colours are still the
first-generation look around the new caps and fields. That is the instruments (`Faceplate`, `Plate`,
`ViewHeader`, `Lamp`) reaching the pages, and it is where most of the colour-literal ratchet will shrink.

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
