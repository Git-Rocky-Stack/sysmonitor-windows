using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Text.Json;
using Windows.Storage;

namespace SysMonitor.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ApplicationDataContainer? _localSettings;
    private readonly string _settingsFilePath;
    private readonly bool _useFileStorage;

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
        // Try to use ApplicationData (packaged app), fall back to file storage (unpackaged)
        _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "settings.json");

        try
        {
            _localSettings = ApplicationData.Current.LocalSettings;
            _useFileStorage = false;
        }
        catch (InvalidOperationException)
        {
            // App is running unpackaged - use file-based storage
            _localSettings = null;
            _useFileStorage = true;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

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

    private Dictionary<string, object>? _fileSettings;

    private T GetSetting<T>(string key, T defaultValue)
    {
        try
        {
            if (_useFileStorage)
            {
                // Load from file if not already loaded
                if (_fileSettings == null)
                {
                    LoadFileSettings();
                }

                if (_fileSettings != null && _fileSettings.TryGetValue(key, out var value))
                {
                    if (value is JsonElement element)
                    {
                        if (typeof(T) == typeof(int) && element.TryGetInt32(out var intVal))
                            return (T)(object)intVal;
                        if (typeof(T) == typeof(bool) && element.ValueKind == JsonValueKind.True)
                            return (T)(object)true;
                        if (typeof(T) == typeof(bool) && element.ValueKind == JsonValueKind.False)
                            return (T)(object)false;
                        if (typeof(T) == typeof(string))
                            return (T)(object)(element.GetString() ?? defaultValue?.ToString() ?? "");
                    }
                    else if (value is T typedValue)
                    {
                        return typedValue;
                    }
                }
            }
            else if (_localSettings != null)
            {
                if (_localSettings.Values.TryGetValue(key, out var value) && value is T typedValue)
                {
                    return typedValue;
                }
            }
        }
        catch
        {
            // Fall through to default
        }
        return defaultValue;
    }

    private void LoadFileSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                _fileSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }
            else
            {
                _fileSettings = new Dictionary<string, object>();
            }
        }
        catch
        {
            _fileSettings = new Dictionary<string, object>();
        }
    }

    private void SaveSetting<T>(string key, T value)
    {
        try
        {
            if (_useFileStorage)
            {
                if (_fileSettings == null)
                {
                    _fileSettings = new Dictionary<string, object>();
                }
                _fileSettings[key] = value!;
            }
            else if (_localSettings != null)
            {
                _localSettings.Values[key] = value;
            }
        }
        catch
        {
            // Silently fail - settings are not critical
        }
    }

    private void SaveAllFileSettings()
    {
        if (_useFileStorage && _fileSettings != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(_fileSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Silently fail
            }
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

        // Save to file if using file storage
        SaveAllFileSettings();

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
    private static T GetStaticSetting<T>(string key, T defaultValue)
    {
        try
        {
            // Try ApplicationData first (packaged app)
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
        }
        catch (InvalidOperationException)
        {
            // Unpackaged app - try file-based settings
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SysMonitor", "settings.json");

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var fileSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (fileSettings != null && fileSettings.TryGetValue(key, out var element))
                    {
                        if (typeof(T) == typeof(int) && element.TryGetInt32(out var intVal))
                            return (T)(object)intVal;
                        if (typeof(T) == typeof(bool))
                            return (T)(object)element.GetBoolean();
                        if (typeof(T) == typeof(string))
                            return (T)(object)(element.GetString() ?? defaultValue?.ToString() ?? "");
                    }
                }
            }
            catch { }
        }
        catch { }
        return defaultValue;
    }

    public static int GetRefreshIntervalMs()
    {
        var interval = GetStaticSetting("RefreshInterval", 2);
        return interval * 1000;
    }

    public static (int warning, int critical) GetCpuTempThresholds()
    {
        var warning = GetStaticSetting("CpuTempWarning", 75);
        var critical = GetStaticSetting("CpuTempCritical", 90);
        return (warning, critical);
    }

    public static (int warning, int critical) GetGpuTempThresholds()
    {
        var warning = GetStaticSetting("GpuTempWarning", 80);
        var critical = GetStaticSetting("GpuTempCritical", 95);
        return (warning, critical);
    }

    public static (int low, int critical) GetBatteryThresholds()
    {
        var low = GetStaticSetting("BatteryLowWarning", 20);
        var critical = GetStaticSetting("BatteryCriticalWarning", 10);
        return (low, critical);
    }
}
