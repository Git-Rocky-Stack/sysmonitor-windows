using System.ComponentModel;
using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// SYSTEM IMAGE is Windows Backup's image of the system drive and its boot partitions, not a copy of files. The
/// plan refuses the places wbadmin cannot write; the run hands wbadmin its arguments and reads its report.
/// </summary>
public class SystemImageTests : IDisposable
{
    private const long GB = 1L << 30;
    private const string SystemDirectory = @"C:\Windows\system32";

    private readonly TempDirectory _root = new("sysimage");
    private readonly Dictionary<string, DriveFacts> _drives = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\"] = new(true, "NTFS", 100 * GB, 250 * GB, DriveType.Fixed),
        [@"E:\"] = new(true, "NTFS", 900 * GB, 1000 * GB, DriveType.Removable),
        [@"F:\"] = new(true, "FAT32", 30 * GB, 32 * GB, DriveType.Removable),
        [@"G:\"] = new(false, "", 0, 0, DriveType.Removable),
        [@"M:\"] = new(true, "NTFS", 10 * GB, 20 * GB, DriveType.Network),
        [@"R:\"] = new(true, "UDF", 0, 4 * GB, DriveType.CDRom),
        [@"S:\"] = new(true, "NTFS", 50 * GB, 100 * GB, DriveType.Fixed),
    };

    public void Dispose() => _root.Dispose();

    private SystemImagePlan Plan(string destination) =>
        SystemImage.Plan(destination, SystemDirectory, root => _drives.GetValueOrDefault(root));

    [Fact]
    public void AnNtfsDriveTakesTheImageAtItsRootWhateverFolderWasChosen()
    {
        var plan = Plan(@"E:\Backups\Mine");

        plan.Problem.Should().BeNull();
        plan.Target.Should().Be("E:");
        plan.ImageFolder.Should().Be(@"E:\WindowsImageBackup");
        plan.SystemDrive.Should().Be("C:");
        plan.SystemUsedBytes.Should().Be(150 * GB);
        plan.TargetFreeBytes.Should().Be(900 * GB);
        plan.MayRunOutOfSpace.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Backups", "the drive it is an image of")]
    [InlineData(@"F:\", "formatted FAT32")]
    [InlineData(@"G:\", "not ready")]
    [InlineData(@"M:\", "mapped network drive")]
    [InlineData(@"R:\", "disc drive")]
    [InlineData(@"Z:\", "not ready")]
    [InlineData(@"E:\100%", "% or a quote")]
    [InlineData("", "Choose the drive")]
    [InlineData(@"\\server", "not a network folder")]
    [InlineData("backups", "not a drive or a network folder")]
    public void APlaceWindowsBackupCannotWriteIsRefusedWithTheReason(string destination, string reason) =>
        Plan(destination).Problem.Should().Contain(reason);

    [Fact]
    public void ANetworkFolderIsGivenToWindowsBackupAsItIs()
    {
        var plan = Plan(@"\\nas\backups\");

        plan.Problem.Should().BeNull();
        plan.IsNetworkTarget.Should().BeTrue();
        plan.Target.Should().Be(@"\\nas\backups");
        plan.ImageFolder.Should().Be(@"\\nas\backups\WindowsImageBackup");
        plan.MayRunOutOfSpace.Should().BeFalse("a network folder's free space cannot be read");
    }

    [Fact]
    public void ADriveSmallerThanTheSystemDriveIsWarnedAbout() =>
        Plan(@"S:\").MayRunOutOfSpace.Should().BeTrue();

    [Fact]
    public void WindowsBackupIsAskedForEveryPartitionWindowsNeedsToStart()
    {
        SystemImage.Arguments(Plan(@"E:\")).Should().Be("start backup -backupTarget:E: -include:C: -allCritical -quiet");
        SystemImage.Arguments(Plan(@"\\nas\my backups"))
            .Should().Be("start backup -backupTarget:\"\\\\nas\\my backups\" -include:C: -allCritical -quiet");
    }

    private const string Report = """
        wbadmin 1.0 - Backup command-line tool
        Creating a shadow copy of the volumes specified for backup...
        Creating a backup of volume EFI System Partition (100.00 MB), copied (100%).
        Creating a backup of volume (C:), copied (12%).
        Creating a backup of volume (C:), copied (47%).
        """;

    [Fact]
    public void ProgressIsTheLastPercentWindowsBackupReported()
    {
        SystemImage.LastPercent(Report).Should().Be(47);
        SystemImage.LastLine(Report).Should().Be("Creating a backup of volume (C:), copied (47%).");
        SystemImage.LastPercent("Creating a shadow copy...").Should().BeNull();
    }

    [Fact]
    public void AFailureIsReportedInWindowsBackupsWords()
    {
        var log = Report + "\nERROR - The backup storage location is too small.\nThe backup operation stopped before completing.";
        SystemImage.FailureLine(log, 5).Should().Be("ERROR - The backup storage location is too small.");
        SystemImage.FailureLine("", 5).Should().Contain("code 5");
    }

    private BackupService Service(Func<string, string, string, Task<int>> windowsBackup)
    {
        var service = new BackupService(Path.Combine(_root.Path, "catalog"))
        {
            SystemDirectory = SystemDirectory,
            InspectDrive = root => _drives.GetValueOrDefault(root),
            RunElevatedAsync = windowsBackup,
        };
        return service;
    }

    [Fact]
    public async Task ACompletedImageIsRecordedOnceForItsFolder()
    {
        var calls = new List<(string Program, string Arguments)>();
        var service = Service(async (program, arguments, log) =>
        {
            calls.Add((program, arguments));
            await File.WriteAllTextAsync(log, Report + "\nThe backup operation successfully completed.");
            _drives[@"E:\"] = _drives[@"E:\"] with { FreeBytes = _drives[@"E:\"].FreeBytes - 60 * GB };
            return 0;
        });

        var first = await service.CreateSystemImageAsync(@"E:\");
        var second = await service.CreateSystemImageAsync(@"E:\");

        first.Success.Should().BeTrue(first.Message);
        second.Success.Should().BeTrue(second.Message);
        calls.Should().HaveCount(2);
        calls[0].Program.Should().EndWith("wbadmin.exe");
        calls[0].Arguments.Should().Contain("-allCritical");
        second.Archive!.Type.Should().Be(BackupType.SystemImage);
        second.Archive.FilePath.Should().Be(@"E:\WindowsImageBackup");
        second.Archive.SizeBytes.Should().Be(120 * GB, "the entry grows by what each run added");
        Directory.GetFiles(Path.Combine(_root.Path, "catalog"), "*.json").Should().ContainSingle("Windows keeps every version in the one folder");
        Directory.GetFiles(Path.Combine(_root.Path, "Logs"), "wbadmin-*.log").Should().NotBeEmpty();
    }

    [Fact]
    public async Task AFailedImageSaysWhyAndRecordsNothing()
    {
        var service = Service(async (_, _, log) =>
        {
            await File.WriteAllTextAsync(log, "ERROR - The backup storage location is too small.");
            return 5;
        });

        var result = await service.CreateSystemImageAsync(@"E:\");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("The backup storage location is too small").And.Contain("wbadmin-");
        Directory.GetFiles(Path.Combine(_root.Path, "catalog"), "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task RefusingAdministratorPermissionMakesNoImage()
    {
        var service = Service((_, _, _) => throw new Win32Exception(1223));

        var result = await service.CreateSystemImageAsync(@"E:\");

        result.Success.Should().BeFalse();
        result.Status.Should().Be(BackupStatus.Cancelled);
        result.Message.Should().Contain("administrator permission");
    }

    [Fact]
    public async Task ARefusedPlanNeverStartsWindowsBackup()
    {
        var started = false;
        var service = Service((_, _, _) => { started = true; return Task.FromResult(0); });

        var result = await service.CreateSystemImageAsync(@"C:\");

        result.Success.Should().BeFalse();
        started.Should().BeFalse();
    }

    [Fact]
    public async Task CancellingAsksWindowsBackupToStop()
    {
        var finish = new TaskCompletionSource<int>();
        var calls = new List<string>();
        var service = Service((_, arguments, _) =>
        {
            calls.Add(arguments);
            if (arguments.StartsWith("stop", StringComparison.Ordinal))
            {
                finish.TrySetResult(1);
                return Task.FromResult(0);
            }
            return finish.Task;
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.CreateSystemImageAsync(@"E:\", cancellationToken: cts.Token);

        result.Status.Should().Be(BackupStatus.Cancelled);
        calls.Should().Contain("stop job -quiet");
    }

    [Fact]
    public async Task ASystemImageIsNotRestoredOrDeletedAsFiles()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root.Path, "WindowsImageBackup")).FullName;
        File.WriteAllText(Path.Combine(folder, "image.vhdx"), "disk");
        var image = new BackupArchive { Name = "System image of C:", Type = BackupType.SystemImage, FilePath = folder };
        var service = Service((_, _, _) => Task.FromResult(0));

        var restore = await service.RestoreBackupAsync(image, _root.Path, new RestoreOptions());
        var plan = await service.PrepareRestoreAsync(image, _root.Path, new RestoreOptions());
        var delete = await service.DeleteBackupAsync(image);

        restore.Success.Should().BeFalse();
        restore.Message.Should().Contain("System Image Recovery");
        plan.Problem.Should().Contain("System Image Recovery");
        delete.Success.Should().BeTrue();
        File.Exists(Path.Combine(folder, "image.vhdx")).Should().BeTrue("Windows manages the image's files");
    }
}
