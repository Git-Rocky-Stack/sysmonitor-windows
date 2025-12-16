using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using SysMonitor.App.ViewModels;
using SysMonitor.App.Views;
using SysMonitor.Core.Services;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Core.Services.Backup;

namespace SysMonitor.App;

public partial class App : Application
{
    private static Window? _mainWindow;
    private static IHost? _host;
    private static bool _isElevatedMode = false;

    public static Window? MainWindow => _mainWindow;

    public App()
    {
        // Check for elevated registry cleaning mode BEFORE InitializeComponent
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 4 && args[1] == "--fix-registry")
        {
            _isElevatedMode = true;
            // Run elevated registry cleaning and exit
            var inputFile = args[2];
            var outputFile = args[3];

            Task.Run(async () =>
            {
                var exitCode = await ElevatedRegistryHelper.ExecuteElevatedClean(inputFile, outputFile);
                Environment.Exit(exitCode);
            }).GetAwaiter().GetResult();

            return; // Don't initialize the rest of the app
        }

        InitializeComponent();
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core Services - Monitors
                services.AddSingleton<ICpuMonitor, CpuMonitor>();
                services.AddSingleton<IMemoryMonitor, MemoryMonitor>();
                services.AddSingleton<IDiskMonitor, DiskMonitor>();
                services.AddSingleton<IBatteryMonitor, BatteryMonitor>();
                services.AddSingleton<INetworkMonitor, NetworkMonitor>();
                services.AddSingleton<IProcessMonitor, ProcessMonitor>();
                services.AddSingleton<ITemperatureMonitor, TemperatureMonitor>();

                // Core Services - Main
                services.AddSingleton<ISystemInfoService, SystemInfoService>();

                // Core Services - Cleaners
                services.AddSingleton<ITempFileCleaner, TempFileCleaner>();
                services.AddSingleton<IBrowserCacheCleaner, BrowserCacheCleaner>();
                services.AddSingleton<IRegistryCleaner, RegistryCleaner>();
                services.AddSingleton<IBrowserPrivacyCleaner, BrowserPrivacyCleaner>();

                // Core Services - Optimizers
                services.AddSingleton<IStartupOptimizer, StartupOptimizer>();
                services.AddSingleton<IMemoryOptimizer, MemoryOptimizer>();

                // Core Services - Utilities
                services.AddSingleton<ILargeFileFinder, LargeFileFinder>();
                services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
                services.AddSingleton<IFileConverter, FileConverter>();
                services.AddSingleton<IBluetoothAnalyzer, BluetoothAnalyzer>();
                services.AddSingleton<IWiFiAnalyzer, WiFiAnalyzer>();
                services.AddSingleton<IPdfTools, PdfTools>();
                services.AddSingleton<IPdfEditor, PdfEditor>();
                services.AddSingleton<INetworkMapper, NetworkMapper>();
                services.AddSingleton<IImageTools, ImageTools>();
                services.AddSingleton<IInstalledProgramsService, InstalledProgramsService>();
                services.AddSingleton<IDriveWiper, DriveWiper>();
                services.AddSingleton<IHealthCheckService, HealthCheckService>();
                services.AddSingleton<ISystemRestoreService, SystemRestoreService>();
                services.AddSingleton<IScheduledCleaningService, ScheduledCleaningService>();
                services.AddSingleton<IBackupService, BackupService>();
                services.AddSingleton<IDriverUpdater, DriverUpdater>();

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProcessesViewModel>();
                services.AddTransient<CleanerViewModel>();
                services.AddTransient<RegistryCleanerViewModel>();
                services.AddTransient<StartupViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<CpuViewModel>();
                services.AddTransient<MemoryViewModel>();
                services.AddTransient<NetworkViewModel>();
                services.AddTransient<BatteryViewModel>();
                services.AddTransient<DiskViewModel>();
                services.AddTransient<TemperatureViewModel>();
                services.AddTransient<SystemInfoViewModel>();
                services.AddTransient<GpuViewModel>();
                services.AddTransient<LargeFilesViewModel>();
                services.AddTransient<DuplicateFinderViewModel>();
                services.AddTransient<FileToolsViewModel>();
                services.AddTransient<BluetoothViewModel>();
                services.AddTransient<WiFiViewModel>();
                services.AddTransient<PdfToolsViewModel>();
                services.AddTransient<PdfEditorViewModel>();
                services.AddTransient<NetworkMapperViewModel>();
                services.AddTransient<ImageToolsViewModel>();
                services.AddTransient<InstalledProgramsViewModel>();
                services.AddTransient<HealthCheckViewModel>();
                services.AddTransient<BrowserPrivacyViewModel>();
                services.AddTransient<DriveWiperViewModel>();
                services.AddTransient<ScheduledCleaningViewModel>();
                services.AddTransient<BackupViewModel>();
                services.AddTransient<DriverUpdaterViewModel>();

                // Views
                services.AddTransient<DashboardPage>();
                services.AddTransient<ProcessesPage>();
                services.AddTransient<CleanerPage>();
                services.AddTransient<RegistryCleanerPage>();
                services.AddTransient<StartupPage>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<CpuPage>();
                services.AddTransient<MemoryPage>();
                services.AddTransient<NetworkPage>();
                services.AddTransient<BatteryPage>();
                services.AddTransient<DiskPage>();
                services.AddTransient<TemperaturePage>();
                services.AddTransient<SystemInfoPage>();
                services.AddTransient<GpuPage>();
                services.AddTransient<LargeFilesPage>();
                services.AddTransient<DuplicateFinderPage>();
                services.AddTransient<FileToolsPage>();
                services.AddTransient<BluetoothPage>();
                services.AddTransient<WiFiPage>();
                services.AddTransient<PdfToolsPage>();
                services.AddTransient<PdfEditorPage>();
                services.AddTransient<NetworkMapperPage>();
                services.AddTransient<ImageToolsPage>();
                services.AddTransient<InstalledProgramsPage>();
                services.AddTransient<HealthCheckPage>();
                services.AddTransient<BrowserPrivacyPage>();
                services.AddTransient<DriveWiperPage>();
                services.AddTransient<ScheduledCleaningPage>();
                services.AddTransient<BackupPage>();
                services.AddTransient<DriverUpdaterPage>();
            })
            .Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    public static T GetService<T>() where T : class
    {
        if (_host?.Services.GetService(typeof(T)) is not T service)
        {
            throw new InvalidOperationException($"Service {typeof(T).Name} not found.");
        }
        return service;
    }
}
