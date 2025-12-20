using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.GameMode;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class GameModeViewModel : ObservableObject
{
    private readonly IGameModeService _gameModeService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private bool _isGameModeEnabled;
    [ObservableProperty] private bool _isActivating;
    [ObservableProperty] private string _statusMessage = "Game Mode is OFF";
    [ObservableProperty] private string _statusColor = "#FFFFFF";
    [ObservableProperty] private int _lastProcessesKilled;
    [ObservableProperty] private string _lastMemoryFreed = "0 MB";
    [ObservableProperty] private bool _hasLastSession;

    public ObservableCollection<string> TargetApps { get; } = new();
    public ObservableCollection<string> KilledApps { get; } = new();

    public GameModeViewModel(IGameModeService gameModeService)
    {
        _gameModeService = gameModeService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to Game Mode changes
        _gameModeService.GameModeChanged += OnGameModeChanged;

        // Load target apps list
        foreach (var app in _gameModeService.GetTargetProcesses())
        {
            TargetApps.Add(FormatAppName(app));
        }

        // Initialize state
        IsGameModeEnabled = _gameModeService.IsEnabled;
        UpdateStatusDisplay();
    }

    private void OnGameModeChanged(object? sender, bool isEnabled)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsGameModeEnabled = isEnabled;
            UpdateStatusDisplay();
        });
    }

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
}
