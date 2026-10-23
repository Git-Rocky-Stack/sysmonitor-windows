using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.TaskScheduler;
using SysMonitor.Core.Services.Utilities;

namespace SysMonitor.Core.Services.Backup;

/// <summary>Puts a backup schedule into Windows Task Scheduler, which starts the app to run it.</summary>
internal interface IBackupTaskScheduler
{
    /// <summary>
    /// Registers (or replaces) the schedule's task. <c>Elevated</c> says whether it runs with administrator
    /// rights, which a scheduled backup needs to copy files in use from a shadow copy without asking anyone.
    /// </summary>
    (bool Registered, bool Elevated, string? Error) Register(BackupSchedule schedule, string executable);

    void Remove(string scheduleId);

    DateTime? NextRun(string scheduleId);
}

/// <summary>
/// One task per schedule, in Task Scheduler's SysMonitor folder beside Scheduled Cleaning's, starting the app with
/// <c>--run-backup &lt;id&gt;</c>. It runs as the signed-in person, only while they are signed in, so no password is
/// stored with it. It asks for the person's highest rights, which Windows grants when they are an administrator
/// and the app was allowed to ask; otherwise it is registered with ordinary rights and runs all the same.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsBackupTaskScheduler : IBackupTaskScheduler
{
    private const string Folder = "SysMonitor";

    internal static string TaskName(string scheduleId) => $"Backup {scheduleId}";

    public (bool Registered, bool Elevated, string? Error) Register(BackupSchedule schedule, string executable)
    {
        try
        {
            using var service = new TaskService();
            var folder = service.GetFolder(Folder) ?? service.RootFolder.CreateFolder(Folder);
            folder.DeleteTask(TaskName(schedule.Id), exceptionOnNotExists: false);

            try
            {
                Register(service, folder, schedule, executable, TaskRunLevel.Highest);
                return (true, true, null);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
            {
                // Highest rights need an administrator, and an administrator's unelevated app may not grant them.
                Register(service, folder, schedule, executable, TaskRunLevel.LUA);
                return (true, false, null);
            }
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    private static void Register(TaskService service, TaskFolder folder, BackupSchedule schedule, string executable, TaskRunLevel runLevel)
    {
        var task = service.NewTask();
        task.RegistrationInfo.Description = $"STX-1 ULTIMATE System Monitor: back up \"{schedule.Name}\"";
        task.RegistrationInfo.Author = "STX-1 ULTIMATE System Monitor";
        task.Principal.LogonType = TaskLogonType.InteractiveToken;
        task.Principal.RunLevel = runLevel;

        task.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.StartWhenAvailable = true; // a backup missed while the PC was off runs when it is next on
        task.Settings.ExecutionTimeLimit = TimeSpan.FromHours(12);
        task.Settings.Enabled = schedule.IsEnabled;

        // Always a start in the future: Task Scheduler runs a start in the past at once (FirstRunAfter).
        var first = ScheduledCleaningService.FirstRunAfter(DateTime.Now, schedule.TimeOfDay);
        task.Triggers.Add<Trigger>(schedule.Frequency switch
        {
            BackupFrequency.Weekly => new WeeklyTrigger
            {
                StartBoundary = first,
                DaysOfWeek = (DaysOfTheWeek)(1 << (int)(schedule.DayOfWeek ?? DayOfWeek.Sunday))
            },
            BackupFrequency.Monthly => new MonthlyTrigger
            {
                StartBoundary = first,
                DaysOfMonth = [ScheduledCleaningService.DayOfMonthWithin(schedule.DayOfMonth ?? 1)]
            },
            _ => new DailyTrigger { StartBoundary = first, DaysInterval = 1 },
        });

        task.Actions.Add(new ExecAction(executable, $"--run-backup {schedule.Id}"));
        folder.RegisterTaskDefinition(TaskName(schedule.Id), task);
    }

    public void Remove(string scheduleId)
    {
        using var service = new TaskService();
        service.GetFolder(Folder)?.DeleteTask(TaskName(scheduleId), exceptionOnNotExists: false);
    }

    public DateTime? NextRun(string scheduleId)
    {
        using var service = new TaskService();
        var next = service.GetFolder(Folder)?.Tasks.FirstOrDefault(t => t.Name == TaskName(scheduleId))?.NextRunTime;
        return next is { } time && time > DateTime.MinValue ? time : null;
    }
}
