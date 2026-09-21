using System.IO.Compression;
using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class BackupVerificationTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly TempDirectory _root = new("backup");
    private readonly BackupService _service;
    private readonly string _source;

    public BackupVerificationTests()
    {
        _service = new BackupService(Path.Combine(_root.Path, "catalog"));
        _source = Path.Combine(_root.Path, "source");
        _root.File(Path.Combine("source", "a.txt"), "alpha");
        _root.File(Path.Combine("source", "sub", "b.txt"), "bravo");
    }

    public void Dispose() => _root.Dispose();

    private async Task<BackupArchive> CreateBackupAsync(BackupCompression compression = BackupCompression.Normal,
        bool encrypt = false, bool verifyAfterBackup = true)
    {
        var result = await _service.CreateBackupAsync(new BackupJob
        {
            Name = "T",
            SourcePaths = [_source],
            DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, "dest-" + Guid.NewGuid().ToString("N"))).FullName,
            Compression = compression,
            EnableEncryption = encrypt,
            EncryptionPassword = encrypt ? Password : null,
            VerifyAfterBackup = verifyAfterBackup
        });
        result.Success.Should().BeTrue(result.Message);
        return result.Archive!;
    }

    [Fact]
    public async Task Verify_UnchangedBackup_Succeeds()
    {
        var archive = await CreateBackupAsync();

        var result = await _service.VerifyBackupAsync(archive);

        result.Success.Should().BeTrue(result.Message);
        result.ProcessedFiles.Should().Be(2);
    }

    [Fact]
    public async Task Verify_ChecksTheBackup_NotTheOriginalSourceFiles()
    {
        var archive = await CreateBackupAsync();
        File.WriteAllText(Path.Combine(_source, "a.txt"), "edited after the backup");
        File.Delete(Path.Combine(_source, "sub", "b.txt"));

        var result = await _service.VerifyBackupAsync(archive);

        result.Success.Should().BeTrue("the backup itself is intact: " + result.Message);
    }

    [Fact]
    public async Task Verify_AlteredEntryInsideArchive_Fails()
    {
        var archive = await CreateBackupAsync();
        using (var zip = ZipFile.Open(archive.FilePath, ZipArchiveMode.Update))
        {
            var entry = zip.Entries.Single(e => e.FullName.EndsWith("a.txt", StringComparison.OrdinalIgnoreCase));
            var name = entry.FullName;
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("tampered");
        }

        var result = await _service.VerifyBackupAsync(archive);

        result.Success.Should().BeFalse();
        result.FailedFiles.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Checksum mismatch");
    }

    [Fact]
    public async Task Verify_EntryMissingFromArchive_Fails()
    {
        var archive = await CreateBackupAsync();
        using (var zip = ZipFile.Open(archive.FilePath, ZipArchiveMode.Update))
        {
            zip.Entries.Single(e => e.FullName.EndsWith("b.txt", StringComparison.OrdinalIgnoreCase)).Delete();
        }

        var result = await _service.VerifyBackupAsync(archive);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Missing from backup");
    }

    [Fact]
    public async Task Verify_UncompressedBackupFolder_DetectsAlteredFile()
    {
        var archive = await CreateBackupAsync(BackupCompression.None);
        (await _service.VerifyBackupAsync(archive)).Success.Should().BeTrue();

        File.WriteAllText(Path.Combine(archive.FilePath, "source", "a.txt"), "tampered");

        (await _service.VerifyBackupAsync(archive)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_EncryptedBackup_RequiresCorrectPassword()
    {
        var archive = await CreateBackupAsync(encrypt: true);

        var noPassword = await _service.VerifyBackupAsync(archive);
        var wrongPassword = await _service.VerifyBackupAsync(archive, password: "wrong password");
        var rightPassword = await _service.VerifyBackupAsync(archive, password: Password);

        noPassword.Success.Should().BeFalse();
        noPassword.Message.Should().Contain("encrypted");
        wrongPassword.Success.Should().BeFalse();
        wrongPassword.Message.Should().Contain("Incorrect password");
        rightPassword.Success.Should().BeTrue(rightPassword.Message);
        rightPassword.ProcessedFiles.Should().Be(2);
    }

    [Theory]
    [InlineData(BackupCompression.Normal, false)]
    [InlineData(BackupCompression.None, false)]
    [InlineData(BackupCompression.Normal, true)]
    public async Task CreateBackup_WithVerification_MarksVerifiedOnlyAfterRealCheck(BackupCompression compression, bool encrypt)
    {
        var verified = await CreateBackupAsync(compression, encrypt, verifyAfterBackup: true);
        var unverified = await CreateBackupAsync(compression, encrypt, verifyAfterBackup: false);

        verified.IsVerified.Should().BeTrue();
        unverified.IsVerified.Should().BeFalse();
        (await _service.VerifyBackupAsync(unverified, password: encrypt ? Password : null))
            .Message.Should().Contain("without checksums");
    }
}
