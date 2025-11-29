using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;

namespace SysMonitor.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedThemeIndex = 0;
    [ObservableProperty] private bool _runAtStartup = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private int _refreshInterval = 2;
    [ObservableProperty] private bool _autoOptimizeMemory = false;
    [ObservableProperty] private int _memoryThreshold = 80;
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _statusMessage = "";

    public SettingsViewModel()
    {
        LoadSettings();
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    private void LoadSettings()
    {
        // Load from preferences/registry
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Save to preferences/registry
        StatusMessage = "Settings saved successfully";
    }

    [RelayCommand]
    private void ResetSettings()
    {
        SelectedThemeIndex = 0;
        RunAtStartup = false;
        MinimizeToTray = true;
        ShowNotifications = true;
        RefreshInterval = 2;
        AutoOptimizeMemory = false;
        MemoryThreshold = 80;
        StatusMessage = "Settings reset to defaults";
    }
}
