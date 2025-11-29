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

namespace SysMonitor.App;

public partial class App : Application
{
    private Window? _mainWindow;
    private static IHost? _host;

    public App()
    {
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

                // Core Services - Optimizers
                services.AddSingleton<IStartupOptimizer, StartupOptimizer>();
                services.AddSingleton<IMemoryOptimizer, MemoryOptimizer>();

                // Core Services - Utilities
                services.AddSingleton<ILargeFileFinder, LargeFileFinder>();
                services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
                services.AddSingleton<IFileConverter, FileConverter>();

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProcessesViewModel>();
                services.AddTransient<CleanerViewModel>();
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

                // Views
                services.AddTransient<DashboardPage>();
                services.AddTransient<ProcessesPage>();
                services.AddTransient<CleanerPage>();
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
