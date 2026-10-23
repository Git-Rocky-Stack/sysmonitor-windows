using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// A backup schedule is a task in Windows Task Scheduler that starts the app with <c>--run-backup &lt;id&gt;</c>.
/// Task Scheduler is stood in for here, so this machine's own tasks are never touched.
/// </summary>
public class BackupScheduleTests : IDisposable
{
    private readonly TempDirectory _root = new("schedule");
    private readonly FakeTasks _tasks = new();
    private readonly BackupService _service;

    public BackupScheduleTests()
    {
        _service = new BackupService(Path.Combine(_root.Path, "catalog"))
        {
            TaskScheduler = _tasks,
            Executable = @"C:\Program Files\STX-1 ULTIMATE\SysMonitor.App.exe",
            AllowElevationPrompt = false,
        };
        _root.File(Path.Combine("source", "a.txt"), "alpha");
    }

    public void Dispose() => _root.Dispose();

    private sealed class FakeTasks : IBackupTaskScheduler
    {
        public Dictionary<string, (BackupSchedule Schedule, string Executable)> Registered { get; } = [];
        public bool Refuse { get; set; }
        public bool Elevated { get; set; } = true;

        public (bool Registered, bool Elevated, string? Error) Register(BackupSchedule schedule, string executable)
        {
            if (Refuse) return (false, false, "access is denied");
            Registered[schedule.Id] = (schedule, executable);
            return (true, Elevated, null);
        }

        public void Remove(string scheduleId) => Registered.Remove(scheduleId);

        public DateTime? NextRun(string scheduleId) =>
            Registered.ContainsKey(scheduleId) ? new DateTime(2030, 1, 6, 2, 0, 0) : null;
    }

    private BackupSchedule Weekly(bool encrypted = false) => new()
    {
        Name = "Docs weekly",
        Frequency = BackupFrequency.Weekly,
        DayOfWeek = DayOfWeek.Sunday,
        TimeOfDay = TimeSpan.FromHours(2),
        Job = new BackupJob
        {
            Name = "Docs weekly",
            SourcePaths = [Path.Combine(_root.Path, "source")],
            DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, "dest")).FullName,
            Compression = BackupCompression.None,
            EnableEncryption = encrypted,
            EncryptionPassword = encrypted ? "correct horse" : null,
        },
    };

    [Fact]
    public async Task AScheduleIsATaskThatStartsTheAppForThatSchedule()
    {
        var schedule = Weekly();

        var result = await _service.ScheduleBackupAsync(schedule);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("weekly on Sunday");
        _tasks.Registered.Should().ContainKey(schedule.Id);
        var saved = (await _service.GetScheduledBackupsAsync()).Should().ContainSingle().Subject;
        saved.Id.Should().Be(schedule.Id);
        saved.NextRunTime.Should().Be(new DateTime(2030, 1, 6, 2, 0, 0), "Task Scheduler's own next run is what the page shows");
        saved.RunsElevated.Should().BeTrue();
    }

    [Fact]
    public async Task AScheduleTaskSchedulerRefusesIsNotKept()
    {
        _tasks.Refuse = true;

        var result = await _service.ScheduleBackupAsync(Weekly());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("access is denied");
        (await _service.GetScheduledBackupsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAdministratorRightsTheScheduleSaysFilesInUseWillBeReported()
    {
        _tasks.Elevated = false;

        var result = await _service.ScheduleBackupAsync(Weekly());

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("files in use when it runs are reported");
    }

    [Fact]
    public async Task RunningAScheduleMakesTheBackupAndKeepsTheOutcome()
    {
        var schedule = Weekly();
        await _service.ScheduleBackupAsync(schedule);

        var result = await _service.RunScheduledBackupAsync(schedule.Id);

        result.Success.Should().BeTrue(result.Message);
        (await _service.GetBackupHistoryAsync()).Should().ContainSingle(a => a.Name == "Docs weekly");
        var saved = (await _service.GetScheduledBackupsAsync()).Single();
        saved.LastRunTime.Should().NotBeNull();
        saved.LastResult!.Success.Should().BeTrue();
        saved.LastResult.Archive.Should().BeNull("the schedule keeps the outcome, not the backup's whole manifest");
    }

    [Fact]
    public async Task StoppingAScheduleRemovesItsTaskAndLeavesItsBackups()
    {
        var schedule = Weekly();
        await _service.ScheduleBackupAsync(schedule);
        await _service.RunScheduledBackupAsync(schedule.Id);

        (await _service.RemoveScheduledBackupAsync(schedule.Id)).Should().BeTrue();

        _tasks.Registered.Should().BeEmpty();
        (await _service.GetScheduledBackupsAsync()).Should().BeEmpty();
        (await _service.GetBackupHistoryAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("..\\..\\settings")]
    [InlineData("not-a-schedule")]
    public async Task OnlyAScheduleThatExistsCanBeRun(string id)
    {
        var result = await _service.RunScheduledBackupAsync(id);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no longer exists");
    }

    [Fact]
    public async Task AnEncryptedScheduleKeepsItsPasswordProtectedAndNeverInClearText()
    {
        var schedule = Weekly(encrypted: true);

        var result = await _service.ScheduleBackupAsync(schedule);

        if (!OperatingSystem.IsWindows())
        {
            result.Success.Should().BeFalse("its password is kept by Windows' data protection");
            return;
        }

        result.Success.Should().BeTrue(result.Message);
        var file = Directory.GetFiles(Path.Combine(_root.Path, "catalog", "schedules"), "*.json").Single();
        File.ReadAllText(file).Should().NotContain("correct horse");

        var run = await _service.RunScheduledBackupAsync(schedule.Id);
        run.Success.Should().BeTrue(run.Message);
        run.Archive!.IsEncrypted.Should().BeTrue();
    }

    [Fact]
    public async Task AFileInUseIsCopiedFromAShadowCopyWhenTheJobAsksForOne()
    {
        var locked = Path.Combine(_root.Path, "source", "open.db");
        File.WriteAllText(locked, "live");
        var asked = new List<LockedFile>();
        _service.CopyLockedFilesAsync = (files, _, _) =>
        {
            asked.AddRange(files);
            foreach (var file in files) File.WriteAllText(file.Destination, "from the snapshot");
            return Task.FromResult(files.Select(f => new ShadowCopyOutcome(f, true, null)).ToList());
        };
        var job = Weekly().Job;
        job.UseVss = true;

        BackupResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await _service.CreateBackupAsync(job);

        result.Success.Should().BeTrue(result.Message);
        asked.Select(f => f.Source).Should().Equal(locked);
        File.ReadAllText(Path.Combine(result.Archive!.FilePath, "source", "open.db")).Should().Be("from the snapshot");
        result.Message.Should().Contain("copied from a shadow copy");
    }

    [Fact]
    public async Task AFileInUseIsReportedWhenTheJobDoesNotAskForAShadowCopy()
    {
        var locked = Path.Combine(_root.Path, "source", "open.db");
        File.WriteAllText(locked, "live");
        var job = Weekly().Job;
        job.UseVss = false;

        BackupResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await _service.CreateBackupAsync(job);

        result.Status.Should().Be(BackupStatus.PartialSuccess);
        result.Errors.Should().ContainSingle(e => e.FilePath == locked && e.ErrorMessage.Contains("Use Volume Shadow Copy"));
    }
}
