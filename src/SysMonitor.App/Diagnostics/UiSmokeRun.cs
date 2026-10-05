using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Serilog;
using SysMonitor.App.Controls.Instruments;
using SysMonitor.Core.Models;
using Windows.Graphics;
using WinRT.Interop;
using Drawing = System.Drawing;

namespace SysMonitor.App.Diagnostics;

/// <summary>
/// Opens every page in both themes, takes a picture of each, and fails on the errors that only appear when XAML
/// is loaded: a parse error, a navigation that fails, a resource that does not resolve. After each theme's pages
/// it shows every console instrument, so a template that cannot be built fails before a page uses it.
/// <para>
/// Nothing else can see those. The build compiles the XAML, but a <c>{StaticResource}</c> that names no resource,
/// or a style that only exists in the other theme, is found when the page is opened - by a user, if nobody opened
/// it first. CI runs the app with <c>--ui-smoke &lt;folder&gt;</c>: the run writes <c>report.json</c> and one PNG
/// per page and theme into the folder, and exits 0 only when every page opened cleanly.
/// </para>
/// <para>
/// A smoke run shows no tray icon, records no history, leaves the power plan alone, and never hides on close.
/// </para>
/// </summary>
internal sealed class UiSmokeRun
{
    private const string Switch = "--ui-smoke";

    /// <summary>How long a page gets to raise Loaded before it is reported as never having loaded.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Time for data a page fetches as it opens to reach the screen, so the picture shows the page.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    /// <summary>A run that has not finished by then is stuck; the report says how far it got.</summary>
    private static readonly TimeSpan Watchdog = TimeSpan.FromMinutes(12);

    private static readonly SizeInt32 WindowSize = new(1440, 900);

    private static readonly (ElementTheme Theme, string Name)[] Shifts =
    [
        (ElementTheme.Dark, "night"),
        (ElementTheme.Light, "day"),
    ];

    /// <summary>The console faces (Styles/Console/Fonts.xaml), each of which has to have loaded.</summary>
    private static readonly string[] ConsoleFaces =
    [
        "ConsoleBodyFontFamily", "ConsoleBodyMediumFontFamily", "ConsoleBodySemiBoldFontFamily",
        "ConsoleBodyBoldFontFamily", "ConsoleTitleFontFamily", "ConsolePlacardFontFamily", "ConsoleCapFontFamily",
        "ConsoleLampFontFamily", "ConsoleTelemetryFontFamily", "ConsoleCodeFontFamily",
    ];

    /// <summary>Wide enough, and mixed enough, that no two of these faces set it to the same width.</summary>
    private const string FontProbeText = "MOD - MONITOR - 23  S/N STX-2266-20  48%";

    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly List<PageResult> _pages = new();
    private readonly List<string> _problems = new();
    private readonly List<string> _notes = new();
    private readonly List<string> _checks = new();
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private PageResult? _current;
    private Timer? _watchdog;

    private UiSmokeRun(string folder) => Folder = folder;

    /// <summary>Where the report and the pictures go.</summary>
    public string Folder { get; }

