using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.App.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ISystemInfoService _systemInfoService;
    private readonly ITempFileCleaner _tempFileCleaner;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly INetworkMonitor _networkMonitor;
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    [ObservableProperty] private int _healthScore = 0;
    [ObservableProperty] private string _healthStatus = "Checking...";
    [ObservableProperty] private string _healthColor = "#4CAF50";
    [ObservableProperty] private double _cpuUsage = 0;
    [ObservableProperty] private double _memoryUsage = 0;
    [ObservableProperty] private double _memoryUsedGB = 0;
    [ObservableProperty] private double _memoryTotalGB = 0;
    [ObservableProperty] private double _diskUsage = 0;
    [ObservableProperty] private double _diskUsedGB = 0;
    [ObservableProperty] private double _diskTotalGB = 0;
    [ObservableProperty] private int _driveCount = 0;
    [ObservableProperty] private int _batteryLevel = 0;
    [ObservableProperty] private bool _hasBattery = false;
    [ObservableProperty] private bool _isCharging = false;
    [ObservableProperty] private string _batteryStatus = "";
    [ObservableProperty] private string _osName = "";
    [ObservableProperty] private string _uptime = "";
    [ObservableProperty] private double _cleanableSpaceMB = 0;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isOptimizing = false;

    // Network stats
    [ObservableProperty] private bool _isConnected = false;
    [ObservableProperty] private string _connectionType = "Unknown";
    [ObservableProperty] private string _downloadSpeed = "0 B/s";
    [ObservableProperty] private string _uploadSpeed = "0 B/s";

    // Temperature stats
    [ObservableProperty] private double _cpuTemperature = 0;
    [ObservableProperty] private string _cpuTempStatus = "N/A";
    [ObservableProperty] private double _gpuTemperature = 0;

    // Process count
    [ObservableProperty] private int _processCount = 0;

    public DashboardViewModel(
        ISystemInfoService systemInfoService,
        ITempFileCleaner tempFileCleaner,
        IMemoryOptimizer memoryOptimizer,
        INetworkMonitor networkMonitor,
        ITemperatureMonitor temperatureMonitor)
    {
        _systemInfoService = systemInfoService;
        _tempFileCleaner = tempFileCleaner;
        _memoryOptimizer = memoryOptimizer;
        _networkMonitor = networkMonitor;
        _temperatureMonitor = temperatureMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await _temperatureMonitor.InitializeAsync();
        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _cts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

        try
        {
            // Fetch all data in parallel
            var infoTask = _systemInfoService.GetSystemInfoAsync();
            var networkTask = _networkMonitor.GetNetworkInfoAsync();
            var networkSpeedTask = _networkMonitor.GetSpeedAsync();
            var cpuTempTask = _temperatureMonitor.GetCpuTemperatureAsync();
            var gpuTempTask = _temperatureMonitor.GetGpuTemperatureAsync();

            await Task.WhenAll(infoTask, networkTask, networkSpeedTask, cpuTempTask, gpuTempTask);

            var info = await infoTask;
            var networkInfo = await networkTask;
            var (upload, download) = await networkSpeedTask;
            var cpuTemp = await cpuTempTask;
            var gpuTemp = await gpuTempTask;

            if (_isDisposed) return;

            // Update UI on dispatcher thread
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Health Score
                HealthScore = info.HealthScore;
                (HealthStatus, HealthColor) = GetHealthStatus(info.HealthScore);

                // CPU
                CpuUsage = info.Cpu.UsagePercent;

                // Memory
                MemoryUsage = info.Memory.UsagePercent;
                MemoryUsedGB = info.Memory.UsedGB;
                MemoryTotalGB = info.Memory.TotalGB;

                // Disk
                if (info.Disks.Count > 0)
                {
                    var mainDisk = info.Disks[0];
                    DiskUsage = mainDisk.UsagePercent;
                    DiskUsedGB = mainDisk.UsedGB;
                    DiskTotalGB = mainDisk.TotalGB;
                }
                DriveCount = info.Disks.Count;

                // Battery
                if (info.Battery != null)
                {
                    HasBattery = true;
                    BatteryLevel = info.Battery.ChargePercent;
                    IsCharging = info.Battery.IsCharging;
                    BatteryStatus = info.Battery.IsCharging ? "Charging" :
                                   info.Battery.IsPluggedIn ? "Plugged In" : "On Battery";
                }

                // OS & Uptime
                OsName = info.OperatingSystem.Name;
                Uptime = FormatUptime(info.OperatingSystem.Uptime);

                // Network
                IsConnected = networkInfo.IsConnected;
                ConnectionType = networkInfo.ConnectionType;
                DownloadSpeed = FormatSpeed(download);
                UploadSpeed = FormatSpeed(upload);

                // Temperature
                CpuTemperature = cpuTemp;
                GpuTemperature = gpuTemp;
                CpuTempStatus = GetTempStatus(cpuTemp);

                // Process count (estimate from CPU info)
                ProcessCount = Environment.ProcessorCount * 10; // Rough estimate

                IsLoading = false;
            });

            // Get cleanable space (less critical, can be async)
            var cleanable = await _tempFileCleaner.GetTotalCleanableBytesAsync();
            if (!_isDisposed)
            {
                _dispatcherQueue.TryEnqueue(() => CleanableSpaceMB = cleanable / (1024.0 * 1024));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Log in production - for now, silently handle
        }
    }

    private static (string status, string color) GetHealthStatus(int score)
    {
        return score switch
        {
            >= 90 => ("Excellent", "#4CAF50"),
            >= 75 => ("Good", "#8BC34A"),
            >= 60 => ("Fair", "#FF9800"),
            >= 40 => ("Poor", "#FF5722"),
            _ => ("Critical", "#F44336")
        };
    }

    private static string GetTempStatus(double temp)
    {
        return temp switch
        {
            0 => "N/A",
            <= 45 => "Cool",
            <= 65 => "Normal",
            <= 80 => "Warm",
            <= 90 => "Hot",
            _ => "Critical"
        };
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1_000_000)
            return $"{bytesPerSecond / 1_000_000:F1} MB/s";
        if (bytesPerSecond >= 1_000)
            return $"{bytesPerSecond / 1_000:F1} KB/s";
        return $"{bytesPerSecond:F0} B/s";
    }

    [RelayCommand]
    private async Task QuickCleanAsync()
    {
        IsOptimizing = true;
        try
        {
            var items = await _tempFileCleaner.ScanAsync();
            await _tempFileCleaner.CleanAsync(items);
            await RefreshDataAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        IsOptimizing = true;
        try
        {
            await _memoryOptimizer.OptimizeMemoryAsync();
            await RefreshDataAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
