using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// An incremental holds only what changed since the backup before it - full or incremental - and restoring one
/// rebuilds the sources as they were when it was taken, from every backup in its chain.
/// </summary>
public class BackupServiceIncrementalTests : IDisposable
{
    private readonly TempDirectory _root = new("incremental");
    private readonly BackupService _service;
    private readonly string _source;
    private readonly string _destination;

    public BackupServiceIncrementalTests()
    {
        _service = new BackupService(Path.Combine(_root.Path, "catalog"));
        _source = Path.Combine(_root.Path, "source");
        _destination = Directory.CreateDirectory(Path.Combine(_root.Path, "dest")).FullName;
        Write("a.txt", "alpha", 1);
        Write(Path.Combine("sub", "b.txt"), "bravo", 1);
        Write("c.txt", "charlie", 1);
    }

    public void Dispose() => _root.Dispose();

    private void Write(string relative, string content, int day)
    {
        var path = Path.Combine(_source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Utc));
    }

    private BackupJob Job(BackupType type, string? password = null, int keep = 5) => new()
    {
        Name = "Chain",
        Type = type,
        SourcePaths = [_source],
        DestinationPath = _destination,
        Compression = BackupCompression.None,
        EnableEncryption = password != null,
        EncryptionPassword = password,
        MaxBackupsToKeep = keep,
    };

    /// <summary>Backup folders are named to the second, so each backup in a chain waits for the next one.</summary>
    private async Task<BackupResult> Backup(BackupJob job)
    {
        await Task.Delay(1100);
        var result = await _service.CreateBackupAsync(job);
        result.Success.Should().BeTrue(result.Message);
        return result;
    }

    private static IEnumerable<string> Held(BackupResult result) =>
        result.Archive!.Manifest!.Files.Select(f => Path.GetFileName(f.OriginalPath)).OrderBy(n => n);

    private async Task<string> Restore(BackupArchive archive, string? password = null)
    {
        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "restored-" + Guid.NewGuid().ToString("N"))).FullName;
        var restore = await _service.RestoreBackupAsync(archive, target,
            new RestoreOptions { RestoreToOriginalLocation = false, Password = password });
        restore.Success.Should().BeTrue(restore.Message);
        return Path.Combine(target, "source");
    }

    [Fact]
    public async Task EachIncrementalHoldsOnlyWhatChangedSinceTheBackupBeforeIt()
    {
        var full = await Backup(Job(BackupType.Full));

        Write("a.txt", "alpha two", 2);
        Write("d.txt", "delta", 2);
        var first = await Backup(Job(BackupType.Incremental));

        Write(Path.Combine("sub", "b.txt"), "bravo two", 3);
        var second = await Backup(Job(BackupType.Incremental));

        Held(full).Should().Equal("a.txt", "b.txt", "c.txt");
        Held(first).Should().Equal("a.txt", "d.txt");
        Held(second).Should().Equal("b.txt");
        first.Archive!.ParentBackupId.Should().Be(full.Archive!.Id);
        second.Archive!.ParentBackupId.Should().Be(first.Archive.Id, "the second builds on the first, not on the full backup");
    }

    [Fact]
    public async Task RestoringAnIncrementalRebuildsTheSourcesAsTheyWereWhenItWasTaken()
    {
        await Backup(Job(BackupType.Full));

        Write("a.txt", "alpha two", 2);
        Write("d.txt", "delta", 2);
        await Backup(Job(BackupType.Incremental));

        File.Delete(Path.Combine(_source, "c.txt"));
        Write(Path.Combine("sub", "b.txt"), "bravo two", 3);
        var last = await Backup(Job(BackupType.Incremental));

        var restored = await Restore(last.Archive!);

        File.ReadAllText(Path.Combine(restored, "a.txt")).Should().Be("alpha two", "from the first incremental");
        File.ReadAllText(Path.Combine(restored, "sub", "b.txt")).Should().Be("bravo two", "from the second");
        File.ReadAllText(Path.Combine(restored, "d.txt")).Should().Be("delta");
        File.Exists(Path.Combine(restored, "c.txt")).Should().BeFalse("it was deleted before the incremental was taken");
    }

    [Fact]
    public async Task RestoringTheFullBackupStillGivesTheFilesAsTheyWereThen()
    {
        var full = await Backup(Job(BackupType.Full));
        Write("a.txt", "alpha two", 2);
        await Backup(Job(BackupType.Incremental));

        var restored = await Restore(full.Archive!);

        File.ReadAllText(Path.Combine(restored, "a.txt")).Should().Be("alpha");
    }

    [Fact]
    public async Task AFilePutBackFromAnOlderCopyIsAChange()
    {
        Write("a.txt", "alpha two", 5);
        await Backup(Job(BackupType.Full));

        Write("a.txt", "alpha", 1);
        var incremental = await Backup(Job(BackupType.Incremental));

        Held(incremental).Should().Equal("a.txt");
    }

    [Fact]
    public async Task NothingChangedMeansNoNewBackup()
    {
        await Backup(Job(BackupType.Full));

        var incremental = await Backup(Job(BackupType.Incremental));

        incremental.Archive.Should().BeNull();
        incremental.Message.Should().Contain("Nothing has changed");
        (await _service.GetBackupHistoryAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task ADeletionAloneIsStillRecorded()
    {
        await Backup(Job(BackupType.Full));
        File.Delete(Path.Combine(_source, "c.txt"));

        var incremental = await Backup(Job(BackupType.Incremental));

        incremental.Archive.Should().NotBeNull();
        File.Exists(Path.Combine(await Restore(incremental.Archive!), "c.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task WithNothingToBuildOnAnIncrementalHoldsEveryFile()
    {
        var incremental = await Backup(Job(BackupType.Incremental));

        incremental.Archive!.ParentBackupId.Should().BeNull();
        Held(incremental).Should().Equal("a.txt", "b.txt", "c.txt");
        incremental.Message.Should().Contain("no earlier backup");
    }

    [Fact]
    public async Task ABackupALaterIncrementalBuildsOnCannotBeDeletedFirst()
    {
        var full = await Backup(Job(BackupType.Full));
        Write("a.txt", "alpha two", 2);
        var incremental = await Backup(Job(BackupType.Incremental));

        var refused = await _service.DeleteBackupAsync(full.Archive!);
        refused.Success.Should().BeFalse();
        Directory.Exists(full.Archive!.FilePath).Should().BeTrue();

        (await _service.DeleteBackupAsync(incremental.Archive!)).Success.Should().BeTrue();
        (await _service.DeleteBackupAsync(full.Archive)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task RetentionKeepsEveryBackupAKeptOneBuildsOn()
    {
        var full = await Backup(Job(BackupType.Full, keep: 1));
        Write("a.txt", "alpha two", 2);
        var first = await Backup(Job(BackupType.Incremental, keep: 1));
        Write("a.txt", "alpha three", 3);
        var second = await Backup(Job(BackupType.Incremental, keep: 1));

        var ids = (await _service.GetBackupHistoryAsync()).Select(a => a.Id);
        ids.Should().Contain([full.Archive!.Id, first.Archive!.Id, second.Archive!.Id]);
        File.ReadAllText(Path.Combine(await Restore(second.Archive), "a.txt")).Should().Be("alpha three");
    }

    [Fact]
    public async Task AnIncrementalWhoseChainIsBrokenSaysSoRatherThanRestoringHalf()
    {
        var full = await Backup(Job(BackupType.Full));
        Write("a.txt", "alpha two", 2);
        var incremental = await Backup(Job(BackupType.Incremental));
        Directory.Delete(full.Archive!.FilePath, true);

        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "broken")).FullName;
        var options = new RestoreOptions { RestoreToOriginalLocation = false };
        var plan = await _service.PrepareRestoreAsync(incremental.Archive!, target, options);
        var restore = await _service.RestoreBackupAsync(incremental.Archive!, target, options);

        plan.Problem.Should().Contain("cannot be restored");
        plan.Files.Should().BeEmpty();
        restore.Success.Should().BeFalse();
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
    }

    [Fact]
    public async Task ThePreviewOfAChainRestoreListsEveryFileOnce()
    {
        await Backup(Job(BackupType.Full));
        Write("a.txt", "alpha two", 2);
        var incremental = await Backup(Job(BackupType.Incremental));

        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "preview")).FullName;
        var plan = await _service.PrepareRestoreAsync(incremental.Archive!, target, new RestoreOptions { RestoreToOriginalLocation = false });

        plan.Problem.Should().BeNull();
        plan.Files.Select(f => Path.GetFileName(f.DestinationFile)).OrderBy(n => n).Should().Equal("a.txt", "b.txt", "c.txt");
    }

    [Fact]
    public async Task AnEncryptedChainUsesOnePassword()
    {
        await Backup(Job(BackupType.Full, password: "first"));
        Write("a.txt", "alpha two", 2);

        var samePassword = await Backup(Job(BackupType.Incremental, password: "first"));
        Write("a.txt", "alpha three", 3);
        var otherPassword = await Backup(Job(BackupType.Incremental, password: "second"));

        Held(samePassword).Should().Equal("a.txt");
        File.ReadAllText(Path.Combine(await Restore(samePassword.Archive!, "first"), "a.txt")).Should().Be("alpha two");

        otherPassword.Archive!.ParentBackupId.Should().BeNull("it cannot be opened with the chain's password");
        Held(otherPassword).Should().Equal("a.txt", "b.txt", "c.txt");
        otherPassword.Message.Should().Contain("different password");
    }
}
