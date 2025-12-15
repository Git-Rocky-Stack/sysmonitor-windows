using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class ScheduledCleaningViewModel : ObservableObject
{
    private readonly IScheduledCleaningService _scheduledCleaningService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = "";

    // Schedule settings
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private CleaningSchedule _selectedSchedule = CleaningSchedule.Weekly;
    [ObservableProperty] private DayOfWeek _selectedDayOfWeek = DayOfWeek.Sunday;
    [ObservableProperty] private int _selectedDayOfMonth = 1;
    [ObservableProperty] private int _selectedHour = 3;
    [ObservableProperty] private int _selectedMinute = 0;

    // What to clean
    [ObservableProperty] private bool _cleanTempFiles = true;
    [ObservableProperty] private bool _cleanBrowserCache = true;
    [ObservableProperty] private bool _cleanRecycleBin = true;
    [ObservableProperty] private bool _cleanWindowsUpdateCache;
    [ObservableProperty] private bool _cleanThumbnailCache = true;

    // Options
    [ObservableProperty] private bool _showNotification = true;
    [ObservableProperty] private bool _onlyWhenIdle = true;
    [ObservableProperty] private bool _wakeToRun;
    [ObservableProperty] private bool _runMissedSchedule = true;

    // Task info
    [ObservableProperty] private bool _taskExists;
    [ObservableProperty] private string _nextRunTime = "Not scheduled";
    [ObservableProperty] private string _lastRunTime = "Never";

    // Collections for UI
    public ObservableCollection<ScheduleOption> ScheduleOptions { get; } = new()
    {
        new ScheduleOption(CleaningSchedule.Daily, "Daily", "Run every day at the specified time"),
        new ScheduleOption(CleaningSchedule.Weekly, "Weekly", "Run once a week on the specified day"),
        new ScheduleOption(CleaningSchedule.Monthly, "Monthly", "Run once a month on the specified day"),
        new ScheduleOption(CleaningSchedule.OnStartup, "On Startup", "Run when Windows starts (with 5 min delay)"),
        new ScheduleOption(CleaningSchedule.OnIdle, "When Idle", "Run when system is idle for 10+ minutes")
    };

    public ObservableCollection<DayOfWeek> DaysOfWeek { get; } = new(Enum.GetValues<DayOfWeek>());

    public ObservableCollection<int> DaysOfMonth { get; } = new(Enumerable.Range(1, 28));

    public ObservableCollection<int> Hours { get; } = new(Enumerable.Range(0, 24));

    public ObservableCollection<int> Minutes { get; } = new(new[] { 0, 15, 30, 45 });

    public ScheduledCleaningViewModel(IScheduledCleaningService scheduledCleaningService)
    {
        _scheduledCleaningService = scheduledCleaningService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            var config = await _scheduledCleaningService.GetConfigurationAsync();
            ApplyConfig(config);

            TaskExists = await _scheduledCleaningService.IsScheduledTaskExistsAsync();

            var nextRun = await _scheduledCleaningService.GetNextRunTimeAsync();
            NextRunTime = nextRun?.ToString("g") ?? "Not scheduled";

            var lastRun = await _scheduledCleaningService.GetLastRunTimeAsync();
            LastRunTime = lastRun?.ToString("g") ?? "Never";

            StatusMessage = TaskExists ? "Scheduled task is active" : "Scheduled cleaning is disabled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        StatusMessage = "Saving settings...";

        try
        {
            var config = BuildConfig();
            var success = await _scheduledCleaningService.SaveConfigurationAsync(config);

            if (success)
            {
                TaskExists = await _scheduledCleaningService.IsScheduledTaskExistsAsync();

                if (IsEnabled)
                {
                    var nextRun = await _scheduledCleaningService.GetNextRunTimeAsync();
                    NextRunTime = nextRun?.ToString("g") ?? "Not scheduled";
                    StatusMessage = $"Scheduled cleaning enabled. Next run: {NextRunTime}";
                }
                else
                {
                    NextRunTime = "Not scheduled";
                    StatusMessage = "Scheduled cleaning disabled";
                }
            }
            else
            {
                StatusMessage = "Failed to save settings. Make sure you have administrator privileges.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        IsSaving = true;
        StatusMessage = "Removing scheduled task...";

        try
        {
            var success = await _scheduledCleaningService.DeleteScheduledTaskAsync();

            if (success)
            {
                TaskExists = false;
                IsEnabled = false;
                NextRunTime = "Not scheduled";
                StatusMessage = "Scheduled task removed";
            }
            else
            {
                StatusMessage = "Failed to remove scheduled task";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            TaskExists = await _scheduledCleaningService.IsScheduledTaskExistsAsync();

            var nextRun = await _scheduledCleaningService.GetNextRunTimeAsync();
            NextRunTime = nextRun?.ToString("g") ?? "Not scheduled";

            var lastRun = await _scheduledCleaningService.GetLastRunTimeAsync();
            LastRunTime = lastRun?.ToString("g") ?? "Never";

            StatusMessage = "Status refreshed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing: {ex.Message}";
        }
    }

    private void ApplyConfig(ScheduledCleaningConfig config)
    {
        IsEnabled = config.IsEnabled;
        SelectedSchedule = config.Schedule;
        SelectedDayOfWeek = config.DayOfWeek;
        SelectedDayOfMonth = config.DayOfMonth;
        SelectedHour = config.TimeOfDay.Hours;
        SelectedMinute = config.TimeOfDay.Minutes;

        CleanTempFiles = config.CleanTempFiles;
        CleanBrowserCache = config.CleanBrowserCache;
        CleanRecycleBin = config.CleanRecycleBin;
        CleanWindowsUpdateCache = config.CleanWindowsUpdateCache;
        CleanThumbnailCache = config.CleanThumbnailCache;

        ShowNotification = config.ShowNotification;
        OnlyWhenIdle = config.OnlyWhenIdle;
        WakeToRun = config.WakeToRun;
        RunMissedSchedule = config.RunMissedSchedule;
    }

    private ScheduledCleaningConfig BuildConfig()
    {
        return new ScheduledCleaningConfig
        {
            IsEnabled = IsEnabled,
            Schedule = SelectedSchedule,
            DayOfWeek = SelectedDayOfWeek,
            DayOfMonth = SelectedDayOfMonth,
            TimeOfDay = new TimeSpan(SelectedHour, SelectedMinute, 0),

            CleanTempFiles = CleanTempFiles,
            CleanBrowserCache = CleanBrowserCache,
            CleanRecycleBin = CleanRecycleBin,
            CleanWindowsUpdateCache = CleanWindowsUpdateCache,
            CleanThumbnailCache = CleanThumbnailCache,

            ShowNotification = ShowNotification,
            OnlyWhenIdle = OnlyWhenIdle,
            WakeToRun = WakeToRun,
            RunMissedSchedule = RunMissedSchedule
        };
    }
}

public class ScheduleOption
{
    public CleaningSchedule Schedule { get; }
    public string Name { get; }
    public string Description { get; }

    public ScheduleOption(CleaningSchedule schedule, string name, string description)
    {
        Schedule = schedule;
        Name = name;
        Description = description;
    }
}
