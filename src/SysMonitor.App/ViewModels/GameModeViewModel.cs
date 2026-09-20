using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.GameMode;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class GameModeViewModel : ObservableObject, IDisposable
{
    private readonly IGameModeService _gameModeService;
    private readonly IAutoGameModeService _autoGameModeService;
    private readonly IProfileService _profileService;
    private readonly IFpsOverlayService _fpsOverlayService;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _isInitialized;
    private bool _isDisposed;

    // Main Game Mode
    [ObservableProperty] private bool _isGameModeEnabled;
    [ObservableProperty] private bool _isActivating;
    [ObservableProperty] private string _statusMessage = "Game Mode is OFF";
    [ObservableProperty] private string _statusColor = "#FFFFFF";
    [ObservableProperty] private int _lastBackgroundAppsAffected;

    /// <summary>
    /// Whether to ask background apps to close instead of only lowering them out of the way. Off by default:
    /// closing them can lose unsaved work, so it is the user's choice and is confirmed before it happens.
    /// </summary>
    [ObservableProperty] private bool _closeBackgroundApps;

    /// <summary>What the number beside it counted: apps lowered out of the way, or apps closed.</summary>
    [ObservableProperty] private string _lastSessionAppsLabel = "Apps Lowered";

    /// <summary>
    /// Asked before background apps are closed, with the apps that are actually running. The page puts this
    /// on screen; Game Mode does not close anything unless it comes back true.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmCloseBackgroundApps { get; set; }
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

    public ObservableCollection<string> TargetApps { get; } = new();
    public ObservableCollection<string> AffectedApps { get; } = new();
    public ObservableCollection<PerformanceProfile> Profiles { get; } = new();

    public GameModeViewModel(
        IGameModeService gameModeService,
        IAutoGameModeService autoGameModeService,
        IProfileService profileService,
        IFpsOverlayService fpsOverlayService)
    {
        _gameModeService = gameModeService;
        _autoGameModeService = autoGameModeService;
        _profileService = profileService;
        _fpsOverlayService = fpsOverlayService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to events
        _gameModeService.GameModeChanged += OnGameModeChanged;
        _autoGameModeService.GameDetected += OnGameDetected;
        _autoGameModeService.GameClosed += OnGameClosed;
        _profileService.ProfileChanged += OnProfileChanged;
    }

    /// <summary>
    /// Fills the page in. The page awaits this when it is navigated to: run from the constructor as an
    /// async void, a failure reading the profiles had nowhere to go but the top of a thread nobody was
    /// watching, which ends the process.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed) return;
        _isInitialized = true;

        try
        {
            await LoadInitialStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Game Mode could not read its settings: {ex.Message}";
            StatusColor = "#F44336";
        }
    }

    private async Task LoadInitialStateAsync()
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
    }

    /// <summary>
    /// Lets go of the services. They are singletons that outlive the page, so a view model still subscribed
    /// to them is a view model they keep alive - and with it the page, its bindings and everything those
    /// hold. The page disposes this when it navigates away, as the other pages do.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _gameModeService.GameModeChanged -= OnGameModeChanged;
        _autoGameModeService.GameDetected -= OnGameDetected;
        _autoGameModeService.GameClosed -= OnGameClosed;
        _profileService.ProfileChanged -= OnProfileChanged;

        // It points back at the page, and a page being navigated away from cannot show a dialog.
        ConfirmCloseBackgroundApps = null;
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

                AffectedApps.Clear();
                HasLastSession = false;
            }
            else
            {
                // Enable Game Mode
                var action = CloseBackgroundApps ? BackgroundAppAction.AskToClose : BackgroundAppAction.LowerPriority;

                if (action == BackgroundAppAction.AskToClose && !await ConfirmClosingAsync())
                {
                    StatusMessage = "Game Mode not started.";
                    return;
                }

                StatusMessage = "Activating Game Mode...";
                var result = await _gameModeService.EnableAsync(new GameModeOptions { BackgroundApps = action });

                if (result.Success)
                {
                    // Update last session info
                    LastBackgroundAppsAffected = result.ProcessesAffected;
                    LastMemoryFreed = FormatBytes(result.MemoryFreedBytes);
                    HasLastSession = true;
                    LastSessionAppsLabel = action == BackgroundAppAction.AskToClose ? "Apps Closed" : "Apps Lowered";

                    AffectedApps.Clear();
                    foreach (var app in result.BackgroundAppsAffected)
                    {
                        AffectedApps.Add(FormatAppName(app));
                    }

                    if (result.BackgroundAppsStillRunning.Count > 0)
                    {
                        // Left running on purpose: they had something to keep.
                        StatusMessage = $"Game Mode on. Still open: {string.Join(", ", result.BackgroundAppsStillRunning.Select(FormatAppName))}";
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

    /// <summary>
    /// Asks before closing anything, listing the apps that are actually running. Without an answer from the
    /// page, nothing is closed.
    /// </summary>
    private async Task<bool> ConfirmClosingAsync()
    {
        var running = _gameModeService.GetTargetProcesses()
            .Where(name => System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
            .Select(FormatAppName)
            .ToList();

        if (running.Count == 0)
        {
            return true;
        }

        return ConfirmCloseBackgroundApps != null && await ConfirmCloseBackgroundApps(running);
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
