using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.Views;

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
        { "Settings", typeof(SettingsPage) },
        { "Donate", typeof(DonationPage) }
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = "SysMonitor - Windows System Monitor & Optimizer";

        // Navigate to dashboard on startup
        ContentFrame.Navigate(typeof(DashboardPage));
        NavView.SelectedItem = NavView.MenuItems[0];
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
