using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitoring;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace SysMonitor.App.ViewModels;

public partial class PerformanceViewModel : ObservableObject, IDisposable
{
    private readonly IPerformanceMonitor _performanceMonitor;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

    [ObservableProperty] private ObservableCollection<PerformanceMetric> _metrics = new();
    [ObservableProperty] private bool _isMonitoring = true;
    [ObservableProperty] private double _frameRate = 60;
    [ObservableProperty] private double _uiThreadUtilization = 0;
    [ObservableProperty] private double _memoryUsageMB = 0;
    [ObservableProperty] private double _gcPressure = 0;

    public PerformanceViewModel(IPerformanceMonitor performanceMonitor)
    {
        _performanceMonitor = performanceMonitor;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (_dispatcherQueue == null)
        {
            System.Diagnostics.Debug.WriteLine("PerformanceViewModel: DispatcherQueue is null!");
            return;
        }

        await RefreshMetricsAsync();
        StartMonitoring();
    }

    private void StartMonitoring()
    {
        _cts = new CancellationTokenSource();
        _ = MonitoringLoopAsync(_cts.Token);
    }

    private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (IsMonitoring)
                {
                    await RefreshMetricsAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private Task RefreshMetricsAsync()
    {
        if (_isDisposed) return Task.CompletedTask;

        try
        {
            var stats = _performanceMonitor.GetAllStats();

            // Calculate system metrics
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / (1024.0 * 1024.0);
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var gcPressure = gc0 + gc1 * 10 + gc2 * 100; // Weighted GC pressure

            var metrics = new List<PerformanceMetric>();

            foreach (var kvp in stats)
            {
                metrics.Add(new PerformanceMetric
                {
                    Operation = kvp.Key,
                    Count = kvp.Value.Count,
                    AverageMs = kvp.Value.AverageMs,
                    MinMs = kvp.Value.MinMs,
                    MaxMs = kvp.Value.MaxMs,
                    P95Ms = kvp.Value.P95Ms,
                    P99Ms = kvp.Value.P99Ms,
                    LastMs = kvp.Value.LastMs,
                    Status = GetStatus(kvp.Value.AverageMs)
                });
            }

            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                Metrics.Clear();
                foreach (var metric in metrics.OrderBy(m => m.Operation))
                {
                    Metrics.Add(metric);
                }

                MemoryUsageMB = workingSet;
                GcPressure = gcPressure;
            });
        }
        catch (Exception)
        {
            // Ignore errors during refresh
        }

        return Task.CompletedTask;
    }

    private static string GetStatus(double averageMs)
    {
        return averageMs switch
        {
            < 16 => "Excellent",
            < 50 => "Good",
            < 100 => "Fair",
            < 200 => "Poor",
            _ => "Critical"
        };
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
    }

    [RelayCommand]
    private void ClearMetrics()
    {
        _performanceMonitor.Clear();
        Metrics.Clear();
    }

    [RelayCommand]
    private async Task ExportMetricsAsync()
    {
        var stats = _performanceMonitor.GetAllStats();
        var csv = "Operation,Count,Average(ms),Min(ms),Max(ms),P95(ms),P99(ms)\n";

        foreach (var kvp in stats)
        {
            csv += $"{kvp.Key},{kvp.Value.Count},{kvp.Value.AverageMs:F2}," +
                   $"{kvp.Value.MinMs:F2},{kvp.Value.MaxMs:F2}," +
                   $"{kvp.Value.P95Ms:F2},{kvp.Value.P99Ms:F2}\n";
        }

        var fileName = $"performance_metrics_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

        await File.WriteAllTextAsync(path, csv);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public class PerformanceMetric
{
    public string Operation { get; set; } = "";
    public int Count { get; set; }
    public double AverageMs { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double LastMs { get; set; }
    public string Status { get; set; } = "";

    // Pre-formatted strings to avoid converter issues in XAML
    public string AverageMsFormatted => AverageMs.ToString("F2");
    public string MinMsFormatted => MinMs.ToString("F2");
    public string MaxMsFormatted => MaxMs.ToString("F2");
    public string P95MsFormatted => P95Ms.ToString("F2");
    public string P99MsFormatted => P99Ms.ToString("F2");
}