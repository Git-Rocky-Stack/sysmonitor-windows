using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using SysMonitor.Core.Services.Settings;

namespace SysMonitor.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger _logger;

    // The store the whole app shares (SettingsStore). This page used to keep a private one - LocalSettings when
    // packaged, its own copy of settings.json otherwise - which nothing else read in the packaged build, and which
    // it wrote back whole on Save, erasing whatever another writer had saved since the page opened.
    private readonly ISettingsStore _settings;

    // Appearance
    [ObservableProperty] private int _selectedThemeIndex = 2; // Default to Dark

    // Behavior
    [ObservableProperty] private bool _runAtStartup = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _showNotifications = true;

    // Monitoring
    [ObservableProperty] private int _refreshInterval = 2;
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

    public SettingsViewModel(ISettingsStore settings, ILogger<SettingsViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _settings = settings;

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

    private T GetSetting<T>(string key, T defaultValue) => _settings.Get(key, defaultValue);

    private void SaveSetting<T>(string key, T value) => _settings.Set(key, value);

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

        var saved = _settings.Save();

        // Handle startup registration
        UpdateStartupRegistration();

        ShowStatus(saved ? "Settings saved successfully" : "Settings could not be saved; the log says why");
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
            // Every setting, in both builds. This used to clear LocalSettings alone, which the unpackaged build
            // never wrote, so there it cleared nothing.
            _settings.Clear();
            var saved = _settings.Save();

            // Reload defaults
            LoadSettings();

            // Settings are all this clears: the history database and the logs stay where they are.
            ShowStatus(saved
                ? "Every setting is back to its default"
                : "Settings were reset but could not be saved; the log says why");
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
            // Best effort: this needs administrator rights the app may not have, and the setting it
            // would change is not one the app depends on.
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
}
