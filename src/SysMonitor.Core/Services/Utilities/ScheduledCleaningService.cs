using Microsoft.Win32.TaskScheduler;
using System.Runtime.Versioning;
using SystemTask = System.Threading.Tasks.Task;

namespace SysMonitor.Core.Services.Utilities;

public interface IScheduledCleaningService
{
    Task<ScheduledCleaningConfig> GetConfigurationAsync();
    Task<bool> SaveConfigurationAsync(ScheduledCleaningConfig config);
    Task<bool> CreateScheduledTaskAsync(ScheduledCleaningConfig config);
    Task<bool> DeleteScheduledTaskAsync();
    Task<bool> IsScheduledTaskExistsAsync();
    Task<DateTime?> GetNextRunTimeAsync();
    Task<DateTime?> GetLastRunTimeAsync();
}

public class ScheduledCleaningConfig
{
    public bool IsEnabled { get; set; }
    public CleaningSchedule Schedule { get; set; } = CleaningSchedule.Weekly;
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;
    public TimeSpan TimeOfDay { get; set; } = new TimeSpan(3, 0, 0); // 3:00 AM
    public int DayOfMonth { get; set; } = 1;

    // What to clean
    public bool CleanTempFiles { get; set; } = true;
    public bool CleanBrowserCache { get; set; } = true;
    public bool CleanRecycleBin { get; set; } = true;
    public bool CleanWindowsUpdateCache { get; set; } = false;
    public bool CleanThumbnailCache { get; set; } = true;

    // Options
    public bool ShowNotification { get; set; } = true;
    public bool OnlyWhenIdle { get; set; } = true;
    public bool WakeToRun { get; set; } = false;
    public bool RunMissedSchedule { get; set; } = true;
}

public enum CleaningSchedule
{
    Daily,
    Weekly,
    Monthly,
    OnStartup,
    OnIdle
}

[SupportedOSPlatform("windows")]
public class ScheduledCleaningService : IScheduledCleaningService
{
    private const string TaskName = "SysMonitor Scheduled Cleaning";
    private const string TaskFolder = "SysMonitor";
    private const string ConfigFileName = "scheduled_cleaning.json";

    private readonly string _configPath;
    private readonly string _executablePath;

    public ScheduledCleaningService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor");

        Directory.CreateDirectory(appDataPath);
        _configPath = Path.Combine(appDataPath, ConfigFileName);

