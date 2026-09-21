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
}
