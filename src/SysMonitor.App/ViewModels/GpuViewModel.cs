using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;
using System.Management;

namespace SysMonitor.App.ViewModels;

public partial class GpuViewModel : ObservableObject, IDisposable
{
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    // GPU Info (static)
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _gpuDriver = "";
    [ObservableProperty] private string _gpuMemory = "";
    [ObservableProperty] private string _gpuResolution = "";

    // GPU Stats (dynamic)
    [ObservableProperty] private double _gpuTemperature;
    [ObservableProperty] private string _tempStatus = "N/A";
    [ObservableProperty] private string _tempColor = "#808080";

    // Additional temps with status
    [ObservableProperty] private double _gpuHotSpot;
    [ObservableProperty] private string _hotSpotStatus = "N/A";
    [ObservableProperty] private string _hotSpotColor = "#808080";
    [ObservableProperty] private double _gpuMemoryTemp;
    [ObservableProperty] private string _memTempStatus = "N/A";
    [ObservableProperty] private string _memTempColor = "#808080";
    [ObservableProperty] private bool _hasHotSpot;
    [ObservableProperty] private bool _hasMemoryTemp;

    // State
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasGpu = true;

    public GpuViewModel(ITemperatureMonitor temperatureMonitor)
    {
        _temperatureMonitor = temperatureMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await _temperatureMonitor.InitializeAsync();
        await LoadGpuInfoAsync();
        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private async Task LoadGpuInfoAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    var driver = obj["DriverVersion"]?.ToString() ?? "";
                    var ram = obj["AdapterRAM"];
                    var hRes = obj["CurrentHorizontalResolution"];
                    var vRes = obj["CurrentVerticalResolution"];

                    string memory = "Unknown";
                    if (ram != null)
                    {
                        var ramBytes = Convert.ToUInt64(ram);
                        if (ramBytes > 0)
                        {
                            var ramGB = ramBytes / (1024.0 * 1024 * 1024);
                            memory = ramGB >= 1 ? $"{ramGB:F0} GB" : $"{ramBytes / (1024 * 1024)} MB";
                        }
                    }

                    string resolution = "";
                    if (hRes != null && vRes != null)
                    {
                        resolution = $"{hRes} x {vRes}";
                    }

                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        GpuName = name;
                        GpuDriver = driver;
                        GpuMemory = memory;
                        GpuResolution = resolution;
                        HasGpu = !string.IsNullOrEmpty(name) && name != "Unknown GPU";
                    });
                    break; // Use first GPU
                }
            }
            catch
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    GpuName = "GPU Not Detected";
                    HasGpu = false;
                });
            }
        });
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
            var allTemps = await _temperatureMonitor.GetAllTemperaturesAsync();
            var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();

            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Main GPU temp
                GpuTemperature = gpuTemp;
                (TempStatus, TempColor) = GetTempStatus(gpuTemp);

                // Look for hot spot and memory temps
                var hotSpot = allTemps.FirstOrDefault(t =>
                    t.Key.Contains("GPU", StringComparison.OrdinalIgnoreCase) &&
                    t.Key.Contains("Hot", StringComparison.OrdinalIgnoreCase));

                var memTemp = allTemps.FirstOrDefault(t =>
                    t.Key.Contains("GPU", StringComparison.OrdinalIgnoreCase) &&
                    t.Key.Contains("Memory", StringComparison.OrdinalIgnoreCase));

                if (hotSpot.Key != null && hotSpot.Value > 0)
                {
                    GpuHotSpot = hotSpot.Value;
                    (HotSpotStatus, HotSpotColor) = GetTempStatus(hotSpot.Value);
                    HasHotSpot = true;
                }

                if (memTemp.Key != null && memTemp.Value > 0)
                {
                    GpuMemoryTemp = memTemp.Value;
                    (MemTempStatus, MemTempColor) = GetTempStatus(memTemp.Value);
                    HasMemoryTemp = true;
                }

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
                IsLoading = false;
            });
        }
    }

    private static (string status, string color) GetTempStatus(double temp)
    {
        return temp switch
        {
            0 => ("N/A", "#808080"),
            <= 50 => ("Cool", "#2196F3"),
            <= 70 => ("Normal", "#4CAF50"),
            <= 85 => ("Warm", "#FF9800"),
            <= 95 => ("Hot", "#FF5722"),
            _ => ("Critical", "#F44336")
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
