using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class TemperatureViewModel : ObservableObject, IDisposable
{
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    // Temperature Collection
    public ObservableCollection<TemperatureDisplayInfo> Temperatures { get; } = [];

    // Primary Temperatures
    [ObservableProperty] private double _cpuTemperature;
    [ObservableProperty] private double _gpuTemperature;
    [ObservableProperty] private string _cpuTempStatus = "Normal";
    [ObservableProperty] private string _gpuTempStatus = "Normal";
    [ObservableProperty] private string _cpuTempColor = "#4CAF50";
    [ObservableProperty] private string _gpuTempColor = "#4CAF50";

    // State
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasTemperatures;
    [ObservableProperty] private bool _noSensorsFound;
    [ObservableProperty] private int _sensorCount;

    public TemperatureViewModel(ITemperatureMonitor temperatureMonitor)
    {
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); // Temperature updates every 2 seconds
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
            var allTemps = await _temperatureMonitor.GetAllTemperaturesAsync();
            var cpuTemp = await _temperatureMonitor.GetCpuTemperatureAsync();
            var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();

            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Primary temperatures
                CpuTemperature = cpuTemp;
                GpuTemperature = gpuTemp;

                // CPU Status
                (CpuTempStatus, CpuTempColor) = GetTempStatus(cpuTemp);
                (GpuTempStatus, GpuTempColor) = GetTempStatus(gpuTemp);

                // All sensors
                Temperatures.Clear();
                foreach (var temp in allTemps.OrderBy(t => t.Key))
                {
                    var (status, color) = GetTempStatus(temp.Value);
                    var category = GetCategory(temp.Key);
                    Temperatures.Add(new TemperatureDisplayInfo
                    {
                        Name = temp.Key,
                        Temperature = temp.Value,
                        Status = status,
                        StatusColor = color,
                        Icon = GetIcon(category),
                        Category = category
                    });
                }

                SensorCount = Temperatures.Count;
                HasTemperatures = Temperatures.Count > 0;
                NoSensorsFound = Temperatures.Count == 0;
                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                NoSensorsFound = true;
                HasTemperatures = false;
                IsLoading = false;
            });
        }
    }

    private static (string status, string color) GetTempStatus(double temp)
    {
        return temp switch
        {
            0 => ("N/A", "#808080"),
            <= 45 => ("Cool", "#2196F3"),      // Blue - Cool
            <= 65 => ("Normal", "#4CAF50"),    // Green - Normal
            <= 80 => ("Warm", "#FF9800"),      // Orange - Warm
            <= 90 => ("Hot", "#FF5722"),       // Deep Orange - Hot
            _ => ("Critical", "#F44336")        // Red - Critical
        };
    }

    private static string GetCategory(string sensorName)
    {
        if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            return "CPU";
        if (sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            return "GPU";
        if (sensorName.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Disk", StringComparison.OrdinalIgnoreCase) ||
            sensorName.Contains("Drive", StringComparison.OrdinalIgnoreCase))
            return "Storage";
        return "System";
    }

    private static string GetIcon(string category)
    {
        return category switch
        {
            "CPU" => "\uE950",     // Processor
            "GPU" => "\uE7F4",     // Display
            "Storage" => "\uEDA2", // Hard drive
            _ => "\uE770"          // Hardware
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public class TemperatureDisplayInfo
{
    public string Name { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public string FormattedTemp => Temperature > 0 ? $"{Temperature:F0}°C" : "N/A";
    public string ShortName => Name.Contains(" - ") ? Name.Split(" - ").Last() : Name;
}