        // Get the executable path
        _executablePath = Environment.ProcessPath ?? "";
    }

    public async Task<ScheduledCleaningConfig> GetConfigurationAsync()
    {
        return await SystemTask.Run(() =>
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    return System.Text.Json.JsonSerializer.Deserialize<ScheduledCleaningConfig>(json)
                           ?? new ScheduledCleaningConfig();
                }
            }
            catch { }

            return new ScheduledCleaningConfig();
        });
    }

    public async Task<bool> SaveConfigurationAsync(ScheduledCleaningConfig config)
    {
        try
        {
            await SystemTask.Run(() =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configPath, json);
            });

            // Update or remove the scheduled task based on config
            if (config.IsEnabled)
            {
                return await CreateScheduledTaskAsync(config);
            }
            else
            {
                return await DeleteScheduledTaskAsync();
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CreateScheduledTaskAsync(ScheduledCleaningConfig config)
    {
        try
        {
            return await SystemTask.Run(() =>
            {
                using var ts = new TaskService();

                // Delete existing task if present
                var existingTask = ts.FindTask(TaskName);
                existingTask?.Folder.DeleteTask(TaskName, false);

                // Create task folder if it doesn't exist
                var folder = ts.RootFolder;
                try
                {
                    folder = ts.GetFolder(TaskFolder) ?? ts.RootFolder.CreateFolder(TaskFolder);
                }
                catch
                {
                    folder = ts.RootFolder;
                }

                // Create new task definition
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "Automated system cleaning by SysMonitor";
                td.RegistrationInfo.Author = "SysMonitor";

                // Set principal (run with highest privileges)
                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                td.Principal.RunLevel = TaskRunLevel.Highest;

                // Settings
                td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.AllowHardTerminate = true;
                td.Settings.StartWhenAvailable = config.RunMissedSchedule;
                td.Settings.WakeToRun = config.WakeToRun;
                td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(1);
                td.Settings.DeleteExpiredTaskAfter = TimeSpan.Zero;
                td.Settings.Enabled = true;

                if (config.OnlyWhenIdle)
                {
                    td.Settings.IdleSettings.IdleDuration = TimeSpan.FromMinutes(10);
                    td.Settings.IdleSettings.WaitTimeout = TimeSpan.FromHours(1);
                    td.Settings.RunOnlyIfIdle = true;
                }

                // Create trigger based on schedule type
                switch (config.Schedule)
                {
                    case CleaningSchedule.Daily:
                        td.Triggers.Add(new DailyTrigger
                        {
                            StartBoundary = DateTime.Today.Add(config.TimeOfDay),
                            DaysInterval = 1
                        });
                        break;

                    case CleaningSchedule.Weekly:
                        td.Triggers.Add(new WeeklyTrigger
                        {
                            StartBoundary = DateTime.Today.Add(config.TimeOfDay),
                            DaysOfWeek = (DaysOfTheWeek)(1 << (int)config.DayOfWeek)
                        });
                        break;

                    case CleaningSchedule.Monthly:
                        td.Triggers.Add(new MonthlyTrigger
                        {
                            StartBoundary = DateTime.Today.Add(config.TimeOfDay),
                            DaysOfMonth = new[] { config.DayOfMonth }
                        });
                        break;

                    case CleaningSchedule.OnStartup:
                        td.Triggers.Add(new BootTrigger
                        {
                            Delay = TimeSpan.FromMinutes(5)
                        });
                        break;

                    case CleaningSchedule.OnIdle:
                        td.Triggers.Add(new IdleTrigger());
                        break;
                }

                // Build command line arguments based on what to clean
                var args = new List<string> { "--scheduled-clean" };
                if (config.CleanTempFiles) args.Add("--temp");
                if (config.CleanBrowserCache) args.Add("--browser");
                if (config.CleanRecycleBin) args.Add("--recycle");
                if (config.CleanWindowsUpdateCache) args.Add("--update");
                if (config.CleanThumbnailCache) args.Add("--thumbnails");
                if (!config.ShowNotification) args.Add("--silent");

                // Add action
                if (!string.IsNullOrEmpty(_executablePath))
                {
                    td.Actions.Add(new ExecAction(_executablePath, string.Join(" ", args)));
                }
                else
                {
                    // Fallback to PowerShell script for cleaning
                    var script = GenerateCleaningScript(config);
                    td.Actions.Add(new ExecAction("powershell.exe",
                        $"-ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{script}\""));
                }

                // Register the task
                folder.RegisterTaskDefinition(TaskName, td);
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteScheduledTaskAsync()
    {
        try
        {
            return await SystemTask.Run(() =>
            {
                using var ts = new TaskService();
                var task = ts.FindTask(TaskName);
                if (task != null)
                {
                    task.Folder.DeleteTask(TaskName, false);
                    return true;
                }
                return true; // Already doesn't exist
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsScheduledTaskExistsAsync()
    {
        try
        {
            return await SystemTask.Run(() =>
            {
                using var ts = new TaskService();
                return ts.FindTask(TaskName) != null;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<DateTime?> GetNextRunTimeAsync()
    {
        try
        {
            return await SystemTask.Run(() =>
            {
                using var ts = new TaskService();
                var task = ts.FindTask(TaskName);
                return task?.NextRunTime;
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<DateTime?> GetLastRunTimeAsync()
    {
        try
        {
            return await SystemTask.Run(() =>
            {
                using var ts = new TaskService();
                var task = ts.FindTask(TaskName);
                var lastRun = task?.LastRunTime;
                return lastRun == DateTime.MinValue ? null : lastRun;
            });
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateCleaningScript(ScheduledCleaningConfig config)
    {
        var commands = new List<string>();

        if (config.CleanTempFiles)
        {
            commands.Add("Remove-Item -Path $env:TEMP\\* -Recurse -Force -ErrorAction SilentlyContinue");
            commands.Add("Remove-Item -Path 'C:\\Windows\\Temp\\*' -Recurse -Force -ErrorAction SilentlyContinue");
        }

        if (config.CleanRecycleBin)
        {
            commands.Add("Clear-RecycleBin -Force -ErrorAction SilentlyContinue");
        }

        if (config.CleanThumbnailCache)
        {
            commands.Add("Remove-Item -Path $env:LOCALAPPDATA\\Microsoft\\Windows\\Explorer\\thumbcache_*.db -Force -ErrorAction SilentlyContinue");
        }

        if (config.CleanWindowsUpdateCache)
        {
            commands.Add("Stop-Service wuauserv -Force -ErrorAction SilentlyContinue");
            commands.Add("Remove-Item -Path 'C:\\Windows\\SoftwareDistribution\\Download\\*' -Recurse -Force -ErrorAction SilentlyContinue");
            commands.Add("Start-Service wuauserv -ErrorAction SilentlyContinue");
        }

        return string.Join("; ", commands);
    }
}
