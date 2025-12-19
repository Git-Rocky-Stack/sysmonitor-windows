using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.Services;
using SysMonitor.App.Views;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.History;
using WinRT.Interop;

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

    public MainWindow()
    {
        InitializeComponent();
        Title = "SysMonitor - Windows System Monitor & Optimizer";

        // Set window icon
        SetWindowIcon();

        // Initialize tray icon and services
        InitializeTrayIcon();
        InitializeServicesAsync();

        // Handle window close to minimize to tray
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Closing += AppWindow_Closing;
        }

        // Navigate to dashboard on startup
        ContentFrame.Navigate(typeof(DashboardPage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private async void InitializeServicesAsync()
    {
        try
        {
            // Initialize history service (starts background recording)
            _historyService = App.GetService<IHistoryService>();
            await _historyService.InitializeAsync();
        }
        catch
        {
            // Services may fail to initialize - continue without them
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
        catch
        {
            // Tray icon may fail to initialize - continue without it
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

    private bool IsMinimizeToTrayEnabled()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SysMonitor", "settings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
                if (settings != null && settings.TryGetValue("MinimizeToTray", out var element))
                {
                    return element.GetBoolean();
                }
            }
        }
        catch { }
        return true; // Default to true
    }

    private void ShowWindow()
    {
        Activate();
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Show();
        }
    }

    private void HideWindow()
    {
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Hide();
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void NavigateToPage(string tag)
    {
        if (_pageMap.TryGetValue(tag, out var pageType))
        {
            ContentFrame.Navigate(pageType);

            // Try to select the nav item
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag?.ToString() == tag)
                {
                    NavView.SelectedItem = item;
                    break;
                }
            }
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
            if (tag != null && _pageMap.TryGetValue(tag, out var pageType))
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
