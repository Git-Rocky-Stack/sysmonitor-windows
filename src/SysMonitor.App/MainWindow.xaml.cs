using SysMonitor.App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.Services;
using SysMonitor.App.Views;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.History;
using SysMonitor.Core.Services.Settings;
using WinRT.Interop;
using Serilog;

namespace SysMonitor.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pageMap = new()
    {
        { "Dashboard", typeof(DashboardPage) },
        { "CpuMonitor", typeof(CpuPage) },
        { "MemoryMonitor", typeof(MemoryPage) },
        { "Processes", typeof(ProcessesPage) },
        { "DirectoryCleaner", typeof(CleanerPage) },
        { "RegistryCleaner", typeof(RegistryCleanerPage) },
        { "Startup", typeof(StartupPage) },
        { "InstalledPrograms", typeof(InstalledProgramsPage) },
        { "DiskAnalyzer", typeof(DiskPage) },
        { "Network", typeof(NetworkPage) },
        { "Battery", typeof(BatteryPage) },
        { "Temperature", typeof(TemperaturePage) },
        { "SystemInfo", typeof(SystemInfoPage) },
        { "GpuMonitor", typeof(GpuPage) },
        // Utilities
        { "LargeFiles", typeof(LargeFilesPage) },
        { "DuplicateFinder", typeof(DuplicateFinderPage) },
        { "FileTools", typeof(FileToolsPage) },
        { "PdfTools", typeof(PdfToolsPage) },
        { "PdfEditor", typeof(PdfEditorPage) },
        { "ImageTools", typeof(ImageToolsPage) },
        // Maintenance
        { "HealthCheck", typeof(HealthCheckPage) },
        { "GameMode", typeof(GameModePage) },
        { "BrowserPrivacy", typeof(BrowserPrivacyPage) },
        { "DriveWiper", typeof(DriveWiperPage) },
        { "ScheduledCleaning", typeof(ScheduledCleaningPage) },
        { "Backup", typeof(BackupPage) },
        { "DriverUpdater", typeof(DriverUpdaterPage) },
        // Wireless
        { "Bluetooth", typeof(BluetoothPage) },
        { "WiFi", typeof(WiFiPage) },
        { "NetworkMapper", typeof(NetworkMapperPage) },
        // Footer
        { "Performance", typeof(PerformancePage) },
        { "History", typeof(HistoryPage) },
        { "Settings", typeof(SettingsPage) },
        { "Donate", typeof(DonationPage) },
        { "UserGuide", typeof(UserGuidePage) }
    };

    private TrayIconService? _trayService;
    private IHistoryService? _historyService;
    private bool _isExiting;
    private WindowMinimumSize? _minimumSize;

    public MainWindow()
    {
        InitializeComponent();
        Title = "SysMonitor - Windows System Monitor & Optimizer";

        // Set window icon
        SetWindowIcon();

        // Small enough for a 1280-wide screen with room to spare; below it the pages' columns clip.
        _minimumSize = WindowMinimumSize.Apply(this, 1100, 700);

        // A smoke run only opens pages and takes pictures of them: no tray icon, no history recording, and no
        // hiding in the tray when it closes.
        if (App.SmokeRun is null)
        {
            // Initialize tray icon and services
            InitializeTrayIcon();
            InitializeServicesAsync();

            // Handle window close to minimize to tray
            var appWindow = GetAppWindow();
            if (appWindow != null)
            {
                appWindow.Closing += AppWindow_Closing;
            }
        }

        // The shift the user chose in Settings. The smoke run sets each shift itself, so it is left alone there.
        if (App.SmokeRun is null)
        {
            ApplyTheme(App.GetService<ISettingsStore>().Get("ThemeIndex", 2));
        }

        // The rail follows the frame, whoever navigated it: the rail itself, the tray menu, the smoke run, or
        // PDF Tools opening the editor.
        ContentFrame.Navigated += (_, e) => SelectRailItemFor(e.SourcePageType);

        // Navigate to dashboard on startup
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private async void InitializeServicesAsync()
    {
        try
        {
            // Initialize history service (starts background recording)
            _historyService = App.GetService<IHistoryService>();
            await _historyService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "A service failed to start; the window opens without it");
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _trayService = App.GetService<TrayIconService>();
            _trayService.Initialize();

            _trayService.ShowWindowRequested += (s, e) => ShowWindow();
            _trayService.ExitRequested += (s, e) => ExitApplication();
            _trayService.NavigateRequested += (s, tag) => NavigateToPage(tag);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The tray icon could not be created");
        }
    }

    private AppWindow? GetAppWindow()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // If we're actually exiting, let it close
        if (_isExiting)
        {
            // Cleanup
            _trayService?.Dispose();
            _historyService?.Dispose();
            return;
        }

        // Check if minimize to tray is enabled
        if (IsMinimizeToTrayEnabled())
        {
            args.Cancel = true;
            HideWindow();
        }
        else
        {
            // Cleanup and exit
            _trayService?.Dispose();
            _historyService?.Dispose();
        }
    }

    /// <summary>
    /// Read from the store the Settings page writes to. This used to read settings.json itself, which the
    /// packaged build never wrote, so in the Store build switching minimize-to-tray off did nothing.
    /// </summary>
    private static bool IsMinimizeToTrayEnabled() => App.GetService<ISettingsStore>().Get("MinimizeToTray", true);

    /// <summary>
    /// Shows the window in the shift Settings names: 0 follows Windows' own light or dark choice, 1 is Day Shift,
    /// anything else Night Ops. The setting used to be saved and never read, and App.xaml holds the application
    /// at Dark, so the app was Night Ops whatever was chosen. The theme is set on the window's content, which every
    /// page, dialog owner and well inside it inherits; the title bar keeps its own black, as System-X's does.
    /// </summary>
    internal void ApplyTheme(int themeIndex)
    {
        if (Content is not FrameworkElement root)
            return;

        root.RequestedTheme = themeIndex switch
        {
            0 => WindowsUsesDarkApps() ? ElementTheme.Dark : ElementTheme.Light,
            1 => ElementTheme.Light,
            _ => ElementTheme.Dark,
        };
    }

    /// <summary>Whether Windows is set to dark apps, read off the background colour it hands apps.</summary>
    private static bool WindowsUsesDarkApps()
    {
        try
        {
            var background = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R + background.G + background.B < 384;
        }
        catch (Exception ex)
        {
            // Best effort: a Windows that will not say is taken as dark, the app's own default.
            Log.Debug(ex, "Reading Windows' app theme failed");
            return true;
        }
    }

    /// <summary>Brings the window back from the tray or behind other windows - what a second launch asks for.</summary>
    internal void ShowWindow()
    {
        Activate();
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Show();
        }

        // Back to the page that was showing when the window went to the tray.
        if (_parkedPage is { } parked)
        {
            _parkedPage = null;
            ContentFrame.Navigate(parked);
        }
    }

    /// <summary>
    /// The pages that only watch: hidden in the tray they would go on sampling - Processes walks every process
    /// every three seconds, the CPU page every second - for a window nobody can see. They are left while the window
    /// is hidden and opened again when it is shown. A page doing work (a backup, a wipe, a scan) is never left,
    /// since leaving it ends the work.
    /// </summary>
    private static readonly HashSet<Type> WatchOnlyPages =
    [
        typeof(DashboardPage), typeof(CpuPage), typeof(GpuPage), typeof(MemoryPage), typeof(ProcessesPage),
        typeof(DiskPage), typeof(NetworkPage), typeof(BatteryPage), typeof(TemperaturePage),
        typeof(SystemInfoPage), typeof(PerformancePage),
    ];

    private Type? _parkedPage;

    private void HideWindow()
    {
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Hide();
        }

        if (ContentFrame.Content?.GetType() is { } showing && WatchOnlyPages.Contains(showing))
        {
            _parkedPage = showing;
            ContentFrame.Navigate(typeof(Page));
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;

        // Dispose tray icon first
        try
        {
            _trayService?.Dispose();
        }
        catch
        {
            // Best effort: the window is closing and the icon goes with the process either way.
        }

        // Close window
        Close();

        // Force exit the application
        Application.Current.Exit();
    }

    /// <summary>Every page the rail can open, by the tag its item carries. The smoke run opens each.</summary>
    internal IReadOnlyDictionary<string, Type> Pages => _pageMap;

    /// <summary>The frame the pages open in.</summary>
    internal Frame PageFrame => ContentFrame;

    internal void NavigateToPage(string tag)
    {
        // Navigating selects the rail's item (ContentFrame.Navigated). Selecting it here as well raised
        // SelectionChanged, which navigated a second time and disposed the first page part way through loading.
        if (_pageMap.TryGetValue(tag, out var pageType) && ContentFrame.Content?.GetType() != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    /// <summary>
    /// Selects the rail item for the page now showing, in the menu or the footer; a page with no item of its own,
    /// such as the PDF Editor, leaves nothing selected, so pressing the item it was opened from opens that again.
    /// </summary>
    private void SelectRailItemFor(Type pageType)
    {
        var tag = _pageMap.FirstOrDefault(entry => entry.Value == pageType).Key;
        var item = tag is null ? null : RailItems().FirstOrDefault(candidate => candidate.Tag?.ToString() == tag);

        if (!ReferenceEquals(NavView.SelectedItem, item))
        {
            NavView.SelectedItem = item;
        }
    }

    /// <summary>Every item on the rail, the footer's included, and those inside a group.</summary>
    private IEnumerable<NavigationViewItem> RailItems()
    {
        var pending = new Stack<object>(NavView.MenuItems.Concat(NavView.FooterMenuItems).Reverse());
        while (pending.Count > 0)
        {
            if (pending.Pop() is not NavigationViewItem item)
                continue;

            yield return item;
            foreach (var child in item.MenuItems.Reverse())
                pending.Push(child);
        }
    }

    private void SetWindowIcon()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Set the icon from the app.ico file
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            if (tag != null && _pageMap.TryGetValue(tag, out var pageType) && ContentFrame.Content?.GetType() != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