    /// <summary>The run asked for on the command line, or null when the app was started normally.</summary>
    public static UiSmokeRun? FromCommandLine(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], Switch, StringComparison.OrdinalIgnoreCase))
                return new UiSmokeRun(Path.GetFullPath(args[i + 1]));
        }

        return null;
    }

    /// <summary>
    /// Starts listening for the failures a page can only show when it loads. Called before the main window is
    /// built, so its own XAML is watched too.
    /// </summary>
    public void Attach(Application application)
    {
        Directory.CreateDirectory(Folder);
        _watchdog = new Timer(_ => GiveUp(), null, Watchdog, Timeout.InfiniteTimeSpan);

        var debug = application.DebugSettings;
        debug.IsXamlResourceReferenceTracingEnabled = true;
        debug.XamlResourceReferenceFailed += (_, args) => Problem($"Resource not found: {args.Message}");

        // Binding failures are worth reading, but the pages are full of old ones; they are noted, not failed.
        debug.IsBindingTracingEnabled = true;
        debug.BindingFailed += (_, args) => Note($"Binding failed: {args.Message}");
    }

    /// <summary>
    /// Takes an exception nothing else handled. A XAML parse error fails the page it happened on; anything else
    /// is noted against it and the run carries on.
    /// </summary>
    public void Absorb(Exception exception)
    {
        if (exception is XamlParseException)
            Problem($"XAML parse error: {exception}");
        else
            Note($"Unhandled {exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>Opens every page in every theme, then exits with 0 when all of them opened cleanly.</summary>
    public async Task RunAsync(MainWindow window)
    {
        try
        {
            window.AppWindow.MoveAndResize(new RectInt32(0, 0, WindowSize.Width, WindowSize.Height));
            window.PageFrame.NavigationFailed += (_, args) =>
            {
                Problem($"Navigation to {args.SourcePageType?.Name} failed: {args.Exception}");
                args.Handled = true;
            };

            if (window.Content is not FrameworkElement root)
            {
                Problem("The main window has no content");
                Finish();
                return;
            }

            await LoadedAsync(root);
            CheckFonts(root);
            CheckThemeResources();
            CheckFluentOverrides();
            CheckTextStyles(root);

            foreach (var (theme, name) in Shifts)
            {
                root.RequestedTheme = theme;
                await RenderedAsync();

                var index = 0;
                foreach (var (tag, pageType) in window.Pages)
                    await VisitAsync(window, name, ++index, tag, pageType);

                await VisitSpecimenAsync(window, theme, name);
            }
        }
        catch (Exception ex)
        {
            Problem($"The run itself failed: {ex}");
        }

        Finish();
    }

    private async Task VisitAsync(MainWindow window, string shift, int index, string tag, Type pageType)
    {
        var page = new PageResult { Shift = shift, Page = tag, Type = pageType.Name };
        lock (_gate)
        {
            _current = page;
            _pages.Add(page);
        }

        // Written before the step, so a run that hangs here says where.
        Progress($"{shift} {index:00} {tag}");
        var timer = Stopwatch.StartNew();

        try
        {
            window.NavigateToPage(tag);

            if (window.PageFrame.Content is not FrameworkElement content || content.GetType() != pageType)
            {
                Problem($"The frame shows {window.PageFrame.Content?.GetType().Name ?? "nothing"}, not {pageType.Name}");
            }
            else if (!await LoadedAsync(content))
            {
                Problem($"The page did not load within {LoadTimeout.TotalSeconds:0} seconds");
            }
            else
            {
                page.Loaded = true;
            }

            await RenderedAsync();
            await Task.Delay(Settle);
            await RenderedAsync();

            page.Screenshot = $"{shift}-{index:00}-{tag}.png";
            Capture(window, Path.Combine(Folder, page.Screenshot));
        }
        catch (Exception ex)
        {
            Problem($"Opening the page threw: {ex}");
        }

        page.Milliseconds = timer.ElapsedMilliseconds;
        lock (_gate)
            _current = null;

        WriteReport(finished: false);
    }

    /// <summary>
    /// Shows every console instrument in every state (<see cref="ConsoleSpecimen"/>) where a page would be, after
    /// the shift's pages. An instrument's template is only built when something shows it, so one that cannot be
    /// built fails here, on every push, before a page uses it. The pictures are the review copy; the run passes on
    /// the checks.
    /// <para>
    /// Each instrument is shown alone, twice, since a page shows many copies and a first copy can leave something
    /// behind that trips the next. Then text inside each kind of host, a faceplate one part at a time, each panel
    /// with all its slots filled, and then the whole specimen. Every step is laid out inside a
    /// <see cref="LayoutProbe"/>, so a template that fails in layout is reported against its step and the run goes
    /// on to the next one. A failure while rendering still ends the process, and then the step written last names
    /// what was on screen.
    /// </para>
    /// </summary>
    private async Task VisitSpecimenAsync(MainWindow window, ElementTheme theme, string shift)
    {
        var page = new PageResult { Shift = shift, Page = "specimen", Type = nameof(ConsoleSpecimen) };
        lock (_gate)
        {
            _current = page;
            _pages.Add(page);
        }

        var timer = Stopwatch.StartNew();

        try
        {
            Progress($"{shift} specimen: an empty scroller in the frame");
            var scroller = new ScrollViewer();
            window.PageFrame.Content = scroller;
            await RenderedAsync();
            await RenderedAsync();

            foreach (var type in Instruments())
            {
                await ShowAsync(scroller, $"{shift} specimen: {type.Name} alone",
                    (UIElement)Activator.CreateInstance(type)!);
                await ShowAsync(scroller, $"{shift} specimen: {type.Name} alone, built again",
                    (UIElement)Activator.CreateInstance(type)!);
            }

            // The text styles were measured at the start, but measuring draws nothing; this draws each one.
            foreach (var (key, style) in ConsoleTextStyles())
            {
                await ShowAsync(scroller, $"{shift} specimen: the text style {key} drawn",
                    new TextBlock { Text = FontProbeText, Style = style });
            }

            // Then text in a console style inside each kind of host, one at a time.
            foreach (var (step, host) in TextHosts())
                await ShowAsync(scroller, $"{shift} specimen: {step}", host);

            // Then each panel with every slot it has filled, since a slot is where a second parent comes from.
            foreach (var (step, panel) in FilledPanels())
                await ShowAsync(scroller, $"{shift} specimen: {step}", panel);

            // Day Shift's chassis is light enough for a shadow to show on; Night Ops' is near black already.
            if (theme == ElementTheme.Light)
                await CheckShadowPixelsAsync(window, scroller, shift);

            Progress($"{shift} specimen: all of it");
            var specimen = new ConsoleSpecimen();
            var probe = new LayoutProbe(specimen);
            scroller.Content = probe;

            if (!await LoadedAsync(specimen))
                Problem($"The specimen did not load within {LoadTimeout.TotalSeconds:0} seconds");
            else
                page.Loaded = true;

            await RenderedAsync();
            await Task.Delay(Settle);
            await RenderedAsync();

            if (!Survived(probe, $"{shift} specimen: all of it"))
                return;

            Progress($"{shift} specimen: checking");
            var followedShift = CheckInstruments(specimen, theme, shift);

            // The caps and the confirmation, in the live tree and in this shift: both are questions only a
            // running WinUI answers. The specimen's own root is the host, so they inherit the shift with it.
            if (specimen.Content is Panel capHost)
                CheckCaps(capHost, theme, shift);
            else
                Problem($"The specimen's content is not a panel, so the caps could not be measured in the {shift} shift");

            if (specimen.Content is Panel leverHost)
            {
                await CheckSwitchAsync(leverHost, theme, shift);
                await CheckFieldsAsync(leverHost, theme, shift);
                await CheckStateBrushAsync(leverHost, theme, shift);
            }

            await CheckDialogsAsync(specimen, theme, shift);

            // It is taller than the window: one picture per window's height of it.
            var shots = new List<string>();
            for (var offset = 0.0; shots.Count < 8; offset += scroller.ViewportHeight)
            {
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                await RenderedAsync();
                await RenderedAsync();

                var shot = $"{shift}-specimen-{shots.Count + 1}.png";
                Progress($"{shift} specimen: picture {shots.Count + 1}");
                Capture(window, Path.Combine(Folder, shot));
                shots.Add(shot);

                if (scroller.ViewportHeight <= 0 || offset + scroller.ViewportHeight >= scroller.ExtentHeight)
                    break;
            }

            page.Screenshot = string.Join(", ", shots);

            // After the pictures, because finding out changes the specimen.
            if (!followedShift)
            {
                Progress($"{shift} specimen: why its text did not follow the shift");
                await DescribeShiftAsync(specimen, theme, shift);
            }
        }
        catch (Exception ex)
        {
            Problem($"Showing the specimen threw: {ex}");
        }
        finally
        {
            page.Milliseconds = timer.ElapsedMilliseconds;
            lock (_gate)
                _current = null;

            WriteReport(finished: false);
        }
    }

    /// <summary>
    /// Shows one element where a page would be and lets it draw. A failure in its layout is reported against the
    /// step, and does not end the run.
    /// </summary>
    private async Task ShowAsync(ScrollViewer scroller, string step, UIElement element)
    {
        Progress(step);
        var probe = new LayoutProbe(element);
        scroller.Content = probe;
        await RenderedAsync();
        await RenderedAsync();
        Survived(probe, step);
    }

    /// <summary>False, with the failure reported against the step, when the probed element failed in layout.</summary>
    private bool Survived(LayoutProbe probe, string step)
    {
        if (probe.Failure is not { } failure)
            return true;

        Log.Error(failure, "UI smoke: {Step} failed in layout", step);
        Problem($"{step} failed in layout: {failure.GetType().Name} 0x{failure.HResult:X8} " +
                failure.Message.ReplaceLineEndings(" "));
        return false;
    }

    /// <summary>
    /// What only the live tree can say about the instruments. Each one drew its template: an implicit style that
    /// did not apply leaves a control that draws nothing and raises nothing. A lamp is heard by its word, and a VU
    /// meter drew the segments it was set to. Everything inside a well or a display is in the dark theme, because
    /// displays stay dark in both shifts. And a line asking for Silver inside a well gets Night Ops silver, while
    /// the same line on a faceplate gets the shift's own - the theme reaching an element is not the same as its
    /// resources being looked up again in it, and text built before it joined the window would pass the first
    /// half without following any theme at all. False when the faceplate's text missed the shift, so the visit can
    /// measure why.
    /// </summary>
    private bool CheckInstruments(ConsoleSpecimen specimen, ElementTheme theme, string shift)
    {
        // Only what is shown: a collapsed instrument, such as a bolt on a faceplate too narrow for bolts, is never
        // laid out, so it never builds its template.
        var instruments = Descendants(specimen).OfType<Control>()
            .Where(control => control.GetType().Namespace == typeof(Faceplate).Namespace && IsShown(control, specimen))
            .ToList();
        if (instruments.Count == 0)
        {
            Problem("The specimen shows no instruments");
            return true;
        }

        foreach (var instrument in instruments.Where(instrument => VisualTreeHelper.GetChildrenCount(instrument) == 0))
            Problem($"A {instrument.GetType().Name} drew nothing: its template was not applied");

        // Status is a word first: a screen reader has to hear it, not only a colour on the screen.
        foreach (var lamp in instruments.OfType<Lamp>())
        {
            var spoken = FrameworkElementAutomationPeer.CreatePeerForElement(lamp)?.GetName();
            if (string.IsNullOrEmpty(spoken) || !spoken.Contains(lamp.Code, StringComparison.Ordinal))
                Problem($"The {lamp.Code} lamp is read as \"{spoken}\": a screen reader has to hear its word");
        }

        foreach (var meter in instruments.OfType<VuMeter>())
        {
            var segments = Descendants(meter).OfType<Grid>().FirstOrDefault(grid => grid.Name == "PART_Segments");
            var cells = segments?.Children.Count ?? 0;
            if (cells != meter.Segments)
                Problem($"A VU meter set to {meter.Segments} segments drew {cells}");
        }

        CheckPanels(specimen, instruments, shift);
        CheckDepth(instruments, shift);

        foreach (var surface in instruments.Where(instrument => instrument is Well or Display))
        {
            var light = Descendants(surface).OfType<FrameworkElement>()
                .FirstOrDefault(element => element.ActualTheme != ElementTheme.Dark);
            if (light is not null)
                Problem($"A {light.GetType().Name} inside a {surface.GetType().Name} is in the {light.ActualTheme} theme: " +
                        "displays stay dark in both shifts");
        }

        var nightSilver = PaletteColour(ElementTheme.Dark, "SilverColor");
        var shiftSilver = PaletteColour(theme, "SilverColor");
        var inWell = (specimen.InWell.Foreground as SolidColorBrush)?.Color;
        var onFace = (specimen.OnFace.Foreground as SolidColorBrush)?.Color;
        var followedShift = true;
        if (nightSilver is null || shiftSilver is null)
        {
            Problem("Styles/Console/Tokens.xaml has no SilverColor to compare the specimen's text with");
        }
        else if (onFace != shiftSilver)
        {
            Problem($"Silver on a faceplate is {onFace?.ToString() ?? "not a solid colour"}, not the {shift} shift's " +
                    $"{shiftSilver}: the specimen's text did not follow the shift, so the well's check proves nothing");
            followedShift = false;
        }
        else if (inWell != nightSilver)
        {
            Problem($"Silver inside a well is {inWell?.ToString() ?? "not a solid colour"}, not Night Ops silver " +
                    $"{nightSilver}: the well's text follows the shift");
        }

        Checked($"{instruments.Count} instruments drew their templates in the {shift} shift, " +
                "with their wells and displays dark");
        return followedShift;
    }

    /// <summary>
    /// What the panels have to show of what they were given. A banner is heard by its word and lights its lamp with
    /// it, and a fault is announced at once. A view header's kicker reaches its faceplate's stripe, and its status
    /// and actions are drawn inside it: each passes through the header's template and then the faceplate's. A plate
    /// in a state lights its rail and a neutral one has none. A busy panel shows its bar only for a share it knows,
    /// and 42.5% reads 43%, rounded half up as System-X rounds it; its title, which its template colours inside a
    /// well, is Night Ops platinum in both shifts. The busy sweep shows while animation effects are on, and not at
    /// all while they are off.
    /// </summary>
    private void CheckPanels(ConsoleSpecimen specimen, IReadOnlyList<Control> instruments, string shift)
    {
        var banners = instruments.OfType<Banner>().ToList();
        foreach (var banner in banners)
        {
            var spoken = FrameworkElementAutomationPeer.CreatePeerForElement(banner)?.GetName();
            if (string.IsNullOrEmpty(spoken) || !spoken.Contains(banner.ShownCode, StringComparison.Ordinal))
                Problem($"The {banner.ShownCode} banner is read as \"{spoken}\": a screen reader has to hear its word");

            var lamp = Descendants(banner).OfType<Lamp>().FirstOrDefault();
            if (lamp is null || lamp.Code != banner.ShownCode || lamp.State != banner.State)
                Problem($"The {banner.ShownCode} banner's lamp reads {lamp?.Code ?? "nothing"} in the " +
                        $"{lamp?.State.ToString() ?? "no"} state, not its banner's word and state");

            var live = AutomationProperties.GetLiveSetting(banner);
            var expected = banner.State is LampState.NoGo or LampState.Warn ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite;
            if (live != expected)
                Problem($"The {banner.ShownCode} banner is a {live} live region, not {expected}: faults are heard at once");
        }

        foreach (var header in instruments.OfType<ViewHeader>())
        {
            if (!Descendants(header).OfType<TextBlock>().Any(text => text.Text == header.KickerText))
                Problem($"A view header's kicker \"{header.KickerText}\" did not reach its faceplate's stripe");

            foreach (var (slot, content) in new[] { ("status", header.Status), ("actions", header.Actions) })
            {
                if (content is UIElement element && !IsWithin(element, header))
                    Problem($"A view header's {slot} are not drawn inside it");
            }
        }

        var plates = instruments.OfType<Plate>().ToList();
        foreach (var plate in plates)
        {
            var lit = Named<Border>(plate, "Rail")?.Background is not null;
            if (lit != (plate.State != LampState.Off))
                Problem($"A plate in the {plate.State} state has its rail {(lit ? "lit" : "unlit")}: a plate in a state " +
                        "lights its rail, and a neutral one has none");
        }

        foreach (var panel in instruments.OfType<BusyPanel>())
        {
            var bar = Named<ProgressBar>(panel, "PART_Bar");
            var knows = double.IsFinite(panel.Value);
            var barShown = bar is not null && IsShown(bar, panel);
            if (barShown != knows)
                Problem($"A busy panel at {panel.Value} {(barShown ? "shows" : "hides")} its bar: it shows one only for a share it knows");
            else if (knows && Math.Abs(bar!.Value - Math.Clamp(panel.Value, 0, 100)) > 0.001)
                Problem($"A busy panel at {panel.Value} set its bar to {bar.Value}");
        }

        // Platinum asked for inside a well by an attribute in a template, where the checks above ask through a
        // style: Night Ops platinum in both shifts, as System-X's well declares it.
        var nightPlatinum = PaletteColour(ElementTheme.Dark, "PlatinumColor");
        var title = (Named<TextBlock>(specimen.Progress, "TitleText")?.Foreground as SolidColorBrush)?.Color;
        if (nightPlatinum is not null && title != nightPlatinum)
            Problem($"A busy panel's title, Platinum inside its well, is {title?.ToString() ?? "not a solid colour"}, " +
                    $"not Night Ops platinum {nightPlatinum}: text in a well follows the shift");

        if (specimen.Progress.PercentText != "43%")
            Problem($"A busy panel at 42.5% reads \"{specimen.Progress.PercentText}\", not 43%: shares round half up");

        var sweeps = instruments.Where(instrument => instrument is BusyWell or BusyPanel)
            .Select(instrument => Named<Border>(instrument, "PART_ScanBand")).ToList();
        foreach (var band in sweeps)
        {
            var sweeping = band is { Visibility: Visibility.Visible, ActualHeight: > 0 };
            if (sweeping == Motion.IsReduced)
                Problem(Motion.IsReduced
                    ? "The busy sweep shows while animation effects are off"
                    : "The busy sweep did not show while animation effects are on");
        }

        Checked($"The panels in the {shift} shift: {banners.Count} banners heard by their word and lit with it, the view " +
                $"header's kicker, status and actions drawn in its faceplate, {plates.Count} plates' rails, busy bars only " +
                $"for a known share, and {sweeps.Count} busy sweeps {(Motion.IsReduced ? "still, as animation effects are off" : "running")}");
    }

    /// <summary>
    /// The console's depth, as far as the tree can say it: each shown faceplate casts its four shadows, a plate its
    /// one, and a lamp one more when lit, its outer glow; each lit lamp's word, lit LED and LCD reading glows, and
    /// nothing unlit does. Whether the shadows reach the screen is a question for pixels
    /// (<see cref="CheckShadowPixelsAsync"/>).
    /// </summary>
    private void CheckDepth(IReadOnlyList<Control> instruments, string shift)
    {
        var faceplates = instruments.OfType<Faceplate>().ToList();
        foreach (var faceplate in faceplates.Where(faceplate => faceplate.ShadowLayers != 4))
            Problem($"A faceplate (\"{faceplate.Kicker}\") casts {faceplate.ShadowLayers} shadows, not its 4");

        var plates = instruments.OfType<Plate>().ToList();
        foreach (var plate in plates.Where(plate => plate.ShadowLayers != 1))
            Problem($"A plate in the {plate.State} state casts {plate.ShadowLayers} shadows, not its 1");

        var lamps = instruments.OfType<Lamp>().ToList();
        foreach (var lamp in lamps)
        {
            var lit = lamp.State != LampState.Off;
            if (lamp.ShadowLayers != (lit ? 2 : 1))
                Problem($"The {lamp.Code} lamp casts {lamp.ShadowLayers} shadows, not " +
                        (lit ? "2, its own and its glow" : "1"));
            if (lamp.IsWordGlowing != lit)
                Problem($"The {lamp.Code} lamp's word " +
                        (lit ? "does not glow, though the lamp is lit" : "glows, though the lamp is not lit"));
        }

        var dots = instruments.OfType<LedDot>().ToList();
        foreach (var dot in dots.Where(dot => dot.IsGlowing != (dot.State != LampState.Off)))
            Problem($"An LED in the {dot.State} state {(dot.IsGlowing ? "has a halo" : "has no halo")}");

        var readings = instruments.OfType<Lcd>().ToList();
        foreach (var lcd in readings.Where(lcd => !lcd.IsGlowing))
            Problem($"The {lcd.Label} LCD's reading does not glow");

        Checked($"Depth in the {shift} shift: {faceplates.Count} faceplates cast their four shadows, {plates.Count} plates " +
                $"and {lamps.Count} lamps theirs, and lit lamps' words, {dots.Count(dot => dot.IsGlowing)} LEDs and " +
                $"{readings.Count} LCD readings glow");
    }

    /// <summary>
    /// Whether a button nobody styled is a cap, and whether the two caps that carry more weight are the faces
    /// they are meant to be.
    /// <para>
    /// The dictionary can be read without running anything (ConsoleCapTests), but whether an implicit style
    /// actually reaches a button cannot: WinUI applies one only when nothing nearer has already styled the
    /// element, and every control template in the framework that holds a button of its own defends it with a
    /// local implicit style. So each cap is built here, in the live tree, in this shift, and asked what face it
    /// came out with - against the palette, which is the only thing that says what the answer should be.
    /// </para>
    /// </summary>
    private void CheckCaps(Panel host, ElementTheme theme, string shift)
    {
        // The plain cap has no key: a bare button, with nothing asked of it, is the only way to ask for it.
        var caps = new (string What, string Face, string? Key, Button Cap)[]
        {
            ("a button nobody styled", "CapFaceBrush", null, new Button { Content = "CAP" }),
            ("an armed cap", "ArmedCapFaceBrush", "ArmedCapButtonStyle", Styled("ArmedCapButtonStyle")),
            ("a chrome cap", "ChromeCapFaceBrush", "ChromeCapButtonStyle", Styled("ChromeCapButtonStyle")),
        };

        var measured = 0;
        foreach (var (what, face, key, cap) in caps)
        {
            if (key is not null && cap.Style is null)
            {
                Problem($"{what} could not be built in the {shift} shift: {key} is not in the application's resources");
                continue;
            }

            cap.Opacity = 0;
            cap.IsHitTestVisible = false;
            host.Children.Add(cap);
            try
            {
                cap.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

                var wanted = Describe(PaletteBrush(theme, face));
                var drawn = Describe(cap.Background);
                if (wanted is null)
                    Problem($"The {shift} palette does not write {face}, so {what} cannot be checked");
                else if (drawn != wanted)
                    Problem($"In the {shift} shift {what} came out {drawn}, where the palette's {face} is {wanted}: " +
                            "the cap did not reach it");
                else
                    measured++;
            }
            catch (Exception ex)
            {
                Problem($"{what} could not be measured in the {shift} shift: {ex.Message}");
            }
            finally
            {
                host.Children.Remove(cap);
            }
        }

        if (measured == caps.Length)
            Checked($"The caps in the {shift} shift: a button nobody styled, an armed cap and a chrome cap each " +
                    "drew the palette's face for it");

        static Button Styled(string key) =>
            new() { Content = "CAP", Style = Application.Current.Resources[key] as Style };
    }

    /// <summary>
    /// What a switch actually draws, off and then on. The dictionary can be read without running anything
    /// (ConsoleSwitchTests), but none of this can: WinUI applies an implicit style only where nothing nearer has
    /// styled the element, the armed track and the armed thumb are revealed by a visual state rather than by any
    /// value the template carries, and the knob's travel is a number the control works out for itself from two of
    /// the template's parts. So a switch is built here, in this shift, turned on, turned off again, and asked at
    /// each step what it came out as.
    /// </summary>
    private async Task CheckSwitchAsync(Panel host, ElementTheme theme, string shift)
    {
        Progress($"{shift} specimen: the bat-lever switch, off and on");

        var lever = new ToggleSwitch { Header = "SPECIMEN", Opacity = 0, IsHitTestVisible = false };
        host.Children.Add(lever);
        try
        {
            await SettledAsync();

            // Why InstrumentTemplateTests turns its presenter rule off for this one template: the hazard needs a
            // Content on the templated parent to hand a presenter a second time, and a Control that is not a
            // ContentControl has none. That is a fact about the framework, so it is asked of the framework here
            // rather than asserted in a comment there.
            if (lever.GetType().IsSubclassOf(typeof(ContentControl)))
                Problem("ToggleSwitch is a ContentControl after all, so a presenter in its template can be given " +
                        "the control's own content a second time, and the exemption in InstrumentTemplateTests " +
                        "reopens the crash it was written for");

            var track = Named<Border>(lever, "OuterBorder");
            var armedTrack = Named<Border>(lever, "SwitchKnobBounds");
            var thumbOff = Named<Border>(lever, "SwitchKnobOff");
            var thumbOn = Named<Border>(lever, "SwitchKnobOn");
            var knob = Named<Grid>(lever, "SwitchKnob");
            if (track is null || armedTrack is null || thumbOff is null || thumbOn is null || knob is null)
            {
                Problem($"The switch in the {shift} shift is missing a part the bat lever is drawn from, so it " +
                        "came out as WinUI's pill or not at all");
                return;
            }

            var problems = 0;
            void Face(string what, Brush? drawn, string token)
            {
                var wanted = Describe(PaletteBrush(theme, token));
                if (wanted is null)
                {
                    problems++;
                    Problem($"The {shift} palette does not write {token}, so {what} cannot be checked");
                }
                else if (Describe(drawn) != wanted)
                {
                    problems++;
                    Problem($"In the {shift} shift {what} came out {Describe(drawn)}, where the palette's {token} " +
                            $"is {wanted}");
                }
            }

            Face("the switch's track", track.Background, "SwitchTrackBrush");
            Face("the switch's edge", track.BorderBrush, "SwitchEdgeBrush");
            Face("the thumb", thumbOff.Background, "SwitchThumbBrush");
            Face("the armed track", armedTrack.Background, "SwitchTrackArmedBrush");
            Face("the armed thumb", thumbOn.Background, "SwitchThumbArmedBrush");

            if (Travel(knob) != 0)
            {
                problems++;
                Problem($"The switch in the {shift} shift starts with its thumb {Travel(knob)} over, not at rest");
            }

            if (armedTrack.Opacity > 0.01)
            {
                problems++;
                Problem($"The switch in the {shift} shift shows its armed track while it is off");
            }

            var (trackGlow, thumbGlow) = ConsoleLever.GlowLayers(lever);
            if (trackGlow != 0 || thumbGlow != 0)
            {
                problems++;
                Problem($"The switch in the {shift} shift glows while it is off, at {trackGlow} round the track " +
                        $"and {thumbGlow} round the thumb; only an armed lever is lit");
            }

            lever.IsOn = true;
            await SettledAsync();

            // 20 is translateX(20px), and also what WinUI computes from SwitchKnobBounds less SwitchKnob. A
            // template whose two parts disagree with the state's own number lands the thumb in one place when it
            // is clicked and another when it is dragged, which is the thing ConsoleSwitchTests cannot see.
            if (Travel(knob) != 20)
            {
                problems++;
                Problem($"Turned on, the switch in the {shift} shift moved its thumb {Travel(knob)}, not the 20 " +
                        "the lever travels");
            }

            if (armedTrack.Opacity < 0.99 || thumbOn.Opacity < 0.99)
            {
                problems++;
                Problem($"Turned on, the switch in the {shift} shift left its armed track at " +
                        $"{armedTrack.Opacity:0.##} and its armed thumb at {thumbOn.Opacity:0.##}, so it does not " +
                        "read as armed");
            }

            // The two glows CSS gives an armed lever, 0 0 12px round the track and 0 0 10px round the thumb.
            // Composition casts them from ConsoleLever, so the template alone cannot show whether they are
            // there: the hosts are empty Borders either way, with or without a shadow on them. One each, the
            // way a lamp's shadows are counted - this run is not in High Contrast, where they would both go.
            (trackGlow, thumbGlow) = ConsoleLever.GlowLayers(lever);
            if (trackGlow != 1 || thumbGlow != 1)
            {
                problems++;
                Problem($"Turned on, the switch in the {shift} shift cast {trackGlow} glow(s) round its track " +
                        $"and {thumbGlow} round its thumb, not the one each an armed lever carries");
            }

            problems += await CheckSwitchDragAsync(lever, knob, track, shift);

            lever.IsOn = false;
            await SettledAsync();

            if (Travel(knob) != 0)
            {
                problems++;
                Problem($"Turned off again, the switch in the {shift} shift left its thumb {Travel(knob)} over");
            }

            if (problems == 0)
                Checked($"The bat-lever switch in the {shift} shift drew the palette's recess, edge and thumb, and " +
                        "armed itself and travelled 20 when it was turned on");
        }
        catch (Exception ex)
        {
            Problem($"The switch could not be measured in the {shift} shift: {ex.Message}");
        }
        finally
        {
            host.Children.Remove(lever);
        }

        static double Travel(Grid knob) => (knob.RenderTransform as TranslateTransform)?.X ?? double.NaN;
    }

    /// <summary>
    /// What a field actually draws. The dictionary can be read without running anything (ConsoleFieldTests), but
    /// not whether WinUI applied the implicit styles, whether NumberBox's own text box took the field it was
    /// pointed at, or whether focus arms one. So a text box, a password box and a number box are built here, in
    /// this shift, and asked what face and edge they came out with - and the text box is focused and asked again.
    /// </summary>
    private async Task CheckFieldsAsync(Panel host, ElementTheme theme, string shift)
    {
        Progress($"{shift} specimen: the form fields");

        // InstrumentTemplateTests lets these templates bind presenters to Header and Description because none of
        // the three has a Content of its own for WinUI to hand a presenter a second time. Asked of the framework.
        foreach (var type in new[] { typeof(TextBox), typeof(PasswordBox), typeof(NumberBox) })
        {
            if (type.IsSubclassOf(typeof(ContentControl)))
                Problem($"{type.Name} is a ContentControl after all, so the exemption in InstrumentTemplateTests " +
                        "reopens the crash it was written for");
        }

        var text = new TextBox { Header = "SPECIMEN", PlaceholderText = "specimen", Opacity = 0, IsHitTestVisible = false };
        var password = new PasswordBox { Header = "SPECIMEN", Opacity = 0, IsHitTestVisible = false };
        var number = new NumberBox { Header = "SPECIMEN", Value = 1, Opacity = 0, IsHitTestVisible = false };
        var fields = new Control[] { text, password, number };

        foreach (var field in fields)
            host.Children.Add(field);

        try
        {
            await SettledAsync();

            var problems = 0;
            void Face(string what, Brush? drawn, string token)
            {
                var wanted = Describe(PaletteBrush(theme, token));
                if (wanted is null)
                {
                    problems++;
                    Problem($"The {shift} palette does not write {token}, so {what} cannot be checked");
                }
                else if (Describe(drawn) != wanted)
                {
                    problems++;
                    Problem($"In the {shift} shift {what} came out {Describe(drawn)}, where the palette's {token} " +
                            $"is {wanted}: the field did not reach it");
                }
            }

            // NumberBox draws its well through InputBox, so the well to measure is that text box's.
            var input = Named<TextBox>(number, "InputBox");
            var wells = new (string What, Control Owner)[]
            {
                ("a text box nobody styled", text),
                ("a password box nobody styled", password),
                ("a number box's own text box", input ?? (Control)number),
            };

            foreach (var (what, owner) in wells)
            {
                var well = Named<Border>(owner, "BorderElement");
                if (well is null || Named<Border>(owner, "FocusOutline") is null)
                {
                    problems++;
                    Problem($"In the {shift} shift {what} has no console well to measure, so it came out as WinUI's " +
                            "text box or not at all");
                    continue;
                }

                Face($"{what}'s face", well.Background, "FieldFaceBrush");
                Face($"{what}'s edge", well.BorderBrush, "FieldEdgeBrush");
            }

            // Focus is the window's to give, and a window the run does not have the foreground in may refuse it.
            // A refusal is noted rather than failed: it says nothing about the field.
            var outline = Named<Border>(text, "FocusOutline");
            var edge = Named<Border>(text, "BorderElement");
            if (outline is not null && edge is not null)
            {
                if (outline.Opacity > 0.01)
                {
                    problems++;
                    Problem($"In the {shift} shift an unfocused text box shows its armed outline");
                }

                text.IsHitTestVisible = true;
                if (text.Focus(FocusState.Programmatic))
                {
                    await RenderedAsync();
                    await RenderedAsync();

                    if (outline.Opacity < 0.99)
                    {
                        problems++;
                        Problem($"Focused, the text box in the {shift} shift left its armed outline at {outline.Opacity:0.##}");
                    }

                    Face("a focused text box's edge", edge.BorderBrush, "ArmedEdgeBrush");
                }
                else
                {
                    Note($"The text box in the {shift} shift could not be given focus, so its armed outline was not measured");
                }
            }

            if (problems == 0)
                Checked($"The fields in the {shift} shift: a text box, a password box and a number box each drew " +
                        "the palette's well, and the text box armed its edge and outline when focused");
        }
        catch (Exception ex)
        {
            Problem($"The fields could not be measured in the {shift} shift: {ex.Message}");
        }
        finally
        {
            foreach (var field in fields)
                host.Children.Remove(field);
        }
    }

    /// <summary>
    /// What a state colours, in the live tree. StateBrush asks the palette for the brush of the shift the element
    /// is shown in, which only a running WinUI can say - an element's theme is the window's until it joins the
    /// tree, and the lookup depends on it. So a word, a fill and a wash are built here in this shift and asked
    /// what they came out as, against the palette's own brushes for the shift.
    /// </summary>
    private async Task CheckStateBrushAsync(Panel host, ElementTheme theme, string shift)
    {
        Progress($"{shift} specimen: colours a state gives a word, a fill and a wash");

        var word = new TextBlock { Text = "HOT", Opacity = 0 };
        var fill = new Border { Width = 8, Height = 8, Opacity = 0 };
        var wash = new Border { Width = 8, Height = 8, Opacity = 0 };
        var unwashed = new Border { Width = 8, Height = 8, Opacity = 0 };
        StateBrush.SetForeground(word, LampState.Warn);
        StateBrush.SetBackground(fill, LampState.Go);
        StateBrush.SetWash(wash, LampState.Armed);
        StateBrush.SetWash(unwashed, LampState.Off);

        var parts = new FrameworkElement[] { word, fill, wash, unwashed };
        foreach (var part in parts)
            host.Children.Add(part);

        try
        {
            await RenderedAsync();
            await RenderedAsync();

            var problems = 0;
            void Expect(string what, Brush? drawn, string token)
            {
                var wanted = Describe(PaletteBrush(theme, token));
                if (wanted is null || Describe(drawn) != wanted)
                {
                    problems++;
                    Problem($"In the {shift} shift {what} came out {Describe(drawn) ?? "uncoloured"}, where the " +
                            $"palette's {token} is {wanted ?? "missing"}");
                }
            }

            Expect("a Warn word", word.Foreground, "StateWarnBrush");
            Expect("a Go fill", fill.Background, "RailGoBrush");
            Expect("an Armed wash", wash.Background, "ArmedSoftBrush");

            if (unwashed.Background is SolidColorBrush { Color.A: > 0 })
            {
                problems++;
                Problem($"In the {shift} shift an Off wash drew {Describe(unwashed.Background)}; Off has no wash");
            }

            if (problems == 0)
                Checked($"The state colours in the {shift} shift: a word, a fill and a wash each took the palette's " +
                        "brush for that shift, and Off washed nothing");
        }
        catch (Exception ex)
        {
            Problem($"The state colours could not be measured in the {shift} shift: {ex.Message}");
        }
        finally
        {
            foreach (var part in parts)
                host.Children.Remove(part);
        }
    }

    /// <summary>
    /// What a drag leaves behind. A drag is a pointer gesture and there is no pointer here, but the part of it
    /// that outlives the gesture can be reproduced: ToggleSwitch moves to the Dragging state and then writes
    /// KnobTranslateTransform.X on every move, and that written value stays on the transform afterwards.
    /// <para>
    /// Which is the whole hazard. A state that does not say where the thumb rests inherits whatever the last
    /// drag wrote, so a lever that was dragged and then switched off leaves its thumb standing wherever the
    /// finger let go - on a switch that reads OFF. That is not hypothetical: it is what this found the first
    /// time it ran, and why the Off state now sets the transform instead of assuming it (Controls.xaml, the Off state).
    /// </para>
    /// <para>
    /// The thumb is measured where it is drawn against the track rather than by reading the transform back. A
    /// write to a property returns that value when it is read whether or not it is what gets drawn, so reading
    /// it back would pass no matter what the template did.
    /// </para>
    /// <para>
    /// This is still not a drag: no pointer reaches the Thumb, the gesture recogniser never runs, and nothing
    /// here clamps the distance to the travel - the geometry test is the nearest thing to that
    /// (ConsoleSwitchTests). It covers what a drag leaves on the transform, which is the part that persists.
    /// </para>
    /// </summary>
    private async Task<int> CheckSwitchDragAsync(ToggleSwitch lever, Grid knob, Border track, string shift)
    {
        if (knob.RenderTransform is not TranslateTransform transform)
        {
            Problem($"The switch in the {shift} shift has no translate transform on its knob, so nothing could " +
                    "move it and a drag has nothing to write to");
            return 1;
        }

        double Drawn() => knob.TransformToVisual(track).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

        var armed = Drawn();

        // A finger goes down on an armed lever and drags the thumb most of the way back.
        VisualStateManager.GoToState(lever, "Dragging", false);
        transform.X = 7;
        await RenderedAsync();

        // It is let go on the off side: the switch turns off, and the thumb has to come back to the left.
        lever.IsOn = false;
        await SettledAsync();

        var rested = Drawn();
        var travelled = armed - rested;

        if (Math.Abs(travelled - 20) > 1)
        {
            Problem($"After a drag, the switch in the {shift} shift turned off with its thumb {travelled:0.#} " +
                    $"back from where armed drew it, not the 20 it travels: armed {armed:0.#}, off " +
                    $"{rested:0.#}. The drag wrote 7 onto the transform and the off state left it there, so a " +
                    "lever that reads OFF is drawn part-way on");
            return 1;
        }

        // Left as the rest of the run expects to find it: on, and about to be turned off again.
        lever.IsOn = true;
        await SettledAsync();
        return 0;
    }

    /// <summary>A frame, time for a 160ms state to finish, and another frame.</summary>
    private static async Task SettledAsync()
    {
        await RenderedAsync();
        await Task.Delay(Settle);
        await RenderedAsync();
    }

    /// <summary>
    /// Which button a confirmation actually draws in the armed face. This is the one thing about a dialog that
    /// cannot be read from the code that builds it: WinUI's own template gives the dialog's default button
    /// <c>AccentButtonStyle</c> from a visual state when the dialog opens (generic.xaml, DefaultButtonStates),
    /// and Phase 1 made the accent armed red. So a confirmation whose default button is Cancel draws Cancel in
    /// armed red and the destructive button plain - the emphasis exactly backwards - and nothing anywhere says
    /// so. The dialog is opened here and both buttons are asked what face they came out with.
    /// </summary>
    private async Task CheckDialogsAsync(FrameworkElement owner, ElementTheme theme, string shift)
    {
        const string Destructive = "Wipe it";
        var step = $"{shift} specimen: which button a confirmation arms";
        Progress(step);

        var armed = Describe(PaletteBrush(theme, "ArmedCapFaceBrush"));
        var plain = Describe(PaletteBrush(theme, "CapFaceBrush"));
        if (armed is null || plain is null)
        {
            Problem($"The {shift} palette does not write {(armed is null ? "ArmedCapFaceBrush" : "CapFaceBrush")}, " +
                    "so the dialog cannot be checked");
            return;
        }

        var dialog = ConsoleDialog.Confirm(owner, "Specimen", "This dialog does nothing; the run opens it to " +
                                                              "see which of its buttons is armed.", Destructive);
        var showing = dialog.ShowAsync();
        try
        {
            await RenderedAsync();
            await Task.Delay(Settle);
            await RenderedAsync();

            var primary = Named<Button>(dialog, "PrimaryButton");
            var close = Named<Button>(dialog, "CloseButton");
            if (primary is null || close is null)
            {
                Problem($"The confirmation in the {shift} shift has no {(primary is null ? "primary" : "close")} " +
                        "button to read, so the run cannot tell which one is armed");
                return;
            }

            var problems = 0;
            if (Describe(primary.Background) != armed)
            {
                problems++;
                Problem($"In the {shift} shift the confirmation draws \"{Destructive}\" {Describe(primary.Background)}, " +
                        $"where the armed cap is {armed}: the button that does the thing is not the armed one");
            }

            // Cancel has to be the plain cap exactly, not merely "not the armed cap": the face WinUI's accent
            // state hands it is a solid armed red, which is not the armed cap's gradient and would slip past a
            // check that only asked whether the two matched.
            if (Describe(close.Background) != plain)
            {
                problems++;
                Problem($"In the {shift} shift the confirmation draws Cancel {Describe(close.Background)}, where the " +
                        $"plain cap is {plain}: Cancel is emphasised, and on a confirmation that reads backwards");
            }

            if (problems == 0)
                Checked($"The confirmation in the {shift} shift arms \"{Destructive}\" and draws Cancel plain");
        }
        catch (Exception ex)
        {
            Problem($"Opening the confirmation in the {shift} shift threw: {ex.Message}");
        }
        finally
        {
            // A dialog left open holds the run until the watchdog.
            dialog.Hide();
            try
            {
                await showing;
            }
            catch (Exception ex)
            {
                Note($"Closing the confirmation in the {shift} shift threw: {ex.Message}");
            }

            await RenderedAsync();
        }
    }

    /// <summary>A brush as it reads in a report: a colour, or a gradient's stops in order.</summary>
    private static string? Describe(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.ToString(),
        GradientBrush gradient => string.Join(" ", gradient.GradientStops.Select(stop => $"{stop.Offset:0.##}:{stop.Color}")),
        null => null,
        _ => brush.GetType().Name,
    };

    /// <summary>A brush as Styles/Console/Tokens.xaml writes it for a shift.</summary>
    private static Brush? PaletteBrush(ElementTheme shift, string key)
    {
        var tokens = OwnDictionaries(Application.Current.Resources).FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Styles/Console/Tokens.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var themeKey = shift == ElementTheme.Light ? "Light" : "Default";
        if (tokens is null || !tokens.ThemeDictionaries.TryGetValue(themeKey, out var theme) ||
            theme is not ResourceDictionary brushes || !brushes.TryGetValue(key, out var brush))
            return null;

        return brush as Brush;
    }

    /// <summary>
    /// Whether the faceplate's shadows reach the screen. Composition draws them outside the XAML tree, so the tree
    /// can say they were built and only a picture can say they show: a faceplate is shown alone on Day Shift's
    /// chassis, and the chassis 6 below its foot has to be darker than the chassis well clear of it, as the drop
    /// and ambient shadows darken it there (styles.css :1071-1074).
    /// </summary>
    private async Task CheckShadowPixelsAsync(MainWindow window, ScrollViewer scroller, string shift)
    {
        var step = $"{shift} specimen: a faceplate's shadows on the chassis";
        if (PaletteColour(ElementTheme.Light, "Carbon950Color") is not { } chassis)
        {
            Problem("Styles/Console/Tokens.xaml has no Carbon950Color to lay the shadow probe on");
            return;
        }

        var faceplate = new Faceplate
        {
            Kicker = "PROBE", Width = 360, Height = 120, Margin = new Thickness(60, 40, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var ground = new Grid { Height = 420, Background = new SolidColorBrush(chassis), Children = { faceplate } };
        await ShowAsync(scroller, step, ground);
        await Task.Delay(Settle);
        await RenderedAsync();

        using var picture = CaptureBitmap(window);
        var under = Brightness(window, picture, faceplate, faceplate.ActualWidth / 2, faceplate.ActualHeight + 6);
        var clear = Brightness(window, picture, faceplate, faceplate.ActualWidth / 2, faceplate.ActualHeight + 220);
        if (under is null || clear is null)
            Problem($"{step}: the probe was outside the picture");
        else if (clear - under < 6)
            Problem($"{step}: the chassis under a faceplate is {under:0} and {clear:0} clear of it, " +
                    "so its shadows do not show");
        else
            Checked($"A faceplate's shadows darken Day Shift's chassis under it: {under:0} against {clear:0} clear of it");
    }

    /// <summary>
    /// The brightness, 0 to 255, of the picture's pixel at a point in an element, or null when the point is not in
    /// the picture. The picture is the whole window; the element's point is taken to the window's client area and
    /// scaled to its pixels.
    /// </summary>
    private static double? Brightness(MainWindow window, Drawing.Bitmap picture, FrameworkElement element,
        double x, double y)
    {
        if (window.Content is not UIElement root || element.XamlRoot is not { } xamlRoot)
            return null;

        var handle = WindowNative.GetWindowHandle(window);
        var origin = new NativePoint();
        if (!GetWindowRect(handle, out var bounds) || !ClientToScreen(handle, ref origin))
            return null;

        var point = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(x, y));
        var scale = xamlRoot.RasterizationScale;
        var column = (int)Math.Round(origin.X - bounds.Left + point.X * scale);
        var row = (int)Math.Round(origin.Y - bounds.Top + point.Y * scale);
        if (column < 0 || row < 0 || column >= picture.Width || row >= picture.Height)
            return null;

        var pixel = picture.GetPixel(column, row);
        return 0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;
    }

    /// <summary>
    /// Why the specimen's text did not follow the shift, measured rather than guessed, as one line of the report.
    /// Each reading names the same palette a different way, so the one that fails says where: the probe, whose
    /// colour its text style sets; the faceplate's own foreground, from its implicit style; the swatch beside the
    /// probe, which names Silver on the element itself; the first stop of the faceplate's face, which its template
    /// names; and a stock WinUI button, whose colours are WinUI's own. When only the last followed the shift, the
    /// palette's brushes were not taking their colours from the element's theme, as when they named their colours
    /// with {StaticResource} instead of writing them out (ConsoleDictionaryTests).
    /// </summary>
    private async Task DescribeShiftAsync(ConsoleSpecimen specimen, ElementTheme theme, string shift)
    {
        static string Colour(Brush? brush) =>
            (brush as SolidColorBrush)?.Color.ToString() ?? brush?.GetType().Name ?? "nothing";

        var probe = specimen.OnFace;
        var faceplate = Descendants(specimen).OfType<Faceplate>().FirstOrDefault(plate => IsShown(plate, specimen));
        var face = faceplate is null ? null : Descendants(faceplate).OfType<Border>()
            .Select(border => border.Background).OfType<LinearGradientBrush>().FirstOrDefault();

        var button = new Button { Content = "PROBE" };
        var host = specimen.Content as Panel;
        host?.Children.Add(button);
        await RenderedAsync();
        await RenderedAsync();
        var buttonColour = Colour(button.Foreground);
        host?.Children.Remove(button);

        Problem($"Why, measured in the {shift} shift: the probe is in the {probe.ActualTheme} theme with silver " +
                $"{Colour(probe.Foreground)}; the faceplate's own foreground, from its style, is " +
                $"{Colour(faceplate?.Foreground)} (the shift's platinum is {PaletteColour(theme, "PlatinumColor")}); the " +
                $"swatch beside the probe, Silver named on the element, is {Colour(specimen.Swatch.Background)}; the " +
                $"faceplate's face, from its template, starts at " +
                $"{face?.GradientStops.FirstOrDefault()?.Color.ToString() ?? "nothing"} (the shift's plate is " +
                $"{PaletteColour(theme, "Plate1Color")}); a stock button's text, in WinUI's own colours, is {buttonColour}");
    }

    /// <summary>Whether <paramref name="element"/> is drawn somewhere inside <paramref name="ancestor"/>.</summary>
    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>The element of a type and name among a control's visual descendants: a part of its template.</summary>
    private static T? Named<T>(DependencyObject parent, string name) where T : FrameworkElement =>
        Descendants(parent).OfType<T>().FirstOrDefault(element => element.Name == name);

    /// <summary>Whether an element and every ancestor up to <paramref name="root"/> are visible.</summary>
    private static bool IsShown(DependencyObject element, DependencyObject root)
    {
        for (var current = element; current is not null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
                return false;
        }

        return true;
    }

    /// <summary>
    /// Text in a console style held in each kind of host: plain grids in the other theme and in the same one, then
    /// the surfaces that set their own theme, which changes how the styles inside them find their colours, and
    /// then a faceplate, which does not, one part at a time and then whole. A faceplate holding a body with its
    /// stripe's slot empty is the case that once gave the body two parents (Styles/Console/Instruments.xaml).
    /// </summary>
    private static IEnumerable<(string Step, UIElement Host)> TextHosts()
    {
        var styles = ConsoleTextStyles().ToDictionary(pair => pair.Key.ToString() ?? string.Empty, pair => pair.Style);
        TextBlock Text(string style) => new() { Text = FontProbeText, Style = styles.GetValueOrDefault(style) };

        yield return ("a grid in the light theme holding DescriptionTextStyle",
            new Grid { RequestedTheme = ElementTheme.Light, Children = { Text("DescriptionTextStyle") } });
        yield return ("a grid in the dark theme holding DescriptionTextStyle",
            new Grid { RequestedTheme = ElementTheme.Dark, Children = { Text("DescriptionTextStyle") } });
        yield return ("a well holding DescriptionTextStyle", new Well { Content = Text("DescriptionTextStyle") });
        yield return ("a well holding LcdValueTextStyle", new Well { Content = Text("LcdValueTextStyle") });
        yield return ("a display in the go tone holding StreamTextStyle",
            new Display { Tone = DisplayTone.Go, Content = Text("StreamTextStyle") });
        yield return ("a faceplate with a kicker", new Faceplate { Kicker = "PROBE" });
        yield return ("a faceplate with a serial", new Faceplate { Serial = "S/N STX-0000-00" });
        yield return ("a faceplate holding BodyTextStyle", new Faceplate { Content = Text("BodyTextStyle") });
        yield return ("a faceplate with a kicker and a serial, holding BodyTextStyle",
            new Faceplate { Kicker = "PROBE", Serial = "S/N STX-0000-00", Content = Text("BodyTextStyle") });
        yield return ("a faceplate with something in its stripe, holding BodyTextStyle",
            new Faceplate { Kicker = "PROBE", StripeRight = Text("SerialTextStyle"), Content = Text("BodyTextStyle") });
    }

    /// <summary>
    /// Each panel with every slot it has filled: content, a stripe's status, actions, a cancel cap, an action. A
    /// panel that builds a faceplate into its template hands it what it was given, so each of these puts an element
    /// through two templates; an element given two parents on the way ends the process from inside layout.
    /// </summary>
    private static IEnumerable<(string Step, UIElement Panel)> FilledPanels()
    {
        var styles = ConsoleTextStyles().ToDictionary(pair => pair.Key.ToString() ?? string.Empty, pair => pair.Style);
        TextBlock Text(string style) => new() { Text = FontProbeText, Style = styles.GetValueOrDefault(style) };
        var idle = new RelayCommand(() => { });

        yield return ("a warn plate holding BodyTextStyle",
            new Plate { State = LampState.Warn, IsPressable = true, Content = Text("BodyTextStyle") });
        yield return ("a dismissible banner holding a line of text",
            new Banner { IsDismissible = true, Content = "PROBE" });
        yield return ("a banner holding DescriptionTextStyle",
            new Banner { State = LampState.Exec, Content = Text("DescriptionTextStyle") });
        yield return ("a view header with a status and actions",
            new ViewHeader
            {
                Module = "PROBE", Title = "Probe", Description = FontProbeText,
                Status = new Lamp { State = LampState.Go, Code = "GO", Size = LampSize.Small },
                Actions = new Button { Content = "PROBE" },
            });
        yield return ("a busy well", new BusyWell { Title = FontProbeText });
        yield return ("a busy panel that knows its extent, with a readout and a cancel cap",
            new BusyPanel { Kicker = "PROBE", Title = "Probe", Detail = FontProbeText, Value = 42.5, Readout = "PROBE", CancelCommand = idle });
        yield return ("a busy panel that does not know its extent, with a readout",
            new BusyPanel { Kicker = "PROBE", Title = "Probe", Readout = "PROBE" });
        yield return ("a standby panel with an action",
            new StandbyPanel
            {
                Kicker = "PROBE", Title = "Probe", Description = FontProbeText, Glyph = "\uE721",
                ActionText = "PROBE", ActionCommand = idle,
            });
    }

    /// <summary>Every console instrument: the public controls in <see cref="Faceplate"/>'s namespace.</summary>
    private static IEnumerable<Type> Instruments() =>
        typeof(Faceplate).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(Faceplate).Namespace && type.IsPublic && !type.IsAbstract &&
                           typeof(Control).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>Where the run is, written before the step, so a run that stops there says so.</summary>
    private void Progress(string step) => File.WriteAllText(Path.Combine(Folder, "progress.txt"), step);

    /// <summary>A colour as Styles/Console/Tokens.xaml writes it for a shift: Night Ops for Dark, Day Shift for Light.</summary>
    private static Windows.UI.Color? PaletteColour(ElementTheme shift, string key)
    {
        var tokens = OwnDictionaries(Application.Current.Resources).FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Styles/Console/Tokens.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var themeKey = shift == ElementTheme.Light ? "Light" : "Default";
        if (tokens is null || !tokens.ThemeDictionaries.TryGetValue(themeKey, out var theme) ||
            theme is not ResourceDictionary colours || !colours.TryGetValue(key, out var colour))
            return null;

        return colour as Windows.UI.Color?;
    }

    /// <summary>An element's visual descendants, depth first.</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// A face that fails to load does not throw: the text is quietly set in the system's fallback font and the page
    /// looks nearly right. So each console face sets the same line as a font that does not exist - which is
    /// exactly what a broken reference falls back to, on any machine - and one that comes out as wide never loaded.
    /// </summary>
    private void CheckFonts(FrameworkElement root)
    {
        if (root is not Panel panel)
        {
            Problem("The fonts could not be checked: the window's root is not a panel");
            return;
        }

        var fallback = MeasureWidth(panel, new FontFamily("ms-appx:///Assets/Fonts/NotAFont.ttf#Not A Font"));
        foreach (var key in ConsoleFaces)
        {
            FontFamily face;
            try
            {
                face = (FontFamily)Application.Current.Resources[key];
            }
            catch (Exception ex)
            {
                Problem($"The font resource {key} could not be found: {ex.Message}");
                continue;
            }

            if (Math.Abs(MeasureWidth(panel, face) - fallback) < 0.5)
                Problem($"{key} ({face.Source}) did not load: it sets text exactly as wide as the fallback font does");
        }

        Checked($"{ConsoleFaces.Length} console faces measured against a font that does not exist");
    }

    /// <summary>
    /// Builds every resource in every theme of the app's own theme dictionaries: Night Ops, Day Shift and High
    /// Contrast alike, whichever one this machine shows. A theme's resource is only built the first time something
    /// shown in that theme asks for it, so one that cannot be built - a StaticResource its theme does not hold, a
    /// value that does not parse - would otherwise wait for the first page to use it, in the first theme to show it.
    /// WinUI's own dictionaries (XamlControlsResources) are its business, and are left out.
    /// </summary>
    private void CheckThemeResources()
    {
        var themes = 0;
        var built = 0;
        foreach (var dictionary in OwnDictionaries(Application.Current.Resources))
        {
            var name = dictionary.Source?.OriginalString ?? "App.xaml";
            foreach (var (theme, content) in dictionary.ThemeDictionaries)
            {
                themes++;
                if (content is not ResourceDictionary resources)
                {
                    Problem($"{name}: the {theme} theme is a {content?.GetType().Name ?? "null"}, not a resource dictionary");
                    continue;
                }

                foreach (var key in resources.Keys.ToList())
                {
                    try
                    {
                        _ = resources[key];
                        built++;
                    }
                    catch (Exception ex)
                    {
                        Problem($"{name}: {key} could not be built in the {theme} theme: {ex.Message}");
                    }
                }
            }
        }

        if (themes == 0)
            Problem("The app's resources hold no theme dictionaries to check");
        else
            Checked($"{built} theme resources built across {themes} themes");
    }

    /// <summary>
    /// The Fluent overrides only work if WinUI's own brushes pick them up, and an override WinUI ignores raises
    /// nothing. WinUI's accent fill reads the accent ramp through ThemeResource, and the accent button's
    /// foreground is one of the control keys replaced outright. The application's theme is the dark one, so
    /// each has to come out as the overrides' Night Ops values say.
    /// </summary>
    private void CheckFluentOverrides()
    {
        var overrides = OwnDictionaries(Application.Current.Resources).FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Styles/Console/FluentOverrides.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (overrides is null)
        {
            Problem("Styles/Console/FluentOverrides.xaml is not merged into the application's resources");
            return;
        }

        var night = (ResourceDictionary)overrides.ThemeDictionaries["Default"];
        var reached = Reaches("AccentFillColorDefaultBrush", (Windows.UI.Color)overrides["SystemAccentColorLight2"])
                    & Reaches("AccentButtonForeground", ((SolidColorBrush)night["AccentButtonForeground"]).Color);

        if (reached)
            Checked("The accent ramp and the control overrides reach WinUI's own brushes");

        bool Reaches(string key, Windows.UI.Color expected)
        {
            var actual = (Application.Current.Resources[key] as SolidColorBrush)?.Color;
            if (actual == expected)
                return true;

            Problem($"{key} is {actual?.ToString() ?? "not a solid brush"} where the Fluent overrides say {expected}: WinUI did not pick them up");
            return false;
        }
    }

    /// <summary>
    /// Applies every console text style to a text block in the live tree and measures it. A style is only
    /// resolved when something uses it - its font, its theme colour, each setter's value - so one that cannot
    /// be would otherwise wait for the first page that asks for it.
    /// </summary>
    private void CheckTextStyles(FrameworkElement root)
    {
        var styles = ConsoleTextStyles().ToList();
        if (styles.Count == 0 || root is not Panel panel)
        {
            Problem("The console text styles could not be checked: Styles/Console/Typography.xaml is not merged or holds none, " +
                    "or the root is not a panel");
            return;
        }

        var applied = 0;
        foreach (var (key, style) in styles)
        {
            var sample = new TextBlock { Text = FontProbeText, Opacity = 0, IsHitTestVisible = false };
            try
            {
                sample.Style = style;
                panel.Children.Add(sample);
                sample.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                applied++;
            }
            catch (Exception ex)
            {
                Problem($"The text style {key} could not be applied: {ex.Message}");
            }
            finally
            {
                panel.Children.Remove(sample);
            }
        }

        if (applied > 0)
            Checked($"{applied} console text styles applied");
    }

    /// <summary>The text styles in Styles/Console/Typography.xaml, by key.</summary>
    private static IEnumerable<(object Key, Style Style)> ConsoleTextStyles()
    {
        var typography = OwnDictionaries(Application.Current.Resources).FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Styles/Console/Typography.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (typography is null)
            yield break;

        foreach (var (key, value) in typography)
        {
            if (value is Style style)
                yield return (key, style);
        }
    }

    /// <summary>A dictionary and everything merged into it, except WinUI's own.</summary>
    private static IEnumerable<ResourceDictionary> OwnDictionaries(ResourceDictionary dictionary)
    {
        if (dictionary is XamlControlsResources)
            yield break;

        yield return dictionary;
        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var inner in OwnDictionaries(merged))
                yield return inner;
        }
    }

    /// <summary>How wide a face sets the probe line, measured in the live tree where fonts load.</summary>
    private static double MeasureWidth(Panel panel, FontFamily face)
    {
        var probe = new TextBlock { Text = FontProbeText, FontFamily = face, FontSize = 20, Opacity = 0, IsHitTestVisible = false };
        panel.Children.Add(probe);
        try
        {
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
        finally
        {
            panel.Children.Remove(probe);
        }
    }

    private static async Task<bool> LoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded)
            return true;

        var loaded = new TaskCompletionSource();
        void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
        element.Loaded += OnLoaded;
        try
        {
            return await Task.WhenAny(loaded.Task, Task.Delay(LoadTimeout)) == loaded.Task;
        }
        finally
        {
            element.Loaded -= OnLoaded;
        }
    }

    /// <summary>Completes on the next frame the compositor draws.</summary>
    private static Task RenderedAsync()
    {
        var rendered = new TaskCompletionSource();

        void OnRendering(object? sender, object args)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }

        CompositionTarget.Rendering += OnRendering;
        return rendered.Task;
    }

    /// <summary>
    /// A picture of the whole window. PrintWindow with PW_RENDERFULLCONTENT includes what the compositor draws -
    /// shadows, glows, radial brushes - which a XAML RenderTargetBitmap leaves out.
    /// </summary>
    private static void Capture(MainWindow window, string path)
    {
        using var bitmap = CaptureBitmap(window);
        bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>The whole window as it is drawn, composition included, which the caller disposes of.</summary>
    private static Drawing.Bitmap CaptureBitmap(MainWindow window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        if (!GetWindowRect(handle, out var bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            throw new InvalidOperationException("The window has no size to capture");

        var bitmap = new Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        try
        {
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            var context = graphics.GetHdc();
            try
            {
                if (!PrintWindow(handle, context, PrintWindowRenderFullContent))
                    throw new InvalidOperationException($"PrintWindow failed ({Marshal.GetLastWin32Error()})");
            }
            finally
            {
                graphics.ReleaseHdc(context);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void Problem(string text)
    {
        Log.Error("UI smoke: {Problem}", text);
        lock (_gate)
            (_current?.Problems ?? _problems).Add(text);
    }

    /// <summary>A check that ran, so the report says what was checked and not only what failed.</summary>
    private void Checked(string text)
    {
        lock (_gate)
            _checks.Add(text);
    }

    private void Note(string text)
    {
        lock (_gate)
            (_current?.Notes ?? _notes).Add(text);
    }

    private bool Passed()
    {
        lock (_gate)
            return _problems.Count == 0 && _pages.All(page => page.Problems.Count == 0);
    }

    private void WriteReport(bool finished)
    {
        string json;
        lock (_gate)
        {
            json = JsonSerializer.Serialize(new
            {
                passed = _problems.Count == 0 && _pages.All(page => page.Problems.Count == 0),
                finished,
                startedUtc = _startedUtc,
                writtenUtc = DateTime.UtcNow,
                checks = _checks,
                problems = _problems,
                notes = _notes,
                pages = _pages,
            }, ReportOptions);
        }

        var report = Path.Combine(Folder, "report.json");
        File.WriteAllText(report + ".tmp", json);
        File.Move(report + ".tmp", report, overwrite: true);
    }

    private void Finish()
    {
        _watchdog?.Dispose();
        WriteReport(finished: true);

        var passed = Passed();
        Log.Information("UI smoke run {Outcome}: {Pages} pages", passed ? "passed" : "failed", _pages.Count);
        Log.CloseAndFlush();
        Environment.Exit(passed ? 0 : 1);
    }

    private void GiveUp()
    {
        Problem($"The run did not finish within {Watchdog.TotalMinutes:0} minutes");
        WriteReport(finished: false);
        Log.CloseAndFlush();
        Environment.Exit(2);
    }

    /// <summary>One page in one theme, as the report shows it.</summary>
    private sealed class PageResult
    {
        public string Shift { get; init; } = "";

        public string Page { get; init; } = "";

        public string Type { get; init; } = "";

        public bool Loaded { get; set; }

        public long Milliseconds { get; set; }

        public string? Screenshot { get; set; }

        public List<string> Problems { get; } = new();

        public List<string> Notes { get; } = new();
    }

    private const uint PrintWindowRenderFullContent = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
}
