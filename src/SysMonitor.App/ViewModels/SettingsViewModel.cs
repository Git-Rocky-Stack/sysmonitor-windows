using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using Windows.Storage;

namespace SysMonitor.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ApplicationDataContainer _localSettings;

    // Appearance
    [ObservableProperty] private int _selectedThemeIndex = 2; // Default to Dark

    // Behavior
    [ObservableProperty] private bool _runAtStartup = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _showNotifications = true;

    // Monitoring
    [ObservableProperty] private int _refreshInterval = 2;
    [ObservableProperty] private bool _autoOptimizeMemory = false;
    [ObservableProperty] private int _memoryThreshold = 80;

    // Alert Thresholds
    [ObservableProperty] private bool _enableTempAlerts = true;
    [ObservableProperty] private int _cpuTempWarning = 75;
    [ObservableProperty] private int _cpuTempCritical = 90;
    [ObservableProperty] private int _gpuTempWarning = 80;
    [ObservableProperty] private int _gpuTempCritical = 95;

    // Computed Fahrenheit values for display
    public string CpuTempWarningF => $"{(CpuTempWarning * 1.8) + 32:F0}";
    public string CpuTempCriticalF => $"{(CpuTempCritical * 1.8) + 32:F0}";
    public string GpuTempWarningF => $"{(GpuTempWarning * 1.8) + 32:F0}";
    public string GpuTempCriticalF => $"{(GpuTempCritical * 1.8) + 32:F0}";

    // Notify Fahrenheit properties when Celsius values change
    partial void OnCpuTempWarningChanged(int value) => OnPropertyChanged(nameof(CpuTempWarningF));
    partial void OnCpuTempCriticalChanged(int value) => OnPropertyChanged(nameof(CpuTempCriticalF));
    partial void OnGpuTempWarningChanged(int value) => OnPropertyChanged(nameof(GpuTempWarningF));
    partial void OnGpuTempCriticalChanged(int value) => OnPropertyChanged(nameof(GpuTempCriticalF));

    [ObservableProperty] private bool _enableBatteryAlerts = true;
    [ObservableProperty] private int _batteryLowWarning = 20;
    [ObservableProperty] private int _batteryCriticalWarning = 10;

    // About
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatusMessage = false;

    public SettingsViewModel()
    {
        _localSettings = ApplicationData.Current.LocalSettings;
        LoadSettings();
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    private void LoadSettings()
    {
        // Appearance
        SelectedThemeIndex = GetSetting("ThemeIndex", 2);

        // Behavior
        RunAtStartup = GetSetting("RunAtStartup", false);
        MinimizeToTray = GetSetting("MinimizeToTray", true);
        ShowNotifications = GetSetting("ShowNotifications", true);

        // Monitoring
        RefreshInterval = GetSetting("RefreshInterval", 2);
        AutoOptimizeMemory = GetSetting("AutoOptimizeMemory", false);
        MemoryThreshold = GetSetting("MemoryThreshold", 80);

        // Alert Thresholds
        EnableTempAlerts = GetSetting("EnableTempAlerts", true);
        CpuTempWarning = GetSetting("CpuTempWarning", 75);
        CpuTempCritical = GetSetting("CpuTempCritical", 90);
        GpuTempWarning = GetSetting("GpuTempWarning", 80);
        GpuTempCritical = GetSetting("GpuTempCritical", 95);

        EnableBatteryAlerts = GetSetting("EnableBatteryAlerts", true);
        BatteryLowWarning = GetSetting("BatteryLowWarning", 20);
        BatteryCriticalWarning = GetSetting("BatteryCriticalWarning", 10);
    }

    private T GetSetting<T>(string key, T defaultValue)
    {
        try
        {
            if (_localSettings.Values.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
        }
        catch
        {
            // Fall through to default
        }
        return defaultValue;
    }

    private void SaveSetting<T>(string key, T value)
    {
        try
        {
            _localSettings.Values[key] = value;
        }
        catch
        {
            // Silently fail - settings are not critical
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Appearance
        SaveSetting("ThemeIndex", SelectedThemeIndex);

        // Behavior
        SaveSetting("RunAtStartup", RunAtStartup);
        SaveSetting("MinimizeToTray", MinimizeToTray);
        SaveSetting("ShowNotifications", ShowNotifications);

        // Monitoring
        SaveSetting("RefreshInterval", RefreshInterval);
        SaveSetting("AutoOptimizeMemory", AutoOptimizeMemory);
        SaveSetting("MemoryThreshold", MemoryThreshold);

        // Alert Thresholds
        SaveSetting("EnableTempAlerts", EnableTempAlerts);
        SaveSetting("CpuTempWarning", CpuTempWarning);
        SaveSetting("CpuTempCritical", CpuTempCritical);
        SaveSetting("GpuTempWarning", GpuTempWarning);
        SaveSetting("GpuTempCritical", GpuTempCritical);

        SaveSetting("EnableBatteryAlerts", EnableBatteryAlerts);
        SaveSetting("BatteryLowWarning", BatteryLowWarning);
        SaveSetting("BatteryCriticalWarning", BatteryCriticalWarning);

        // Handle startup registration
        UpdateStartupRegistration();

        ShowStatus("Settings saved successfully");
    }

    [RelayCommand]
    private void ResetSettings()
    {
        // Appearance
        SelectedThemeIndex = 2;

        // Behavior
        RunAtStartup = false;
        MinimizeToTray = true;
        ShowNotifications = true;

        // Monitoring
        RefreshInterval = 2;
        AutoOptimizeMemory = false;
        MemoryThreshold = 80;

        // Alert Thresholds
        EnableTempAlerts = true;
        CpuTempWarning = 75;
        CpuTempCritical = 90;
        GpuTempWarning = 80;
        GpuTempCritical = 95;

        EnableBatteryAlerts = true;
        BatteryLowWarning = 20;
        BatteryCriticalWarning = 10;

        ShowStatus("Settings reset to defaults");
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        try
        {
            // Clear local settings
            _localSettings.Values.Clear();

            // Reload defaults
            LoadSettings();

            ShowStatus("All data cleared successfully");
        }
        catch (Exception ex)
        {
            ShowStatus($"Error clearing data: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    private void UpdateStartupRegistration()
    {
        // Note: Full startup registration requires additional Windows APIs
        // This is a placeholder for the registry-based approach
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key != null)
            {
                if (RunAtStartup)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("SysMonitor", $"\"{exePath}\" --minimized");
                    }
                }
                else
                {
                    key.DeleteValue("SysMonitor", false);
                }
                key.Close();
            }
        }
        catch
        {
            // May fail without admin rights - that's okay
        }
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = true;

        // Auto-clear after 3 seconds
        Task.Delay(3000).ContinueWith(_ =>
        {
            HasStatusMessage = false;
            StatusMessage = "";
        });
    }

    // Static accessors for other ViewModels to read settings
    public static int GetRefreshIntervalMs()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue("RefreshInterval", out var value) && value is int interval)
            {
                return interval * 1000;
            }
        }
        catch { }
        return 2000; // Default 2 seconds
    }

    public static (int warning, int critical) GetCpuTempThresholds()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings;
            var warning = settings.Values.TryGetValue("CpuTempWarning", out var w) && w is int wVal ? wVal : 75;
            var critical = settings.Values.TryGetValue("CpuTempCritical", out var c) && c is int cVal ? cVal : 90;
            return (warning, critical);
        }
        catch { }
        return (75, 90);
    }

    public static (int warning, int critical) GetGpuTempThresholds()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings;
            var warning = settings.Values.TryGetValue("GpuTempWarning", out var w) && w is int wVal ? wVal : 80;
            var critical = settings.Values.TryGetValue("GpuTempCritical", out var c) && c is int cVal ? cVal : 95;
            return (warning, critical);
        }
        catch { }
        return (80, 95);
    }

    public static (int low, int critical) GetBatteryThresholds()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings;
            var low = settings.Values.TryGetValue("BatteryLowWarning", out var l) && l is int lVal ? lVal : 20;
            var critical = settings.Values.TryGetValue("BatteryCriticalWarning", out var c) && c is int cVal ? cVal : 10;
            return (low, critical);
        }
        catch { }
        return (20, 10);
    }
}
