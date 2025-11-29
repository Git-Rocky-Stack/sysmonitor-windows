using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Optimizers;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupOptimizer _startupOptimizer;

    [ObservableProperty] private ObservableCollection<StartupItem> _startupItems = new();
    [ObservableProperty] private StartupItem? _selectedItem;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private int _enabledCount = 0;
    [ObservableProperty] private int _highImpactCount = 0;
    [ObservableProperty] private string _statusMessage = "";

    public StartupViewModel(IStartupOptimizer startupOptimizer)
    {
        _startupOptimizer = startupOptimizer;
    }

    public async Task InitializeAsync()
    {
        await RefreshStartupItemsAsync();
    }

    private async Task RefreshStartupItemsAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _startupOptimizer.GetStartupItemsAsync();
            StartupItems = new ObservableCollection<StartupItem>(items);
            EnabledCount = items.Count(i => i.IsEnabled);
            HighImpactCount = items.Count(i => i.Impact == StartupImpact.High && i.IsEnabled);
            StatusMessage = $"{EnabledCount} startup programs, {HighImpactCount} high impact";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EnableItemAsync()
    {
        if (SelectedItem == null) return;
        await _startupOptimizer.EnableStartupItemAsync(SelectedItem);
        await RefreshStartupItemsAsync();
    }

    [RelayCommand]
    private async Task DisableItemAsync()
    {
        if (SelectedItem == null) return;
        await _startupOptimizer.DisableStartupItemAsync(SelectedItem);
        await RefreshStartupItemsAsync();
    }

    [RelayCommand]
    private async Task DeleteItemAsync()
    {
        if (SelectedItem == null) return;
        await _startupOptimizer.DeleteStartupItemAsync(SelectedItem);
        await RefreshStartupItemsAsync();
    }

    [RelayCommand]
    private async Task DisableAllHighImpactAsync()
    {
        var highImpact = StartupItems.Where(i => i.Impact == StartupImpact.High && i.IsEnabled).ToList();
        foreach (var item in highImpact)
        {
            await _startupOptimizer.DisableStartupItemAsync(item);
        }
        await RefreshStartupItemsAsync();
    }
}
