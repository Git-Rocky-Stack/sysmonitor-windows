using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.App.ViewModels;

public partial class MemoryViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IMemoryOptimizer _memoryOptimizer;
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
    [ObservableProperty] private bool _isOptimizing = false;

    // Action Feedback
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus = false;
    [ObservableProperty] private bool _isActionSuccess = true;
    [ObservableProperty] private long _bytesFreed = 0;

    public MemoryViewModel(IMemoryMonitor memoryMonitor, IMemoryOptimizer memoryOptimizer)
    {
        _memoryMonitor = memoryMonitor;
        _memoryOptimizer = memoryOptimizer;
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
            // Expected when disposed
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

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
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Log in production
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
        ShowActionStatus("Optimizing memory...", true);

        try
        {
            // Capture memory before optimization
            var beforeMemInfo = await _memoryMonitor.GetMemoryInfoAsync();
            var beforeUsedBytes = beforeMemInfo.UsedBytes;

            await _memoryOptimizer.OptimizeMemoryAsync();
            await RefreshDataAsync();

            // Calculate freed memory
            var afterMemInfo = await _memoryMonitor.GetMemoryInfoAsync();
            var freedBytes = beforeUsedBytes - afterMemInfo.UsedBytes;
            BytesFreed = freedBytes > 0 ? freedBytes : 0;

            if (BytesFreed > 0)
            {
                var freedMB = BytesFreed / (1024.0 * 1024);
                ShowActionStatus($"Freed {freedMB:F1} MB of memory!", true);
            }
            else
            {
                ShowActionStatus("Memory already optimized!", true);
            }
        }
        catch (Exception)
        {
            ShowActionStatus("Optimization failed", false);
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
