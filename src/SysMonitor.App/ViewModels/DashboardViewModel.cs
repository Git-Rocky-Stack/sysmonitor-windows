using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Optimizers;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;

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
    private bool _isInitialized;

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

    // Action status
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus = false;
    [ObservableProperty] private bool _isActionSuccess = true;

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
        if (_isInitialized) return;
        _isInitialized = true;

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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
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
        ShowActionStatus("Cleaning temporary files...", true);
        try
        {
            var items = await _tempFileCleaner.ScanAsync();
            var cleaned = await _tempFileCleaner.CleanAsync(items);
            await RefreshDataAsync();
            ShowActionStatus($"Cleaned {cleaned.FilesDeleted} files successfully!", true);
        }
        catch (Exception ex)
        {
            ShowActionStatus($"Clean failed: {ex.Message}", false);
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
        ShowActionStatus("Optimizing memory...", true);
        try
        {
            var freedMB = await _memoryOptimizer.OptimizeMemoryAsync();
            await RefreshDataAsync();
            ShowActionStatus($"Freed {freedMB:F0} MB of memory!", true);
        }
        catch (Exception ex)
        {
            ShowActionStatus($"Optimization failed: {ex.Message}", false);
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        IsOptimizing = true;
        ShowActionStatus("Generating system report...", true);
        try
        {
            var report = await GenerateSystemReportAsync();

            // Create file in Documents folder
            var documentsFolder = await StorageFolder.GetFolderFromPathAsync(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"SysMonitor_Report_{timestamp}.txt";
            var file = await documentsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(file, report);

            ShowActionStatus($"Report saved to Documents/{fileName}", true);
        }
        catch (Exception ex)
        {
            ShowActionStatus($"Export failed: {ex.Message}", false);
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private async Task<string> GenerateSystemReportAsync()
    {
        var sb = new StringBuilder();
        var info = await _systemInfoService.GetSystemInfoAsync();
        var networkInfo = await _networkMonitor.GetNetworkInfoAsync();
        var cpuTemp = await _temperatureMonitor.GetCpuTemperatureAsync();
        var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();

        sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║           STX.1 SYSTEM MONITOR - DIAGNOSTIC REPORT               ║");
        sb.AppendLine("║                Strategic. Excellence. Engineered.                 ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Health Score: {info.HealthScore}% ({GetHealthStatus(info.HealthScore).status})");
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ OPERATING SYSTEM                                                  │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        sb.AppendLine($"  Name:      {info.OperatingSystem.Name}");
        sb.AppendLine($"  Version:   {info.OperatingSystem.Version}");
        sb.AppendLine($"  Build:     {info.OperatingSystem.Build}");
        sb.AppendLine($"  Uptime:    {FormatUptime(info.OperatingSystem.Uptime)}");
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ CPU                                                               │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        sb.AppendLine($"  Model:     {info.Cpu.Name}");
        sb.AppendLine($"  Cores:     {info.Cpu.Cores} cores / {info.Cpu.LogicalProcessors} threads");
        sb.AppendLine($"  Speed:     {info.Cpu.MaxClockSpeedMHz} MHz");
        sb.AppendLine($"  Usage:     {info.Cpu.UsagePercent:F1}%");
        sb.AppendLine($"  Temp:      {cpuTemp:F0}°C ({GetTempStatus(cpuTemp)})");
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ MEMORY                                                            │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        sb.AppendLine($"  Total:     {info.Memory.TotalGB:F1} GB");
        sb.AppendLine($"  Used:      {info.Memory.UsedGB:F1} GB ({info.Memory.UsagePercent:F1}%)");
        sb.AppendLine($"  Available: {info.Memory.AvailableGB:F1} GB");
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ GPU                                                               │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        sb.AppendLine($"  Temp:      {gpuTemp:F0}°C ({GetTempStatus(gpuTemp)})");
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ STORAGE                                                           │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        foreach (var disk in info.Disks)
        {
            sb.AppendLine($"  [{disk.Name}] {disk.Label}");
            sb.AppendLine($"       Total: {disk.TotalGB:F1} GB | Used: {disk.UsedGB:F1} GB ({disk.UsagePercent:F1}%)");
            sb.AppendLine($"       Free:  {disk.FreeGB:F1} GB");
        }
        sb.AppendLine();

        sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ NETWORK                                                           │");
        sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
        sb.AppendLine($"  Status:    {(networkInfo.IsConnected ? "Connected" : "Disconnected")}");
        sb.AppendLine($"  Type:      {networkInfo.ConnectionType}");
        sb.AppendLine($"  Adapter:   {networkInfo.AdapterName}");
        if (!string.IsNullOrEmpty(networkInfo.IpAddress))
            sb.AppendLine($"  IP:        {networkInfo.IpAddress}");
        sb.AppendLine();

        if (info.Battery != null)
        {
            sb.AppendLine("┌──────────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│ BATTERY                                                           │");
            sb.AppendLine("└──────────────────────────────────────────────────────────────────┘");
            sb.AppendLine($"  Level:     {info.Battery.ChargePercent}%");
            sb.AppendLine($"  Status:    {(info.Battery.IsCharging ? "Charging" : info.Battery.IsPluggedIn ? "Plugged In" : "On Battery")}");
            sb.AppendLine();
        }

        sb.AppendLine("══════════════════════════════════════════════════════════════════");
        sb.AppendLine("                    End of Diagnostic Report");
        sb.AppendLine("══════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private void ShowActionStatus(string message, bool success)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            ActionStatus = message;
            IsActionSuccess = success;
            HasActionStatus = true;
        });

        // Auto-clear after 5 seconds (for success messages)
        if (success && !message.Contains("..."))
        {
            Task.Delay(5000).ContinueWith(_ =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    HasActionStatus = false;
                    ActionStatus = "";
                });
            });
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
