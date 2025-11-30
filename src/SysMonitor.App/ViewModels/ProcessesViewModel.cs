using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitor _processMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;
    private readonly object _refreshLock = new();
    private bool _isRefreshing;

    [ObservableProperty] private ObservableCollection<ProcessInfo> _processes = new();
    [ObservableProperty] private ProcessInfo? _selectedProcess;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private int _totalProcesses = 0;
    [ObservableProperty] private double _totalCpuUsage = 0;
    [ObservableProperty] private double _totalMemoryMB = 0;

    public ProcessesViewModel(IProcessMonitor processMonitor)
    {
        _processMonitor = processMonitor;
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

        try
        {
            var allProcesses = await _processMonitor.GetAllProcessesAsync();
            if (_isDisposed) return;

            var searchText = SearchText;
            var filtered = string.IsNullOrEmpty(searchText)
                ? allProcesses
                : allProcesses.Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            var totalCount = allProcesses.Count;
            var totalCpu = allProcesses.Sum(p => p.CpuUsagePercent);
            var totalMem = allProcesses.Sum(p => p.MemoryMB);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Update collection efficiently - clear and re-add instead of recreating
                Processes.Clear();
                foreach (var proc in filtered)
                {
                    Processes.Add(proc);
                }

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

    partial void OnSearchTextChanged(string value) => _ = RefreshProcessesAsync();

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
