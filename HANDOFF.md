# Handoff - Phase 2F-2's open list is closed; the button migration and 2F-3 are not

**Date:** 2026-09-29
**Branch:** `main`
**Working tree:** dirty - nothing committed. Thirteen paths, listed in section 6.
**Previous handoff:** the 2026-09-20 audit, every finding closed, is in git history at `a4b94ec`.

**State at handoff, each proven in section 1:**
`dotnet build SysMonitor.sln -c Debug -p:Platform=x64` -> **0 Warning(s), 0 Error(s)**;
`dotnet test` -> **773 passed, 0 failed** (was 761);
`python scripts/generate-tokens.py --check` -> matches;
`SysMonitor.App.exe --ui-smoke` -> **passed, 0 problems**, 72 pages over both shifts.

---

## 0. Read this first

Four things you cannot get from the diff.

1. **The subclass route named in the last handoff does not exist.** `ToggleSwitch` is `sealed` in
   WinUI 3. "A ConsoleSwitch subclass is the route" to the glows was wrong, and it was wrong in a way
   that only the compiler could say. What replaced it is an attached property, and that turned out to
   be the better shape for a reason beyond the sealing - section 4.1. **Do not spend time re-trying
   the subclass.**

2. **The drag check was written twice, because the first one could not fail.** Its first form wrote
   `KnobTranslateTransform.X` and read it back. A write to a dependency property returns that value
   when read whether or not it is the value being drawn, so the assertion was against itself. It was
   break-checked, passed while broken, and was rewritten to measure where the thumb is *drawn*
   (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:837`). The second form does fail - proof in
   section 2.4.

3. **The disabled item was not the gap it looked like, and resolving it changed the caps too.**
   "Disabled is a recolour, not CSS's opacity .42 + saturate(.5)" read as a shortfall in the switch.
   It was not: the stylesheet writes that rule once for the buttons and then points the switch at it
   with a comment saying a locked control must read the same way everywhere. So a switch-only change
   would have *created* the second idiom the stylesheet exists to prevent. Both now dim through one
   resource. Section 2.5.

4. **The anchor audit found three wrong anchors, and all three were the same mistake.** Not
   arithmetic drift - a range measured from the wrong line of a block. Section 2.1.

Everything below is proven, the command and its output given, unless it carries `UNVERIFIED:`.

---

## 1. State

Run from the repository root.

```
$ dotnet build SysMonitor.sln -c Debug -p:Platform=x64 --nologo
    0 Warning(s)
    0 Error(s)

$ dotnet test --nologo
Passed!  - Failed: 0, Passed: 773, Skipped: 0, Total: 773, Duration: 1 m 29 s

$ python scripts/generate-tokens.py --check
Tokens.xaml matches its generator.

$ ./src/SysMonitor.App/bin/x64/Debug/net8.0-windows10.0.22621.0/SysMonitor.App.exe --ui-smoke <folder>
exit 0
report.json: "passed": true, "problems": []
```

761 -> 773 is 12 new tests: 6 on the anchors, 3 on the switch, 3 on the locked treatment. Every one
was break-checked - the implementation broken, the test watched failing, the implementation restored.
The two new runtime checks in the smoke run were break-checked the same way.

---

## 2. What was done

### 2.1 The ~100 styles.css anchors are audited, and three were wrong

**Checked against `system-x-app@eaba14b`.** Counted three ways, because they are three different
things: **128** distinct line anchors across the console's sources; **70** hand-written anchor
instances outside the palette, each read against the line it names; and **68** palette-group
citations (34 groups, each cited for both shifts) covering **117** colour checks - 68 Night, 49 Day.
The other 19 Day values are identical to their Night ones, which Day Shift does not re-declare, so
its anchor says nothing about them and they are skipped rather than counted as passing.

**Three were wrong, all in `Instruments.xaml`, all the same mistake** - a range measured from two
lines into the block it cites rather than from its first line:

| Was | Is | Cites |
|---|---|---|
| `:458-467` | `:456-465` | `@keyframes blink-1hz` (`src/SysMonitor.App/Styles/Console/Instruments.xaml:247`) |
| `:469-478` | `:467-477` | `@keyframes on-air-pulse` (`src/SysMonitor.App/Styles/Console/Instruments.xaml:381`) |
| `:1188` | `:1182-1194` | the WARN-blinks comment (`src/SysMonitor.App/Styles/Console/Instruments.xaml:711`) |

`:458-467` is the clearest: cited as the 1 Hz blink, it began at `49.9% {` and ran past the block's
close into the keyframes after it. The same rule was cited correctly two hundred lines later in the
same file, which is how a reader could have caught it and nobody did.

**The stylesheet is now vendored** at `tests/SysMonitor.Tests/TestSupport/system-x-styles.css`, so
the anchors are checkable without the System-X working tree, which has moved on. It is pinned by
SHA-256 (`tests/SysMonitor.Tests/TestSupport/PinnedStylesheet.cs:27`) - an edited fixture fails
rather than quietly agreeing with whatever it was edited to say. The digest was taken from the blob
`git show eaba14b:src/styles.css` produces; the vendored body hashes identically to it under
`git hash-object`.

**Four rules now run in the suite** (`tests/SysMonitor.Tests/Architecture/ConsolePinAnchorTests.cs`):

- `:56` the fixture is the pinned revision, by digest
- `:91` a selector named beside an anchor opens at that line
- `:132` no anchor starts mid-comment or part-way down a selector list - **this is the one that
  caught all three**
- `:182` every palette colour is in the lines its group cites, through one hop of `var()`

**What it does not check.** Whether the prose beside an anchor describes what is there. That is a
reading, and it was done by hand once, this session, at that revision. Also **not** checked: where a
range *ends*. Some end on the closing brace and some on the last declaration before it; both land a
reader in the right rule, and the inconsistency is noise rather than error. Four ranges are in the
second style (`:911-918`, `:926-935`, `:439-452`, `:479-486`); they were read and left alone.

### 2.2 The two armed glows are drawn

`0 0 12px var(--armed-glow)` outside the track (`:2352`) and `0 0 10px` outside the thumb (`:2359`).
Both are composition drop shadows cast from `ConsoleLever`
(`src/SysMonitor.App/Controls/Instruments/ConsoleLever.cs:68` and `:71`) onto hosts in the template
(`src/SysMonitor.App/Styles/Console/Controls.xaml:444` and `:474`). The thumb's host sits inside
`SwitchKnob`, so its glow travels with the thumb instead of staying at the left of the track.

The colour is not in the code. The template carries the palette's armed glow on a collapsed border
(`src/SysMonitor.App/Styles/Console/Controls.xaml:439`) and `ConsoleLever` reads and watches it, so a
change of shift arrives as a change of that brush - the same way a lamp's outer glow is coloured.

**Proven at runtime**, because nothing static can see it: the hosts are empty borders with or without
a shadow on them. The smoke run counts the layers off and then on
(`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:728` and `:761`).

```
BREAK: the style's setter flipped to False
  Turned on, the switch in the night shift cast 0 glow(s) round its track and 0 round its
  thumb, not the one each an armed lever carries
  (and the same in the day shift)            -> exit 1, passed: false
RESTORED                                     -> exit 0, passed: true, problems: []
```

### 2.3 The travel is 160ms on ease-snap

`transition: transform 160ms var(--ease-snap)` (`:2347`), with ease-snap being
`cubic-bezier(.32, .72, 0, 1)` (`:710`). Windows' `RepositionThemeAnimation` is gone from the
template; all four transitions that move the knob now animate `KnobTranslateTransform.X` over
`0:0:0.16` on `KeySpline="0.32,0.72 0,1"` (`src/SysMonitor.App/Styles/Console/Controls.xaml:255`).
Pinned by `tests/SysMonitor.Tests/Architecture/ConsoleSwitchTests.cs:218`, which also fails if a
reposition animation comes back - one left in beside a transform animation moves the thumb twice.

### 2.4 Dragging is covered, and covering it found a bug

**The bug.** The `Off` state was empty, which meant it inherited whatever the transform already held.
A drag writes that transform. So a lever that was dragged and then switched off drew its thumb
wherever the finger let go of it, on a switch reading OFF. The first run of the new check reported
exactly that: `Turned off again, the switch in the day shift left its thumb 7 over`. `Off` now says
where the thumb rests (`src/SysMonitor.App/Styles/Console/Controls.xaml:344`).

This was live before this session's changes, not introduced by them. It needed a drag to show, and
nothing had ever dragged.

**The check** (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:837`) reproduces the part of a drag that
outlives the gesture: move to `Dragging`, write the transform as `ToggleSwitch` does on each move,
release toward off, and measure where the thumb is drawn against the track.

```
BREAK: the Off state emptied again
  After a drag, the switch in the night shift turned off with its thumb 13 back from where
  armed drew it, not the 20 it travels: armed 21, off 8. The drag wrote 7 onto the transform
  and the off state left it there, so a lever that reads OFF is drawn part-way on
  (and the same in the day shift)            -> exit 1, passed: false
RESTORED                                     -> exit 0, passed: true, problems: []
```

**What it is not.** Not a drag. No pointer reaches the `Thumb`, the gesture recogniser never runs,
and nothing here clamps the distance to the travel - the geometry test remains the nearest thing to
that (`tests/SysMonitor.Tests/Architecture/ConsoleSwitchTests.cs:131`). It covers what a drag leaves
on the transform, which is the part that persists and the part that was broken.

**Two hazards were tested for and do not exist.** A zero-duration state animation holding the
transform against a later write, and the `Dragging` state failing to release it. Both were tried by
construction - including deleting the `Dragging` state outright - and neither could be made to fail.
Assertions about them were removed rather than kept green. If you go looking for that class of bug,
it is not there.

### 2.5 The locked treatment is one idiom, and dims

`opacity: .42` (`:2016-2023` for the buttons, `:2365-2369` for the switch). It dims through one
resource the palette writes per shift, and **High Contrast writes 1** - dimming a control against a
ground the user chose for its contrast is the one thing that shift must not do, so it greys the words
with the user's own GrayText instead (`scripts/generate-tokens.py:341` and `:345`).

Applied to both the cap (`src/SysMonitor.App/Styles/Console/Controls.xaml:119`) and the switch
(`:242`), because the stylesheet asks for that in as many words. Three rules hold it there
(`tests/SysMonitor.Tests/Architecture/ConsoleLockedStateTests.cs:39`, `:59`, `:77`): every locked
state dims by the shared resource, every locked word comes from the shared brush, and High Contrast
does not dim. A second idiom is easy to add by accident - a Disabled state is written per template,
each looks reasonable alone, and nothing compared them until now.

**CHANGELOG.md was corrected, not just re-anchored.** Two entries claimed a locked cap and a locked
switch "fade to Graphite", which this made false. Five `path:line` anchors in it had also gone stale.
Both fixed.

---

## 3. What is NOT done

### 3.1 The button migration - this is the one waiting on you

**121 buttons across 27 pages** still ask for the legacy styles in
`src/SysMonitor.App/Styles/Styles.xaml` - hardcoded hex, a 24px pill radius, no shift awareness:

| Style | Uses | Defined |
|---|---|---|
| `OutlinedButtonStyle` | 62 | `src/SysMonitor.App/Styles/Styles.xaml:242` |
| `RedButtonStyle` | 41 | `src/SysMonitor.App/Styles/Styles.xaml:176` |
| `OutlineButtonStyle` | 17 | `src/SysMonitor.App/Styles/Styles.xaml:311` |
| `AccentButtonStyle` | 1 | WinUI's, which Phase 1 made armed red |

**48 other buttons already take the console cap**, because they ask for no style and the implicit one
is the cap. So the cap is in production; these 121 are what is left.

**The 79 secondary ones are mechanical** - delete the `Style` attribute and they take the cap. But
that changes 79 buttons from a 24px pill with `24,12` padding to a 4px cap with `14,0` and a 34
minimum height, across 27 pages. The shape change is the point of Phase 2F; the layout consequences
need eyes on the screenshots, and "the smoke run passes" only means nothing threw.

**The 41 red ones need a decision per button, and it is yours.** The armed cap is documented for "a
consequential command, and only those" - if ordinary actions wear it, armed red stops meaning
anything. The split is not obvious:

- reads consequential: `WIPE NOW`, `END TASK`, `DELETE`, `DELETE SELECTED`,
  `DeleteSelectedDuplicatesCommand`, `CLEAN SELECTED` (x2), `QUICK CLEAN`, `FIX SELECTED`
- reads ordinary: `Next`, `Done`, `Refresh`, `REFRESH`, `SAVE SETTINGS` (x2), `COMPRESS` (x2),
  `RUN HEALTH CHECK`, `DonateCommand`, the five `Scan*` commands, the five PDF operations,
  `ToggleGameModeCommand`, the two monitoring toggles
- genuinely arguable: `TRIM MEMORY`, `SAVE SETTINGS`, `QUICK CLEAN`

I did not migrate any of them. Doing so would have put ~10 product calls in my hands and changed the
shape of every page in the shipped app in a session where you could not see the result.

**`ChromeCapButtonStyle` is an ORPHAN** - confirmed, not assumed. Inbound references are the smoke
run (`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:607`), a test
(`tests/SysMonitor.Tests/Architecture/ConsoleCapTests.cs:42`), and its own definition and comment
(`src/SysMonitor.App/Styles/Console/Controls.xaml:172` and `:16`). **No production consumer.** It is
defined for "a page's single polished call to action" and no page asks for it. It stops being an
orphan as part of this migration - one page's call to action points at it - or it should go.

`ArmedCapButtonStyle` is **not** an orphan: `src/SysMonitor.App/Controls/Instruments/ConsoleDialog.cs:92`
gives it to every confirmation dialog's primary button.

### 3.2 `filter: saturate(.5)` is not drawn

The other half of the locked rule. WinUI has no filter, and the only route is a desaturated twin of
every armed face the lever can show, generated beside the original in the palette - roughly four
brushes across three shifts. Dimming to .42 carries the meaning on its own; desaturating a red
already at 42% is the smaller half. Written down beside the state it belongs to
(`src/SysMonitor.App/Styles/Console/Controls.xaml:242`) rather than left to be rediscovered.

### 3.3 Phase 2F-3, 2F-4, 3, 4, 5 not started

2F-3 is ready to transcribe; section 5 has what was read for it.

---

## 4. Findings that change the plan

### 4.1 `ToggleSwitch` is sealed, and the attached property is the better answer anyway

```
error CS0509: 'ConsoleSwitch': cannot derive from sealed type 'ToggleSwitch'
```

So the glows hang on the control from outside
(`src/SysMonitor.App/Controls/Instruments/ConsoleLever.cs:34`), and the switch's style turns them on
for every switch (`src/SysMonitor.App/Styles/Console/Controls.xaml:194`).

**This is better than the subclass would have been, sealing aside.** An implicit style keys on the
element's exact runtime type and a derived type does not inherit one. A `ConsoleSwitch` would have
needed its own implicit style, and all eleven switches on the four pages that have one would have had
to be rewritten to ask for it - and a twelfth added later without asking would have drawn as WinUI's
pill on a page. A switch that asks for nothing is still a bat lever, which is what the implicit style
is for. I had migrated those eleven before the compiler stopped me; the migration was reverted and
the pages are untouched.

The one cost: template parts are found by walking the applied template
(`src/SysMonitor.App/Controls/Instruments/ConsoleLever.cs:134`) instead of `GetTemplateChild`, which
only the control itself may call.

### 4.2 The instruments family has no production consumer

`Lamp`, `LedDot`, `Plate`, `Faceplate`, `VuMeter`, `Chip`, `Banner`, `Display`, `Lcd`, `Well`,
`HexBolt`, `ScanSweep`, `ViewHeader`, `BusyPanel`, `StandbyPanel` - the whole of
`src/SysMonitor.App/Controls/Instruments/` except `ConsoleLever` and `ConsoleDialog` - is reached from
exactly two XAML files: `src/SysMonitor.App/Styles/Console/Instruments.xaml` (their styles) and
`src/SysMonitor.App/Diagnostics/ConsoleSpecimen.xaml` (the smoke run's specimen page). No page under
`src/SysMonitor.App/Views/` declares the namespace.

Verified by the absence of `xmlns:instruments` in every view, and by
`grep -rl "Controls.Instruments" --include=*.xaml` returning those two files only.

This is not a regression and it is not on your list - it is the same root cause as 3.1. Phase 2 built
the console; the pages have not been moved onto it. Worth knowing before scoping 2F-4: **the
instrument set is a larger unmigrated surface than the buttons**, and the smoke run's specimen is
currently the only thing that proves any of it draws.

### 4.3 The generator gained non-colour per-shift resources

`scripts/generate-tokens.py:341` is the first per-shift value that is not a colour. If 3.2 gets done,
the desaturated twins go in the same place.

---

## 5. Phase 2F-3 readiness - what was read

`.field` at the pin, `:2286`:

```css
width: 100%;  padding: 8px 11px;  border-radius: var(--r-control);   /* 4px */
background: linear-gradient(180deg, #070707 0%, #0a0a0a 100%);
border: 1px solid rgba(0, 0, 0, 0.75);
box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.85), inset 0 -1px 0 rgba(255, 255, 255, 0.03);
color: #f5f5f5;  font-family: "Public Sans";  font-size: 13px;
transition: border-color 160ms var(--ease-out);        /* cubic-bezier(.2,.8,.2,1) */
```

- `::placeholder` `#6a6a6a` (`:2300`)
- `:focus` border `var(--armed-edge)`, `outline: 2px solid var(--armed-display)`, offset 1 (`:2303`)
- `.invalid` border `var(--led-warn)` (`:2308`)
- recessed dark **in both shifts** - the comment above it says so, so no Day override is expected

**Already there:**

- `FieldLabelTextStyle` (`src/SysMonitor.App/Styles/Console/Typography.xaml:64`) transcribes
  `.field-label` (`:2312`) - Departure Mono 10, tracked .12em, 5 above its field. Its only consumer
  is `ConsoleSpecimen.xaml`, so in production terms it is an orphan too.
- The inset-shadow idiom already exists: WinUI has none, and a well's is drawn as gradients over its
  face. `.field`'s `inset 0 2px 4px` is the same shape as the well's and the switch recess's, so
  `SwitchShadeTopBrush` has a working precedent to copy.

**Not there:** no console style for `TextBox`, `PasswordBox`, `AutoSuggestBox`, `ComboBox` or
`NumberBox`. The legacy `InputFieldStyle` (`src/SysMonitor.App/Styles/Styles.xaml:618`) and
`SearchTextBoxStyle` (`:628`) are asked for in 3 places on 2 pages.

**Surface, counted:** `TextBox` 9, `PasswordBox` 2, `ComboBox` 14, `NumberBox` 9, `CheckBox` 26,
`RadioButton` 14, `Slider` 12, across 19 pages. Only the first two are `.field` proper; decide
deliberately whether `ComboBox` and `NumberBox` are in 2F-3 or 2F-4, because the recessed well reads
as the same component to a user and half a form in two idioms is worse than either.

---

## 6. Re-running and reviewing

```
dotnet build SysMonitor.sln -c Debug -p:Platform=x64
dotnet test
python scripts/generate-tokens.py --check
dotnet run --project src/SysMonitor.App -- --ui-smoke <folder>
```

The smoke run writes `report.json`, `progress.txt`, and one PNG per page per shift (72 pages, 6
specimen pictures each shift) into the folder. `"passed": true` with `"problems": []` is the pass.

**Uncommitted, all of it:**

```
 M CHANGELOG.md                                            anchors and two false claims fixed
 M HANDOFF.md                                             this file; the 2026-09-20 one is at a4b94ec
 M scripts/generate-tokens.py                              per-shift locked treatment
 M src/SysMonitor.App/Diagnostics/UiSmokeRun.cs            glow counts, drag check
 M src/SysMonitor.App/Styles/Console/Controls.xaml         glow hosts, snap travel, Off rest, locked
 M src/SysMonitor.App/Styles/Console/Instruments.xaml      three corrected anchors
 M src/SysMonitor.App/Styles/Console/Tokens.xaml           generated
 M tests/SysMonitor.Tests/Architecture/ConsoleSwitchTests.cs
?? src/SysMonitor.App/Controls/Instruments/ConsoleLever.cs
?? tests/SysMonitor.Tests/Architecture/ConsoleLockedStateTests.cs
?? tests/SysMonitor.Tests/Architecture/ConsolePinAnchorTests.cs
?? tests/SysMonitor.Tests/TestSupport/PinnedStylesheet.cs
?? tests/SysMonitor.Tests/TestSupport/system-x-styles.css  vendored pin, 2655 lines
```

**Wiring.** `ConsoleLever.cs` is reached from
`src/SysMonitor.App/Styles/Console/Controls.xaml:194` (the setter, which is what constructs it) and
`src/SysMonitor.App/Diagnostics/UiSmokeRun.cs:728`. `PinnedStylesheet.cs` is reached from
`tests/SysMonitor.Tests/Architecture/ConsolePinAnchorTests.cs:56`. Neither is an orphan.
The two new test files are xUnit classes, collected by the runner.
