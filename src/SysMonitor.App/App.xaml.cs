using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using SysMonitor.App.ViewModels;
using SysMonitor.App.Views;
using SysMonitor.Core.Services;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Core.Services.Monitoring;
using SysMonitor.Core.Services.History;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Core.Services.Settings;
using SysMonitor.App.Diagnostics;
using SysMonitor.App.Services;

namespace SysMonitor.App;

public partial class App : Application
{
    private static Window? _mainWindow;
    private static IHost? _host;
    private static UiSmokeRun? _smokeRun;

    public static Window? MainWindow => _mainWindow;

    /// <summary>The smoke run asked for with <c>--ui-smoke &lt;folder&gt;</c>, or null when started normally.</summary>
    internal static UiSmokeRun? SmokeRun => _smokeRun;

    public App()
    {
        // Check for elevated registry cleaning mode BEFORE InitializeComponent
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 4 && args[1] == "--fix-registry")
        {
            // Run elevated registry cleaning and exit. The list comes over the named pipe; the fingerprint is
            // what the unelevated side approved, and the elevated side refuses a list that does not match it.
            var pipeName = args[2];
            var fingerprint = args[3];

            Task.Run(async () =>
            {
                var exitCode = await ElevatedRegistryHelper.ExecuteElevatedClean(pipeName, fingerprint);
                Environment.Exit(exitCode);
            }).GetAwaiter().GetResult();

            return; // Don't initialize the rest of the app
        }

        // Windows Task Scheduler starts the app with these switches at the scheduled time. Clean and exit:
        // opening the whole app with highest privileges and cleaning nothing is what M4 was about.
        if (ScheduledCleaningRequest.FromCommandLine(args) is { } cleaningRequest)
        {
            Task.Run(async () =>
            {
                var exitCode = await RunScheduledCleanAsync(cleaningRequest);
                Environment.Exit(exitCode);
            }).GetAwaiter().GetResult();

            return; // Don't initialize the rest of the app
        }

        // CI opens every page with this and exits; see UiSmokeRun.
        _smokeRun = UiSmokeRun.FromCommandLine(args);

