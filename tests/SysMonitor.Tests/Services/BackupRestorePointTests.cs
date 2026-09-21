using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

// Backup and Utilities each declare a RestorePointInfo; this file means System Restore's.
using RestorePointInfo = SysMonitor.Core.Services.Utilities.RestorePointInfo;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The Backup page used to build its own PowerShell command line for Checkpoint-Computer, dropping the
/// description into a single-quoted literal. An apostrophe in the description closed that literal and the
/// rest of it ran as another statement. The description now goes to System Restore as a typed parameter,
/// so there is no literal for it to close - these tests hold it there.
/// </summary>
public class BackupRestorePointTests : IDisposable
{
    private readonly TempDirectory _metadata = new("backupmeta");

    public void Dispose() => _metadata.Dispose();

    private (BackupService Service, RecordingRestoreService Restore) Build(bool succeeds = true)
    {
        var restore = new RecordingRestoreService { Succeeds = succeeds };
        return (new BackupService(_metadata.Path, logger: null, systemRestore: restore), restore);
    }

    [Theory]
    [InlineData("Before cleaning")]
    [InlineData("It's a backup")]
    [InlineData("x'; Start-Process calc.exe; '")]
    [InlineData("quote \" and backtick ` and $(dollar)")]
    public async Task TheDescriptionReachesSystemRestoreExactlyAsGiven(string description)
    {
        var (service, restore) = Build();

        await service.CreateRestorePointAsync(description);

        restore.LastDescription.Should().Be(description,
            "a typed parameter carries the text as-is; nothing quotes, escapes or re-parses it");
        restore.Calls.Should().Be(1, "the restore point is made once, by System Restore, not by a shell");
    }

    [Fact]
    public async Task TheRestorePointIsRecordedAsAModifySettingsChange()
    {
        var (service, restore) = Build();

        await service.CreateRestorePointAsync("Before cleaning");

        restore.LastType.Should().Be(RestorePointType.ModifySettings,
            "this is what the old Checkpoint-Computer call passed as MODIFY_SETTINGS");
    }

    [Fact]
    public async Task AFailureFromSystemRestoreIsReportedWithItsOwnReason()
    {
        var (service, _) = Build(succeeds: false);

        var result = await service.CreateRestorePointAsync("Before cleaning");

        result.Success.Should().BeFalse();
        result.Status.Should().Be(BackupStatus.Failed);
        result.Message.Should().Be(RecordingRestoreService.FailureMessage,
            "the reason System Restore gave is what the user needs, not a generic one this layer invents");
    }

    [Fact]
    public async Task ASuccessIsReportedAsCompleted()
    {
        var (service, _) = Build();

        var result = await service.CreateRestorePointAsync("Before cleaning");

        result.Success.Should().BeTrue();
        result.Status.Should().Be(BackupStatus.Completed);
    }

    /// <summary>Stands in for System Restore and records what it was handed. Creates nothing.</summary>
    private sealed class RecordingRestoreService : ISystemRestoreService
    {
        public const string FailureMessage = "System Restore is turned off for this drive.";

        public bool Succeeds { get; init; } = true;
        public int Calls { get; private set; }
        public string? LastDescription { get; private set; }
        public RestorePointType LastType { get; private set; }

        public Task<RestorePointResult> CreateRestorePointAsync(string description,
            RestorePointType type = RestorePointType.ApplicationInstall)
        {
            Calls++;
            LastDescription = description;
            LastType = type;

            return Task.FromResult(new RestorePointResult
            {
                Success = Succeeds,
                Message = Succeeds ? $"Restore point '{description}' created successfully." : FailureMessage
            });
        }

        public Task<List<RestorePointInfo>> GetRestorePointsAsync() => Task.FromResult(new List<RestorePointInfo>());
        public Task<bool> IsSystemRestoreEnabledAsync() => Task.FromResult(true);
        public Task<bool> EnableSystemRestoreAsync(string driveLetter = "C:") => Task.FromResult(true);
    }
}
