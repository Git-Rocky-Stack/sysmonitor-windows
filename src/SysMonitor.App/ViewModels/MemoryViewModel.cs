using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Monitoring;
using SysMonitor.Core.Services.Optimizers;
using Serilog;

namespace SysMonitor.App.ViewModels;

public partial class MemoryViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

    // Physical Memory
    [ObservableProperty] private double _totalGB;
    [ObservableProperty] private double _usedGB;
    [ObservableProperty] private double _availableGB;
    [ObservableProperty] private double _usagePercent;

    // Page File (Virtual Memory)
    [ObservableProperty] private double _pageFileTotalGB;
    [ObservableProperty] private double _pageFileUsedGB;
    [ObservableProperty] private double _pageFileUsagePercent;

    // Calculated Values
    [ObservableProperty] private double _cachedGB;
    [ObservableProperty] private string _memoryStatus = "Checking...";
    [ObservableProperty] private string _statusColor = "#4CAF50";

    // State
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTrimmed))] private bool _isOptimizing = false;

    // Action Feedback
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTrimmed))] private bool _isActionSuccess = true;

    /// <summary>
    /// A trim has finished and worked. The banner's tick and badge showed whenever nothing was running, so a
    /// failure read "Optimization failed" beside a green tick and "MEMORY OPTIMIZED".
    /// </summary>
    public bool HasTrimmed => !IsOptimizing && IsActionSuccess;

    public MemoryViewModel(IMemoryMonitor memoryMonitor, IMemoryOptimizer memoryOptimizer, IPerformanceMonitor performanceMonitor)
    {
        _memoryMonitor = memoryMonitor;
        _memoryOptimizer = memoryOptimizer;
        _performanceMonitor = performanceMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Best effort: the page was left while this was in flight, so the work it was doing
            // no longer has anywhere to go.
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

        using var _ = _performanceMonitor.TrackOperation("Memory.Refresh");

        try
        {
            var memInfo = await _memoryMonitor.GetMemoryInfoAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Physical Memory
                TotalGB = memInfo.TotalGB;
                UsedGB = memInfo.UsedGB;
                AvailableGB = memInfo.AvailableGB;
                UsagePercent = memInfo.UsagePercent;

                // Page File
                PageFileTotalGB = memInfo.PageFileTotal / (1024.0 * 1024 * 1024);
                PageFileUsedGB = memInfo.PageFileUsed / (1024.0 * 1024 * 1024);
                PageFileUsagePercent = PageFileTotalGB > 0 ? (PageFileUsedGB / PageFileTotalGB) * 100 : 0;

                // Status based on usage
                UpdateMemoryStatus(memInfo.UsagePercent);

                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Best effort: the app is closing and the refresh was cancelled on purpose.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Refreshing the memory page failed");
        }
    }

    private void UpdateMemoryStatus(double usagePercent)
    {
        if (usagePercent >= 90)
        {
            MemoryStatus = "Critical - Consider closing applications";
            StatusColor = "#F44336"; // Red
        }
        else if (usagePercent >= 75)
        {
            MemoryStatus = "High Usage - Monitor closely";
            StatusColor = "#FF9800"; // Orange
        }
        else if (usagePercent >= 50)
        {
            MemoryStatus = "Normal - System running smoothly";
            StatusColor = "#8BC34A"; // Light Green
        }
        else
        {
            MemoryStatus = "Excellent - Plenty of memory available";
            StatusColor = "#4CAF50"; // Green
        }
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        if (IsOptimizing) return;

        IsOptimizing = true;
        ShowActionStatus("Trimming memory...", true);

        using var perfTracker = _performanceMonitor.TrackOperation("Memory.Optimize");

        try
        {
            // The optimizer's own count, in the Dashboard's words for the same operation. This used to take the drop in
            // used memory from before to after - a number every other app on the machine moved - and report it as memory
            // the trim had released, or say memory was already optimized when there was no drop. A trimmed page goes to
            // the standby list, where Windows can page it straight back; nothing is released.
            var trimmedBytes = await _memoryOptimizer.OptimizeMemoryAsync();
            await RefreshDataAsync();

            ShowActionStatus($"Trimmed {FormatHelper.FormatSize(trimmedBytes)} from background apps", true);
        }
        catch (Exception ex)
        {
            ShowActionStatus($"Trim failed: {ex.Message}", false);
        }
        finally
        {
            IsOptimizing = false;

            // Clear status after delay
            _ = ClearActionStatusAfterDelayAsync();
        }
    }

    private void ShowActionStatus(string message, bool isSuccess)
    {
        ActionStatus = message;
        IsActionSuccess = isSuccess;
        HasActionStatus = true;
    }

    private async Task ClearActionStatusAfterDelayAsync()
    {
        await Task.Delay(5000);
        _dispatcherQueue.TryEnqueue(() =>
        {
            HasActionStatus = false;
            ActionStatus = "";
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
