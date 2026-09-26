using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using WinRT.Interop;
using Drawing = System.Drawing;

namespace SysMonitor.App.Diagnostics;

/// <summary>
/// Opens every page in both themes, takes a picture of each, and fails on the errors that only appear when XAML
/// is loaded: a parse error, a navigation that fails, a resource that does not resolve.
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

            foreach (var (theme, name) in Shifts)
            {
                root.RequestedTheme = theme;
                await RenderedAsync();

                var index = 0;
                foreach (var (tag, pageType) in window.Pages)
                    await VisitAsync(window, name, ++index, tag, pageType);
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
        File.WriteAllText(Path.Combine(Folder, "progress.txt"), $"{shift} {index:00} {tag}");
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
