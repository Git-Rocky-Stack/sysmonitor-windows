using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.App.Helpers;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Monitoring;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;

namespace SysMonitor.App.ViewModels;

public partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitor _processMonitor;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;
    private readonly object _refreshLock = new();
    private bool _isRefreshing;
    private Timer? _searchDebounceTimer;
    private readonly Dictionary<int, ProcessInfo> _processCache = new();
    private List<ProcessInfo> _allProcesses = new();

    [ObservableProperty] private ObservableCollection<ProcessInfo> _processes = new();
    [ObservableProperty] private ProcessInfo? _selectedProcess;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private int _totalProcesses = 0;
    [ObservableProperty] private double _totalCpuUsage = 0;
    [ObservableProperty] private double _totalMemoryMB = 0;

    public ProcessesViewModel(IProcessMonitor processMonitor, IPerformanceMonitor performanceMonitor)
    {
        _processMonitor = processMonitor;
        _performanceMonitor = performanceMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        await RefreshProcessesAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _cts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshProcessesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private async Task RefreshProcessesAsync()
    {
        // Prevent overlapping refreshes
        lock (_refreshLock)
        {
            if (_isRefreshing || _isDisposed) return;
            _isRefreshing = true;
        }

        using var _ = _performanceMonitor.TrackOperation("Processes.Refresh");

        try
        {
            var newProcesses = await _processMonitor.GetAllProcessesAsync();
            if (_isDisposed) return;

            // Store all processes for filtering
            _allProcesses = newProcesses;

            var totalCount = newProcesses.Count;
            var totalCpu = newProcesses.Sum(p => p.CpuUsagePercent);
            var totalMem = newProcesses.Sum(p => p.MemoryMB);

            // Apply filter on background thread
            var searchText = SearchText;
            var filtered = string.IsNullOrEmpty(searchText)
                ? newProcesses
                : newProcesses.Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (_isDisposed) return;

                // Use differential updates instead of Clear + Add
                UpdateProcessesCollection(filtered);

                TotalProcesses = totalCount;
                TotalCpuUsage = totalCpu;
                TotalMemoryMB = totalMem;
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
        finally
        {
            lock (_refreshLock)
            {
                _isRefreshing = false;
            }
        }
    }

    private void UpdateProcessesCollection(List<ProcessInfo> newProcesses)
    {
        // Build lookup for O(1) access
        var newDict = newProcesses.ToDictionary(p => p.Id);
        var existingIds = new HashSet<int>(Processes.Select(p => p.Id));

        // Batch updates to minimize UI notifications
        var toRemove = new List<ProcessInfo>();
        var toAdd = new List<ProcessInfo>();
        var toUpdate = new List<(int index, ProcessInfo newProc)>();

        // Find items to remove or update
        for (int i = 0; i < Processes.Count; i++)
        {
            var existing = Processes[i];
            if (!newDict.TryGetValue(existing.Id, out var newProc))
            {
                toRemove.Add(existing);
            }
            else if (!ProcessEquals(existing, newProc))
            {
                toUpdate.Add((i, newProc));
            }
        }

        // Find new items to add
        foreach (var newProc in newProcesses)
        {
            if (!existingIds.Contains(newProc.Id))
            {
                toAdd.Add(newProc);
            }
        }

        // Apply removals
        foreach (var proc in toRemove)
        {
            Processes.Remove(proc);
        }

        // Apply updates (replace items to trigger UI update)
        foreach (var (index, newProc) in toUpdate.OrderByDescending(x => x.index))
        {
            if (index < Processes.Count)
            {
                Processes[index] = newProc;
            }
        }

        // Apply additions
        foreach (var proc in toAdd)
        {
            Processes.Add(proc);
        }
    }

    private static bool ProcessEquals(ProcessInfo a, ProcessInfo b)
    {
        return a.Id == b.Id &&
               Math.Abs(a.CpuUsagePercent - b.CpuUsagePercent) < 0.1 &&
               Math.Abs(a.MemoryMB - b.MemoryMB) < 0.1 &&
               a.Status == b.Status;
    }

    [RelayCommand]
    private async Task KillProcessAsync()
    {
        if (SelectedProcess == null) return;
        await _processMonitor.KillProcessAsync(SelectedProcess.Id);
        await RefreshProcessesAsync();
    }

    [RelayCommand]
    private async Task SetPriorityAsync(string priority)
    {
        if (SelectedProcess == null) return;
        var p = Enum.Parse<ProcessPriority>(priority);
        await _processMonitor.SetPriorityAsync(SelectedProcess.Id, p);
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce search input to avoid excessive updates while typing
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(_ =>
        {
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = null;

            // Filter existing data without fetching new data
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                var filtered = string.IsNullOrEmpty(value)
                    ? _allProcesses
                    : _allProcesses.Where(p => p.Name.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();

                UpdateProcessesCollection(filtered);
            });
        }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _searchDebounceTimer?.Dispose();
    }
}
