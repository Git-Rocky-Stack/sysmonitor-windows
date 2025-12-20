using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using SkiaSharp;
using SysMonitor.Core.Data.Entities;
using SysMonitor.Core.Services.History;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IHistoryService _historyService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _selectedTimeRangeIndex = 1; // Default to 24 hours
    [ObservableProperty] private int _totalMetricCount;

    // Chart data
    [ObservableProperty] private ObservableCollection<ISeries> _cpuSeries = new();
    [ObservableProperty] private ObservableCollection<ISeries> _memorySeries = new();
    [ObservableProperty] private ObservableCollection<ISeries> _temperatureSeries = new();

    // Chart axes
    public Axis[] TimeAxes { get; } = new Axis[]
    {
        new Axis
        {
            Labeler = value => new DateTime((long)value).ToString("MM/dd HH:mm"),
            LabelsRotation = 15,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(SKColors.Gray)
        }
    };

    public Axis[] PercentAxes { get; } = new Axis[]
    {
        new Axis
        {
            MinLimit = 0,
            MaxLimit = 100,
            Labeler = value => $"{value:F0}%",
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(SKColors.Gray)
        }
    };

    public Axis[] TempAxes { get; } = new Axis[]
    {
        new Axis
        {
            MinLimit = 50,
            MaxLimit = 220,
            Labeler = value => $"{value:F0}°F",
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(SKColors.Gray)
        }
    };

    public string[] TimeRanges { get; } = { "1 Hour", "24 Hours", "7 Days", "30 Days" };

    public HistoryViewModel(IHistoryService historyService)
    {
        _historyService = historyService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await LoadChartDataAsync();
    }

    partial void OnSelectedTimeRangeIndexChanged(int value)
    {
        _ = LoadChartDataAsync();
    }

    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        await LoadChartDataAsync();
    }

    private async Task LoadChartDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading history data...";

        try
        {
            var (startTime, maxPoints) = GetTimeRangeParams();
            var endTime = DateTime.UtcNow;

            // Load all metrics in parallel
            var cpuTask = _historyService.GetAggregatedHistoryAsync(MetricTypes.Cpu, startTime, endTime, maxPoints);
            var memTask = _historyService.GetAggregatedHistoryAsync(MetricTypes.Memory, startTime, endTime, maxPoints);
            var cpuTempTask = _historyService.GetAggregatedHistoryAsync(MetricTypes.CpuTemp, startTime, endTime, maxPoints);
            var gpuTempTask = _historyService.GetAggregatedHistoryAsync(MetricTypes.GpuTemp, startTime, endTime, maxPoints);
            var countTask = _historyService.GetMetricCountAsync();

            await Task.WhenAll(cpuTask, memTask, cpuTempTask, gpuTempTask, countTask);

            var cpuData = await cpuTask;
            var memData = await memTask;
            var cpuTempData = await cpuTempTask;
            var gpuTempData = await gpuTempTask;
            TotalMetricCount = await countTask;

            _dispatcherQueue.TryEnqueue(() =>
            {
                UpdateCpuChart(cpuData);
                UpdateMemoryChart(memData);
                UpdateTemperatureChart(cpuTempData, gpuTempData);

                var dataPoints = cpuData.Count + memData.Count + cpuTempData.Count + gpuTempData.Count;
                StatusMessage = dataPoints > 0
                    ? $"Showing {dataPoints:N0} data points ({TotalMetricCount:N0} total stored)"
                    : "No history data yet. Data will appear after monitoring for a few minutes.";
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusMessage = $"Error loading data: {ex.Message}";
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private (DateTime startTime, int maxPoints) GetTimeRangeParams()
    {
        return SelectedTimeRangeIndex switch
        {
            0 => (DateTime.UtcNow.AddHours(-1), 120),      // 1 hour: ~30s intervals = 120 points
            1 => (DateTime.UtcNow.AddHours(-24), 288),     // 24 hours: ~5min intervals = 288 points
            2 => (DateTime.UtcNow.AddDays(-7), 336),       // 7 days: ~30min intervals = 336 points
            3 => (DateTime.UtcNow.AddDays(-30), 360),      // 30 days: ~2hr intervals = 360 points
            _ => (DateTime.UtcNow.AddHours(-24), 288)
        };
    }

    private void UpdateCpuChart(List<MetricSnapshot> data)
    {
        var values = data.Select(d => new DateTimePoint(d.Timestamp.ToLocalTime(), d.Value)).ToList();

        CpuSeries = new ObservableCollection<ISeries>
        {
            new LineSeries<DateTimePoint>
            {
                Values = values,
                Fill = new SolidColorPaint(SKColors.Red.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.Red, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };
    }

    private void UpdateMemoryChart(List<MetricSnapshot> data)
    {
        var values = data.Select(d => new DateTimePoint(d.Timestamp.ToLocalTime(), d.Value)).ToList();

        MemorySeries = new ObservableCollection<ISeries>
        {
            new LineSeries<DateTimePoint>
            {
                Values = values,
                Fill = new SolidColorPaint(SKColors.Cyan.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.Cyan, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };
    }

    private void UpdateTemperatureChart(List<MetricSnapshot> cpuTemp, List<MetricSnapshot> gpuTemp)
    {
        // Convert Celsius to Fahrenheit: F = (C * 1.8) + 32
        var cpuValues = cpuTemp.Select(d => new DateTimePoint(d.Timestamp.ToLocalTime(), (d.Value * 1.8) + 32)).ToList();
        var gpuValues = gpuTemp.Select(d => new DateTimePoint(d.Timestamp.ToLocalTime(), (d.Value * 1.8) + 32)).ToList();

        var series = new ObservableCollection<ISeries>();

        if (cpuValues.Count > 0)
        {
            series.Add(new LineSeries<DateTimePoint>
            {
                Values = cpuValues,
                Name = "CPU",
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.Orange, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            });
        }

        if (gpuValues.Count > 0)
        {
            series.Add(new LineSeries<DateTimePoint>
            {
                Values = gpuValues,
                Name = "GPU",
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.LimeGreen, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            });
        }

        TemperatureSeries = series;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
