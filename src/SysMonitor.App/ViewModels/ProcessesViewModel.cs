using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitor _processMonitor;
    private System.Timers.Timer? _refreshTimer;

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
    }

    public async Task InitializeAsync()
    {
        await RefreshProcessesAsync();
        _refreshTimer = new System.Timers.Timer(3000);
        _refreshTimer.Elapsed += async (s, e) => await RefreshProcessesAsync();
        _refreshTimer.Start();
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            var allProcesses = await _processMonitor.GetAllProcessesAsync();
            var filtered = string.IsNullOrEmpty(SearchText)
                ? allProcesses
                : allProcesses.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

            Processes = new ObservableCollection<ProcessInfo>(filtered);
            TotalProcesses = allProcesses.Count;
            TotalCpuUsage = allProcesses.Sum(p => p.CpuUsagePercent);
            TotalMemoryMB = allProcesses.Sum(p => p.MemoryMB);
            IsLoading = false;
        }
        catch { }
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
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
    }
}
