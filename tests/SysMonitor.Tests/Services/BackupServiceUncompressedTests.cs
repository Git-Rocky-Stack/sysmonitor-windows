using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class BackupServiceUncompressedTests : IDisposable
{
    private readonly TempDirectory _root = new("backup");
    private readonly BackupService _service;

    public BackupServiceUncompressedTests()
    {
        _service = new BackupService(Path.Combine(_root.Path, "catalog"));
        _root.File(Path.Combine("source", "a.txt"), "alpha");
        _root.File(Path.Combine("source", "sub", "b.txt"), "bravo");
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task UncompressedUnencryptedBackup_Succeeds_AndRestores()
    {
        var job = new BackupJob
        {
            Name = "T",
            SourcePaths = [Path.Combine(_root.Path, "source")],
            DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, "dest")).FullName,
            Compression = BackupCompression.None
        };

        var backup = await _service.CreateBackupAsync(job);

        backup.Success.Should().BeTrue(backup.Message);
        Directory.Exists(backup.Archive!.FilePath).Should().BeTrue("an uncompressed backup is a folder");
        var sizeOnDisk = Directory.EnumerateFiles(backup.Archive.FilePath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        backup.Archive.SizeBytes.Should().Be(sizeOnDisk).And.BeGreaterThan("alpha".Length + "bravo".Length);

        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "restored")).FullName;
        var restore = await _service.RestoreBackupAsync(backup.Archive, target, new RestoreOptions { RestoreToOriginalLocation = false });
        restore.Success.Should().BeTrue(restore.Message);
        File.ReadAllText(Path.Combine(target, "source", "sub", "b.txt")).Should().Be("bravo");
    }

    /// <summary>
    /// Two sources with one name - D:\A\Photos and E:\Photos - each keep a folder of their own in the backup.
    /// Both used to be copied into Photos\, the second over the first, so restoring wrote the wrong file back.
    /// </summary>
    [Fact]
    public async Task TwoSourcesWithOneNameAreBothKept()
    {
        _root.File(Path.Combine("first", "Photos", "same.txt"), "from the first");
        _root.File(Path.Combine("second", "Photos", "same.txt"), "from the second");
        var job = new BackupJob
        {
            Name = "Twins",
            SourcePaths = [Path.Combine(_root.Path, "first", "Photos"), Path.Combine(_root.Path, "second", "Photos")],
            DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, "dest-twins")).FullName,
            Compression = BackupCompression.None
        };

        var backup = await _service.CreateBackupAsync(job);

        backup.Success.Should().BeTrue(backup.Message);
        File.ReadAllText(Path.Combine(backup.Archive!.FilePath, "Photos", "same.txt")).Should().Be("from the first");
        File.ReadAllText(Path.Combine(backup.Archive.FilePath, "Photos-2", "same.txt")).Should().Be("from the second");
    }

    [Fact]
    public async Task AnExcludedFolderLeavesAFolderThatOnlyBeginsWithItsName()
    {
        _root.File(Path.Combine("work", "Temp", "skip.txt"), "excluded");
        _root.File(Path.Combine("work", "TempProjects", "keep.txt"), "kept");
        var job = new BackupJob
        {
            Name = "Exclude",
            SourcePaths = [Path.Combine(_root.Path, "work")],
            ExcludePaths = [Path.Combine(_root.Path, "work", "Temp")],
            DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, "dest-exclude")).FullName,
            Compression = BackupCompression.None
        };

        var backup = await _service.CreateBackupAsync(job);

        backup.Success.Should().BeTrue(backup.Message);
        File.Exists(Path.Combine(backup.Archive!.FilePath, "work", "TempProjects", "keep.txt")).Should().BeTrue();
        File.Exists(Path.Combine(backup.Archive.FilePath, "work", "Temp", "skip.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { @"C:\A\Photos", @"E:\Photos" }, new[] { "Photos", "Photos-2" })]
    [InlineData(new[] { @"D:\" }, new[] { "Drive-D" })]
    [InlineData(new[] { @"C:\Users\Me\Documents\" }, new[] { "Documents" })]
    public void EverySourceIsNamedOnceAndNeverByAnAbsolutePath(string[] sources, string[] expected) =>
        BackupService.SourceFolderNames(sources).Values.Should().Equal(expected);

    /// <summary>
    /// An incremental builds on the backups before it, so retention keeps them however old or far down the list
    /// they are (more in BackupServiceIncrementalTests).
    /// </summary>
    [Fact]
    public async Task RetentionNeverRemovesTheFullBackupLaterOnesDependOn()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root.Path, "dest-retention")).FullName;
        BackupJob Job(BackupType type) => new()
        {
            Name = "Kept",
            Type = type,
            SourcePaths = [Path.Combine(_root.Path, "source")],
            DestinationPath = destination,
            Compression = BackupCompression.None,
            MaxBackupsToKeep = 1,
        };

        var full = await _service.CreateBackupAsync(Job(BackupType.Full));
        _root.File(Path.Combine("source", "c.txt"), "charlie");
        await Task.Delay(1100); // backup folders are named to the second
        var incremental = await _service.CreateBackupAsync(Job(BackupType.Incremental));

        full.Success.Should().BeTrue(full.Message);
        incremental.Success.Should().BeTrue(incremental.Message);
        var history = await _service.GetBackupHistoryAsync();
        history.Where(a => a.Name == "Kept").Select(a => a.Id).Should().Contain(full.Archive!.Id);
        Directory.Exists(full.Archive.FilePath).Should().BeTrue();
    }
}
