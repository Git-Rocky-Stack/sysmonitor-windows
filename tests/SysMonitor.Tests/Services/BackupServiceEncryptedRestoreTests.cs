using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class BackupServiceEncryptedRestoreTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly TempDirectory _root = new("backup");
    private readonly BackupService _service;
    private readonly string _source;

    public BackupServiceEncryptedRestoreTests()
    {
        _service = new BackupService(Path.Combine(_root.Path, "catalog"));
        _source = Path.Combine(_root.Path, "source");
        _root.File(Path.Combine("source", "a.txt"), "alpha");
        _root.File(Path.Combine("source", "sub", "b.txt"), "bravo");
    }

    public void Dispose() => _root.Dispose();

    private BackupJob Job(string destinationName, BackupCompression compression, bool encrypt = true, string? password = Password) => new()
    {
        Name = "T",
        SourcePaths = [_source],
        DestinationPath = Directory.CreateDirectory(Path.Combine(_root.Path, destinationName)).FullName,
        Compression = compression,
        EnableEncryption = encrypt,
        EncryptionPassword = password
    };

    private string RestoreTarget(string name) => Directory.CreateDirectory(Path.Combine(_root.Path, name)).FullName;

    [Theory]
    [InlineData(BackupCompression.Normal)]
    [InlineData(BackupCompression.None)]
    public async Task EncryptedBackup_RestoresWithPassword(BackupCompression compression)
    {
        var backup = await _service.CreateBackupAsync(Job("dest", compression));
        backup.Success.Should().BeTrue(backup.Message);
        backup.Archive!.IsEncrypted.Should().BeTrue();
        backup.Archive.FilePath.Should().EndWith(".zip.enc");

        var target = RestoreTarget("restored");
        var tempBefore = Directory.GetFileSystemEntries(Path.GetTempPath(), "restore_*").ToHashSet();
        var restore = await _service.RestoreBackupAsync(backup.Archive, target,
            new RestoreOptions { RestoreToOriginalLocation = false, Password = Password });

        restore.Success.Should().BeTrue(restore.Message);
        File.ReadAllText(Path.Combine(target, "source", "a.txt")).Should().Be("alpha");
        File.ReadAllText(Path.Combine(target, "source", "sub", "b.txt")).Should().Be("bravo");
        Directory.GetFileSystemEntries(Path.GetTempPath(), "restore_*").Except(tempBefore).Should().BeEmpty(
            "decrypted archives and extraction folders are plaintext copies and must be removed");
    }

    [Fact]
    public async Task EncryptedBackup_WithoutPassword_FailsWithGuidance()
    {
        var backup = await _service.CreateBackupAsync(Job("dest", BackupCompression.Normal));

        var restore = await _service.RestoreBackupAsync(backup.Archive!, RestoreTarget("restored"),
            new RestoreOptions { RestoreToOriginalLocation = false });

        restore.Success.Should().BeFalse();
        restore.Message.Should().Contain("encrypted").And.Contain("password");
    }

    [Fact]
    public async Task EncryptedBackup_WrongPassword_FailsAndWritesNothing()
    {
        var backup = await _service.CreateBackupAsync(Job("dest", BackupCompression.Normal));
        var target = RestoreTarget("restored");
        var tempBefore = Directory.GetFileSystemEntries(Path.GetTempPath(), "restore_*").ToHashSet();

        var restore = await _service.RestoreBackupAsync(backup.Archive!, target,
            new RestoreOptions { RestoreToOriginalLocation = false, Password = "wrong password" });

        restore.Success.Should().BeFalse();
        restore.Message.Should().Contain("Incorrect password");
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
        Directory.GetFileSystemEntries(Path.GetTempPath(), "restore_*").Except(tempBefore).Should().BeEmpty(
            "temporary decrypted archives must be cleaned up");
    }

    [Fact]
    public async Task Backup_EncryptionEnabledWithoutPassword_FailsInsteadOfWritingPlaintext()
    {
        var backup = await _service.CreateBackupAsync(Job("dest", BackupCompression.Normal, encrypt: true, password: ""));

        backup.Success.Should().BeFalse();
        backup.Message.Should().Contain("no password");
        Directory.EnumerateFileSystemEntries(Path.Combine(_root.Path, "dest")).Should().BeEmpty();
    }

    [Fact]
    public async Task UnencryptedBackup_IsNotMarkedEncrypted_AndRestoresWithoutPassword()
    {
        var backup = await _service.CreateBackupAsync(Job("dest", BackupCompression.Normal, encrypt: false, password: null));
        backup.Archive!.IsEncrypted.Should().BeFalse();

        var target = RestoreTarget("restored");
        var restore = await _service.RestoreBackupAsync(backup.Archive, target, new RestoreOptions { RestoreToOriginalLocation = false });

        restore.Success.Should().BeTrue(restore.Message);
        File.ReadAllText(Path.Combine(target, "source", "a.txt")).Should().Be("alpha");
    }
}