        InitializeComponent();

        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnAppUnhandledException;

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "Logs", "sysmonitor-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Add logging
                services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddSerilog(dispose: true);
                });

                // ============================================================
                // OPTIMIZATION: Lazy Singleton Registration
                // ============================================================
                // Services are registered with lazy initialization to defer
                // expensive constructor operations (WMI queries, PerformanceCounter
                // initialization, file system scanning) until first use.
                // This reduces startup time from 5+ seconds to <1 second.
                // ============================================================

                // Settings: one store every reader and writer shares, in the packaged build and the unpackaged one
                services.AddSingleton<SettingsStore>();
                services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsStore>());

                // Core Services - Monitors (Lazy: expensive PerformanceCounter/WMI init)
                // These services have expensive constructors that query WMI or create
                // PerformanceCounter objects. Using lazy initialization defers this cost.
                services.AddSingleton<CpuMonitor>();
                services.AddSingleton<ICpuMonitor>(sp => sp.GetRequiredService<CpuMonitor>());

                services.AddSingleton<MemoryMonitor>();
                services.AddSingleton<IMemoryMonitor>(sp => sp.GetRequiredService<MemoryMonitor>());

                services.AddSingleton<DiskMonitor>();
                services.AddSingleton<IDiskMonitor>(sp => sp.GetRequiredService<DiskMonitor>());

                services.AddSingleton<BatteryMonitor>();
                services.AddSingleton<IBatteryMonitor>(sp => sp.GetRequiredService<BatteryMonitor>());

                services.AddSingleton<NetworkMonitor>();
                services.AddSingleton<INetworkMonitor>(sp => sp.GetRequiredService<NetworkMonitor>());

                services.AddSingleton<ProcessMonitor>();
                services.AddSingleton<IProcessMonitor>(sp => sp.GetRequiredService<ProcessMonitor>());

                services.AddSingleton<TemperatureMonitor>();
                services.AddSingleton<ITemperatureMonitor>(sp => sp.GetRequiredService<TemperatureMonitor>());

                // Core Services - Main
                services.AddSingleton<SystemInfoService>();
                services.AddSingleton<ISystemInfoService>(sp => sp.GetRequiredService<SystemInfoService>());

                // Core Services - Cleaners (Lazy: file system path initialization)
                services.AddSingleton<TempFileCleaner>();
                services.AddSingleton<ITempFileCleaner>(sp => sp.GetRequiredService<TempFileCleaner>());

                services.AddSingleton<BrowserCacheCleaner>();
                services.AddSingleton<IBrowserCacheCleaner>(sp => sp.GetRequiredService<BrowserCacheCleaner>());

                services.AddSingleton<RegistryCleaner>();
                services.AddSingleton<IRegistryCleaner>(sp => sp.GetRequiredService<RegistryCleaner>());

                services.AddSingleton<BrowserPrivacyCleaner>();
                services.AddSingleton<IBrowserPrivacyCleaner>(sp => sp.GetRequiredService<BrowserPrivacyCleaner>());

                // Core Services - Optimizers
                services.AddSingleton<StartupOptimizer>();
                services.AddSingleton<IStartupOptimizer>(sp => sp.GetRequiredService<StartupOptimizer>());

                services.AddSingleton<MemoryOptimizer>();
                services.AddSingleton<IMemoryOptimizer>(sp => sp.GetRequiredService<MemoryOptimizer>());

                // Core Services - Utilities (Lazy: various expensive initializations)
                services.AddSingleton<LargeFileFinder>();
                services.AddSingleton<ILargeFileFinder>(sp => sp.GetRequiredService<LargeFileFinder>());

                services.AddSingleton<DuplicateFinder>();
                services.AddSingleton<IDuplicateFinder>(sp => sp.GetRequiredService<DuplicateFinder>());

                services.AddSingleton<FileConverter>();
                services.AddSingleton<IFileConverter>(sp => sp.GetRequiredService<FileConverter>());

                services.AddSingleton<BluetoothAnalyzer>();
                services.AddSingleton<IBluetoothAnalyzer>(sp => sp.GetRequiredService<BluetoothAnalyzer>());

                services.AddSingleton<WiFiAnalyzer>();
                services.AddSingleton<IWiFiAnalyzer>(sp => sp.GetRequiredService<WiFiAnalyzer>());

                services.AddSingleton<PdfTools>();
                services.AddSingleton<IPdfTools>(sp => sp.GetRequiredService<PdfTools>());

                services.AddSingleton<PdfEditor>();
                services.AddSingleton<IPdfEditor>(sp => sp.GetRequiredService<PdfEditor>());

                services.AddSingleton<NetworkMapper>();
                services.AddSingleton<INetworkMapper>(sp => sp.GetRequiredService<NetworkMapper>());

                services.AddSingleton<ImageTools>();
                services.AddSingleton<IImageTools>(sp => sp.GetRequiredService<ImageTools>());

                services.AddSingleton<InstalledProgramsService>();
                services.AddSingleton<IInstalledProgramsService>(sp => sp.GetRequiredService<InstalledProgramsService>());

                services.AddSingleton<DriveWiper>();
                services.AddSingleton<IDriveWiper>(sp => sp.GetRequiredService<DriveWiper>());

                services.AddSingleton<HealthCheckService>();
                services.AddSingleton<IHealthCheckService>(sp => sp.GetRequiredService<HealthCheckService>());

                services.AddSingleton<SystemRestoreService>();
                services.AddSingleton<ISystemRestoreService>(sp => sp.GetRequiredService<SystemRestoreService>());

                services.AddSingleton<ScheduledCleaningService>();
                services.AddSingleton<IScheduledCleaningService>(sp => sp.GetRequiredService<ScheduledCleaningService>());

                services.AddSingleton<BackupService>();
                services.AddSingleton<IBackupService>(sp => sp.GetRequiredService<BackupService>());

                services.AddSingleton<DriverUpdater>();
                services.AddSingleton<IDriverUpdater>(sp => sp.GetRequiredService<DriverUpdater>());

                // Performance Monitoring Service
                services.AddSingleton<PerformanceMonitor>();
                services.AddSingleton<IPerformanceMonitor>(sp => sp.GetRequiredService<PerformanceMonitor>());

                // History Service (SQLite-based metric storage)
                services.AddSingleton<HistoryService>();
                services.AddSingleton<IHistoryService>(sp => sp.GetRequiredService<HistoryService>());

                // Alert Service (threshold monitoring and notifications)
                services.AddSingleton<AlertService>();
                services.AddSingleton<IAlertService>(sp => sp.GetRequiredService<AlertService>());

                // Game Mode Service (gaming optimization)
                services.AddSingleton<GameModeService>();
                services.AddSingleton<IGameModeService>(sp => sp.GetRequiredService<GameModeService>());

                // Auto Game Mode Service (automatic game detection)
                services.AddSingleton<AutoGameModeService>();
                services.AddSingleton<IAutoGameModeService>(sp => sp.GetRequiredService<AutoGameModeService>());

                // Profile Service (performance profiles)
                services.AddSingleton<ProfileService>();
                services.AddSingleton<IProfileService>(sp => sp.GetRequiredService<ProfileService>());

                // FPS Overlay Service (real-time stats overlay)
                services.AddSingleton<FpsOverlayService>();
                services.AddSingleton<IFpsOverlayService>(sp => sp.GetRequiredService<FpsOverlayService>());

                // Tray Icon Service (system tray integration)
                services.AddSingleton<TrayIconService>();

                // ViewModels (Transient - created on demand per page navigation)
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
                services.AddTransient<DonationViewModel>();
                services.AddTransient<UserGuideViewModel>();
                services.AddTransient<PerformanceViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<GameModeViewModel>();

                // Views (Transient - created on demand per navigation)
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
                services.AddTransient<DonationPage>();
                services.AddTransient<UserGuidePage>();
                services.AddTransient<PerformancePage>();
                services.AddTransient<HistoryPage>();
                services.AddTransient<GameModePage>();
            })
            .Build();
    }

    /// <summary>The key every normal launch registers under, so a second one can find the first.</summary>
    private const string SingleInstanceKey = "SysMonitor.Main";

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_smokeRun is null && await HandedToRunningCopyAsync())
        {
            return;
        }

        if (_smokeRun is not null)
        {
            _smokeRun.Attach(this);
            var window = new MainWindow();
            _mainWindow = window;
            window.Activate();
            _ = _smokeRun.RunAsync(window);
            return;
        }

        _mainWindow = new MainWindow();

        // A Game Mode session that never ended - a crash, or the machine going down with it on - leaves
        // Windows on the High Performance plan. This puts the plan back.
        _mainWindow.Closed += OnMainWindowClosed;
        _ = RestorePowerPlanAfterCrashAsync();

        _mainWindow.Activate();
    }

    /// <summary>
    /// One copy of the app. Minimize-to-tray is on by default, so closing the window only hides it, and starting
    /// the app again from the Start menu used to start a whole second copy: a second tray icon, a second history
    /// recorder writing every snapshot twice into the one database, and every alert twice. A second launch now
    /// hands its activation to the copy already running, which shows its window, and exits.
    /// <para>
    /// The elevated registry fix and the scheduled clean never get here (they exit from the constructor), and the
    /// smoke run is exempt, so neither is ever redirected into an interactive copy.
    /// </para>
    /// </summary>
    private async Task<bool> HandedToRunningCopyAsync()
    {
        try
        {
            var primary = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            if (!primary.IsCurrent)
            {
                var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                await primary.RedirectActivationToAsync(activation);
                Exit();
                return true;
            }

            primary.Activated += (_, _) =>
                _mainWindow?.DispatcherQueue.TryEnqueue(() => (_mainWindow as MainWindow)?.ShowWindow());
        }
        catch (Exception ex)
        {
            // Best effort: without the instance broker the app still runs, as it always has, one copy per launch.
            Log.Warning(ex, "Single-instance registration failed; this launch runs on its own");
        }

        return false;
    }

    private static async Task RestorePowerPlanAfterCrashAsync()
    {
        try
        {
            var restored = await GetService<IGameModeService>().RestorePowerPlanAfterCrashAsync();
            if (restored != null)
            {
                Log.Information("Power plan {Plan} restored after a Game Mode session that did not end properly", restored);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not check whether a power plan was left changed by Game Mode");
        }
    }

    /// <summary>
    /// Puts back what Game Mode changed - process priorities and the power plan - and flushes the log, rather
    /// than leaving the machine on High Performance after the window closes.
    /// </summary>
    private static void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            _host?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutting the services down did not finish cleanly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static T GetService<T>() where T : class
    {
        if (_host?.Services.GetService(typeof(T)) is not T service)
        {
            throw new InvalidOperationException($"Service {typeof(T).Name} not found.");
        }
        return service;
    }

    /// <summary>
    /// Cleans what the schedule asked for, with no window, and records what happened. Returns the process
    /// exit code, so Task Scheduler's history shows a failed run as failed.
    /// </summary>
    private static async Task<int> RunScheduledCleanAsync(ScheduledCleaningRequest request)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "Logs", "sysmonitor-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());

        try
        {
            if (!request.CleansAnything)
            {
                Log.Warning("Scheduled clean ran with nothing selected to clean");
                return 0;
            }

            var runner = new ScheduledCleaningRunner(
                new TempFileCleaner(loggerFactory.CreateLogger<TempFileCleaner>()),
                new BrowserCacheCleaner(loggerFactory.CreateLogger<BrowserCacheCleaner>()),
                loggerFactory.CreateLogger<ScheduledCleaningRunner>());

            var outcome = await runner.RunAsync(request);

            if (request.ShowNotification)
            {
                ShowScheduledCleanNotification(outcome);
            }

            return outcome.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scheduled clean failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ShowScheduledCleanNotification(ScheduledCleaningOutcome outcome)
    {
        try
        {
            var template = Windows.UI.Notifications.ToastNotificationManager.GetTemplateContent(
                Windows.UI.Notifications.ToastTemplateType.ToastText02);
            var textNodes = template.GetElementsByTagName("text");
            if (textNodes.Length >= 2)
            {
                textNodes[0].AppendChild(template.CreateTextNode("Scheduled cleaning finished"));
                textNodes[1].AppendChild(template.CreateTextNode(outcome.Summary));
            }

            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier("SysMonitor")
                .Show(new Windows.UI.Notifications.ToastNotification(template));
        }
        catch (Exception ex)
        {
            // Nobody is watching a scheduled run; the log is the record that matters.
            Log.Warning(ex, "Scheduled clean finished but its notification could not be shown");
        }
    }

    private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Fatal(exception, "AppDomain UnhandledException - IsTerminating: {IsTerminating}", e.IsTerminating);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UnobservedTaskException");
        _smokeRun?.Absorb(e.Exception);
        e.SetObserved(); // Prevent crash
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // A smoke run records the failure against the page it happened on and goes on to the next one.
        if (_smokeRun is not null)
        {
            Log.Error(e.Exception, "Unhandled during the UI smoke run: {Message}", e.Message);
            _smokeRun.Absorb(e.Exception);
            e.Handled = true;
            return;
        }

        Log.Fatal(e.Exception, "App UnhandledException: {Message}", e.Message);
        Log.CloseAndFlush();

        // Write to a crash file for immediate visibility
        try
        {
            var crashPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SysMonitor", "Logs",
                $"SysMonitor_Crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(crashPath, $"Crash at {DateTime.Now}\n\nMessage: {e.Message}\n\nException:\n{e.Exception}");
        }
        catch
        {
            // Best effort: the app is already going down with the failure this was trying to record,
            // and there is nowhere left to say that the record itself could not be written.
        }

        e.Handled = false; // Let it crash but we've logged it
    }
}
