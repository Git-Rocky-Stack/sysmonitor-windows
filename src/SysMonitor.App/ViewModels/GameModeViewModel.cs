using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.GameMode;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class GameModeViewModel : ObservableObject
{
    private readonly IGameModeService _gameModeService;
    private readonly IAutoGameModeService _autoGameModeService;
    private readonly IProfileService _profileService;
    private readonly IFpsOverlayService _fpsOverlayService;
    private readonly IRamCacheService _ramCacheService;
    private readonly DispatcherQueue _dispatcherQueue;

    // Main Game Mode
    [ObservableProperty] private bool _isGameModeEnabled;
    [ObservableProperty] private bool _isActivating;
    [ObservableProperty] private string _statusMessage = "Game Mode is OFF";
    [ObservableProperty] private string _statusColor = "#FFFFFF";
    [ObservableProperty] private int _lastProcessesKilled;
    [ObservableProperty] private string _lastMemoryFreed = "0 MB";
    [ObservableProperty] private bool _hasLastSession;

    // Auto Game Mode
    [ObservableProperty] private bool _autoModeEnabled;
    [ObservableProperty] private string _detectedGamesText = "0 games detected";
    [ObservableProperty] private bool _isAutoModeMonitoring;

    // Performance Profiles
    [ObservableProperty] private PerformanceProfile? _selectedProfile;
    [ObservableProperty] private bool _hasActiveProfile;

    // FPS Overlay
    [ObservableProperty] private bool _overlayVisible;
    [ObservableProperty] private string _overlayButtonText = "SHOW";
    [ObservableProperty] private string _overlayPositionText = "Top Right";

    // RAM Cache
    [ObservableProperty] private bool _ramCacheEnabled;
    [ObservableProperty] private double _ramCacheUsagePercent;
    [ObservableProperty] private string _ramCacheStatus = "0 / 0 MB";
    [ObservableProperty] private long _selectedCacheSize = 1024; // 1GB default

    public ObservableCollection<string> TargetApps { get; } = new();
    public ObservableCollection<string> KilledApps { get; } = new();
    public ObservableCollection<PerformanceProfile> Profiles { get; } = new();
    public ObservableCollection<long> CacheSizes { get; } = new() { 512, 1024, 2048, 4096 };

    public GameModeViewModel(
        IGameModeService gameModeService,
        IAutoGameModeService autoGameModeService,
        IProfileService profileService,
        IFpsOverlayService fpsOverlayService,
        IRamCacheService ramCacheService)
    {
        _gameModeService = gameModeService;
        _autoGameModeService = autoGameModeService;
        _profileService = profileService;
        _fpsOverlayService = fpsOverlayService;
        _ramCacheService = ramCacheService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to events
        _gameModeService.GameModeChanged += OnGameModeChanged;
        _autoGameModeService.GameDetected += OnGameDetected;
        _autoGameModeService.GameClosed += OnGameClosed;
        _profileService.ProfileChanged += OnProfileChanged;
        _ramCacheService.StatsUpdated += OnRamCacheStatsUpdated;

        // Initialize
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        // Load target apps list
        foreach (var app in _gameModeService.GetTargetProcesses())
        {
            TargetApps.Add(FormatAppName(app));
        }

        // Initialize main state
        IsGameModeEnabled = _gameModeService.IsEnabled;
        UpdateStatusDisplay();

        // Initialize Auto Mode
        AutoModeEnabled = _autoGameModeService.AutoModeEnabled;
        IsAutoModeMonitoring = _autoGameModeService.IsMonitoring;
        UpdateDetectedGamesText();

        // Load profiles
        await _profileService.LoadProfilesAsync();
        Profiles.Clear();
        foreach (var profile in _profileService.Profiles)
        {
            Profiles.Add(profile);
        }
        SelectedProfile = _profileService.ActiveProfile;
        HasActiveProfile = _profileService.ActiveProfile != null;

        // Initialize overlay state
        OverlayVisible = _fpsOverlayService.IsVisible;
        UpdateOverlayButtonText();
        UpdateOverlayPositionText();

        // Initialize RAM cache state
        RamCacheEnabled = _ramCacheService.IsEnabled;
        UpdateRamCacheStatus();
    }

    #region Event Handlers

    private void OnGameModeChanged(object? sender, bool isEnabled)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsGameModeEnabled = isEnabled;
            UpdateStatusDisplay();
        });
    }

    private void OnGameDetected(object? sender, GameDetectedEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            UpdateDetectedGamesText();
        });
    }

    private void OnGameClosed(object? sender, GameDetectedEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            UpdateDetectedGamesText();
        });
    }

    private void OnProfileChanged(object? sender, PerformanceProfile? profile)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            SelectedProfile = profile;
            HasActiveProfile = profile != null;
        });
    }

    private void OnRamCacheStatsUpdated(object? sender, RamCacheStats stats)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            RamCacheEnabled = stats.IsEnabled;
            UpdateRamCacheStatus();
        });
    }

    #endregion

    #region Main Game Mode

    private void UpdateStatusDisplay()
    {
        if (IsGameModeEnabled)
        {
            StatusMessage = "Game Mode is ON";
            StatusColor = "#4CAF50";  // Green
        }
        else
        {
            StatusMessage = "Game Mode is OFF";
            StatusColor = "#FFFFFF";  // White
        }
    }

    [RelayCommand]
    private async Task ToggleGameModeAsync()
    {
        if (IsActivating) return;

        IsActivating = true;

        try
        {
            if (IsGameModeEnabled)
            {
                // Disable Game Mode
                StatusMessage = "Restoring settings...";
                await _gameModeService.DisableAsync();

                KilledApps.Clear();
                HasLastSession = false;
            }
            else
            {
                // Enable Game Mode
                StatusMessage = "Activating Game Mode...";
                var result = await _gameModeService.EnableAsync();

                if (result.Success)
                {
                    // Update last session info
                    LastProcessesKilled = result.ProcessesKilled;
                    LastMemoryFreed = FormatBytes(result.MemoryFreedBytes);
                    HasLastSession = true;

                    // Update killed apps list
                    KilledApps.Clear();
                    foreach (var app in result.KilledProcessNames)
                    {
                        KilledApps.Add(FormatAppName(app));
                    }
                }
                else
                {
                    StatusMessage = $"Failed: {result.ErrorMessage}";
                    StatusColor = "#F44336";  // Red
                    await Task.Delay(3000);
                    UpdateStatusDisplay();
                }
            }
        }
        finally
        {
            IsActivating = false;
        }
    }

    #endregion

    #region Auto Game Mode

    private void UpdateDetectedGamesText()
    {
        var count = _autoGameModeService.RunningGames.Count;
        DetectedGamesText = count == 1 ? "1 game detected" : $"{count} games detected";
    }

    [RelayCommand]
    private async Task ToggleAutoModeAsync()
    {
        _autoGameModeService.AutoModeEnabled = !_autoGameModeService.AutoModeEnabled;
        AutoModeEnabled = _autoGameModeService.AutoModeEnabled;
        IsAutoModeMonitoring = _autoGameModeService.IsMonitoring;
        await Task.CompletedTask;
    }

    #endregion

    #region Performance Profiles

    [RelayCommand]
    private async Task ApplyProfileAsync(PerformanceProfile? profile)
    {
        if (profile == null) return;
        await _profileService.ApplyProfileAsync(profile.Id);
    }

    [RelayCommand]
    private async Task DeactivateProfileAsync()
    {
        await _profileService.DeactivateProfileAsync();
    }

    #endregion

    #region FPS Overlay

    private void UpdateOverlayButtonText()
    {
        OverlayButtonText = _fpsOverlayService.IsVisible ? "HIDE" : "SHOW";
    }

    private void UpdateOverlayPositionText()
    {
        OverlayPositionText = _fpsOverlayService.Position switch
        {
            OverlayPosition.TopLeft => "Top Left",
            OverlayPosition.TopRight => "Top Right",
            OverlayPosition.BottomLeft => "Bottom Left",
            OverlayPosition.BottomRight => "Bottom Right",
            _ => "Top Right"
        };
    }

    [RelayCommand]
    private async Task ToggleOverlayAsync()
    {
        await _fpsOverlayService.ToggleAsync();
        OverlayVisible = _fpsOverlayService.IsVisible;
        UpdateOverlayButtonText();
    }

    [RelayCommand]
    private void CycleOverlayPosition()
    {
        var positions = Enum.GetValues<OverlayPosition>();
        var currentIndex = Array.IndexOf(positions, _fpsOverlayService.Position);
        var nextIndex = (currentIndex + 1) % positions.Length;
        _fpsOverlayService.Position = positions[nextIndex];
        UpdateOverlayPositionText();
    }

    #endregion

    #region RAM Cache

    private void UpdateRamCacheStatus()
    {
        if (!_ramCacheService.IsEnabled)
        {
            RamCacheStatus = "Disabled";
            RamCacheUsagePercent = 0;
        }
        else
        {
            var usedMb = _ramCacheService.UsedSizeBytes / (1024.0 * 1024.0);
            var allocatedMb = _ramCacheService.AllocatedSizeBytes / (1024.0 * 1024.0);
            RamCacheStatus = $"{usedMb:F0} / {allocatedMb:F0} MB";
            RamCacheUsagePercent = allocatedMb > 0 ? (usedMb / allocatedMb) * 100 : 0;
        }
    }

    [RelayCommand]
    private async Task ToggleRamCacheAsync()
    {
        if (_ramCacheService.IsEnabled)
        {
            await _ramCacheService.DisableAsync();
        }
        else
        {
            await _ramCacheService.EnableAsync(SelectedCacheSize);
        }
        RamCacheEnabled = _ramCacheService.IsEnabled;
        UpdateRamCacheStatus();
    }

    [RelayCommand]
    private async Task ClearRamCacheAsync()
    {
        await _ramCacheService.ClearCacheAsync();
        UpdateRamCacheStatus();
    }

    #endregion

    #region Helpers

    private static string FormatAppName(string processName)
    {
        // Capitalize first letter
        if (string.IsNullOrEmpty(processName)) return processName;
        return char.ToUpper(processName[0]) + processName[1..];
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F0} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    #endregion
}
