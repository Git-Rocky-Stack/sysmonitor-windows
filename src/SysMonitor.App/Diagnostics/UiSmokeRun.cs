using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Serilog;
using SysMonitor.App.Controls.Instruments;
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
    /// behind that trips the next. Then text inside each kind of host, a faceplate one part at a time, and then the
    /// whole specimen. Every step is laid out inside a <see cref="LayoutProbe"/>, so a template that fails in layout
    /// is reported against its step and the run goes on to the next one. A failure while rendering still ends the
    /// process, and then the step written last names what was on screen.
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
    /// Why the specimen's text did not follow the shift, measured rather than guessed, as one line of the report:
    /// the probe's own theme and colour; the colour the faceplate's style gave the faceplate itself, which was in
    /// the tree when the shift reached it, where the probe, inside the faceplate's body, was not yet; the same
    /// Silver named on an element rather than by a style, the swatch beside the probe; the first stop of the
    /// faceplate's face, named in its template; a stock WinUI button's text, which WinUI's own style colours; the
    /// same text style on a line made once the specimen was live; and the probe again once the specimen's own theme
    /// is set to the shift, which walks everything now in the tree.
    /// </summary>
    private async Task DescribeShiftAsync(ConsoleSpecimen specimen, ElementTheme theme, string shift)
    {
        static string Colour(Brush? brush) =>
            (brush as SolidColorBrush)?.Color.ToString() ?? brush?.GetType().Name ?? "nothing";

        var probe = specimen.OnFace;
        var probeTheme = probe.ActualTheme;
        var probeColour = Colour(probe.Foreground);
        var faceplate = Descendants(specimen).OfType<Faceplate>().FirstOrDefault(plate => IsShown(plate, specimen));

        var swatchColour = Colour(specimen.Swatch.Background);
        var face = faceplate is null ? null : Descendants(faceplate).OfType<Border>()
            .Select(border => border.Background).OfType<LinearGradientBrush>().FirstOrDefault();
        var faceColour = face?.GradientStops.FirstOrDefault()?.Color.ToString() ?? "nothing";

        var styles = ConsoleTextStyles().ToDictionary(pair => pair.Key.ToString() ?? string.Empty, pair => pair.Style);
        var late = new TextBlock { Text = FontProbeText, Style = styles.GetValueOrDefault("DescriptionTextStyle") };
        var button = new Button { Content = "PROBE" };
        var host = specimen.Content as Panel;
        host?.Children.Add(late);
        host?.Children.Add(button);
        await RenderedAsync();
        await RenderedAsync();
        var lateColour = Colour(late.Foreground);
        var buttonColour = Colour(button.Foreground);
        host?.Children.Remove(late);
        host?.Children.Remove(button);

        specimen.RequestedTheme = theme;
        await RenderedAsync();
        await RenderedAsync();

        Problem($"Why, measured in the {shift} shift: the probe is in the {probeTheme} theme with silver " +
                $"{probeColour}; the faceplate's own foreground, from its style, is {Colour(faceplate?.Foreground)} " +
                $"(the shift's platinum is {PaletteColour(theme, "PlatinumColor")}); the swatch beside the probe, Silver " +
                $"named on the element, is {swatchColour}; the faceplate's face, from its template, starts at " +
                $"{faceColour} (the shift's plate is {PaletteColour(theme, "Plate1Color")}); a stock button's text is " +
                $"{buttonColour}; a line in the same style made once the specimen was live is {lateColour}; the probe, " +
                $"once the specimen's own theme is set to the shift, is {Colour(probe.Foreground)}");
    }

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
        var handle = WindowNative.GetWindowHandle(window);
        if (!GetWindowRect(handle, out var bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            throw new InvalidOperationException("The window has no size to capture");

        using var bitmap = new Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
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
        }

        bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
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
}
