using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// A backup file can be edited by anyone who can reach it - a shared drive, a memory stick - and a restore
/// used to write wherever the file inside it said. The restore decides where things go now, not the archive.
/// </summary>
public class BackupRestorePathTests : IDisposable
{
    private readonly TempDirectory _temp = new("restore-paths");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AFileGoesBackWhereItCameFrom()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var manifest = ManifestFrom(documents, Entry(@"Documents\notes.txt", Path.Combine(documents, "notes.txt")));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().ContainSingle();
        plan.Files[0].DestinationFile.Should().Be(Path.Combine(documents, "notes.txt"));
        plan.Refused.Should().BeEmpty();
        plan.DestinationFolders.Should().ContainSingle().Which.Should().Be(documents);
    }

    [Fact]
    public void AnEntryAskingToBeWrittenSomewhereElseIsRefused()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var startup = Path.Combine(_temp.Path, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        var manifest = ManifestFrom(
            documents,
            Entry(@"Documents\notes.txt", Path.Combine(documents, "notes.txt")),
            Entry(@"Documents\notes.txt", Path.Combine(startup, "evil.cmd")));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().ContainSingle("only the file that belongs inside the backed-up folder");
        plan.Refused.Should().ContainSingle();
        plan.Refused[0].FilePath.Should().Be(Path.Combine(startup, "evil.cmd"));
        plan.Refused[0].ErrorMessage.Should().Contain("outside the folders this backup was taken from");
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"Documents\..\..\elsewhere.txt")]
    [InlineData(@"C:\Windows\System32\config\sam")]
    public void AnEntryThatClimbsOutOfTheBackupIsRefused(string relativePath)
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var manifest = ManifestFrom(documents, Entry(relativePath, Path.Combine(documents, "notes.txt")));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().BeEmpty();
        plan.Refused.Should().ContainSingle().Which.ErrorMessage.Should().Contain("outside the backup");
    }

    [Fact]
    public void RestoringElsewhereKeepsEveryFileInsideTheFolderChosen()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var elsewhere = Path.Combine(_temp.Path, "Elsewhere");
        var manifest = ManifestFrom(
            documents,
            Entry(@"Documents\notes.txt", Path.Combine(documents, "notes.txt")),
            Entry(@"..\..\escape.txt", @"C:\Windows\escape.txt"));

        var plan = Plan(manifest, toOriginalLocation: false, destination: elsewhere);

        plan.Files.Should().ContainSingle();
        plan.Files[0].DestinationFile.Should().StartWith(elsewhere);
        plan.Refused.Should().ContainSingle();
    }

    [Fact]
    public void AFolderThatMerelyStartsWithTheSameLettersIsNotInsideTheBackedUpOne()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var lookalike = Path.Combine(_temp.Path, "Documents2");
        var manifest = ManifestFrom(documents, Entry(@"Documents\notes.txt", Path.Combine(lookalike, "notes.txt")));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().BeEmpty("Documents2 is a different folder from Documents");
        plan.Refused.Should().ContainSingle();
    }

    [Fact]
    public void AnEntryWithNoOriginalPlaceIsRefusedRatherThanGuessed()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var manifest = ManifestFrom(documents, Entry(@"Documents\notes.txt", ""));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().BeEmpty();
        plan.Refused.Should().ContainSingle().Which.ErrorMessage.Should().Contain("does not say where it came from");
    }

    [Fact]
    public void AnOlderBackupWithNoRecordedFoldersSaysSo()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var manifest = new BackupManifest { Files = { Entry(@"Documents\notes.txt", Path.Combine(documents, "notes.txt")) } };

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.HasRecordedSourceRoots.Should().BeFalse("its destinations can only be judged by the person restoring it");
        plan.Files.Should().ContainSingle("nothing else is known against it");
        plan.DestinationFolders.Should().ContainSingle().Which.Should().Be(documents);
    }

    [Fact]
    public void TheFoldersThatWouldBeWrittenToAreListedOnce()
    {
        var documents = Path.Combine(_temp.Path, "Documents");
        var pictures = Path.Combine(_temp.Path, "Pictures");
        var manifest = ManifestFrom(
            $"{documents};{pictures}",
            Entry(@"Documents\a.txt", Path.Combine(documents, "a.txt")),
            Entry(@"Documents\b.txt", Path.Combine(documents, "b.txt")),
            Entry(@"Pictures\c.jpg", Path.Combine(pictures, "c.jpg")));

        var plan = Plan(manifest, toOriginalLocation: true);

        plan.Files.Should().HaveCount(3);
        plan.DestinationFolders.Should().BeEquivalentTo([documents, pictures]);
    }

    [Fact]
    public async Task ABackupRecordsTheFoldersItWasTakenFrom()
    {
        var source = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "notes.txt"), "something worth keeping");

        var service = new BackupService(Path.Combine(_temp.Path, "catalog"));
        var job = new BackupJob
        {
            Name = "test",
            SourcePaths = [source],
            DestinationPath = Path.Combine(_temp.Path, "backups"),
            Compression = BackupCompression.None,
        };

        var result = await service.CreateBackupAsync(job);
        result.Success.Should().BeTrue(result.Message);

        var manifestPath = Directory.GetFiles(Path.Combine(_temp.Path, "backups"), "backup_manifest.json", SearchOption.AllDirectories).Single();
        var manifest = System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath));

        manifest!.SourceRoots.Should().BeEquivalentTo([source], "a restore has to know where these files belong");
    }

    private RestorePlan Plan(BackupManifest manifest, bool toOriginalLocation, string? destination = null) =>
        BackupService.PlanRestore(
            manifest,
            manifest.Files,
            Path.Combine(_temp.Path, "extracted"),
            destination ?? Path.Combine(_temp.Path, "restored"),
            new RestoreOptions { RestoreToOriginalLocation = toOriginalLocation });

    private static BackupManifest ManifestFrom(string sourceRoots, params BackupFileEntry[] entries) => new()
    {
        SourceRoots = sourceRoots.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Files = entries.ToList(),
    };

    private static BackupFileEntry Entry(string relativePath, string originalPath) => new()
    {
        RelativePath = relativePath,
        OriginalPath = originalPath,
        SizeBytes = 10,
    };
}
