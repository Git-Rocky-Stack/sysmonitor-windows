using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class BackupEncryptionTests
{
    // Low iteration count keeps the tests fast; the format stores the count, so decryption reads it back.
    private const int TestIterations = 10_000;
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task EncryptThenDecrypt_RestoresExactBytes()
    {
        using var dir = new TempDirectory("enc");
        var original = RandomNumberGenerator.GetBytes(300_000);
        var plain = Path.Combine(dir.Path, "plain.bin");
        await File.WriteAllBytesAsync(plain, original);

        await BackupEncryption.EncryptFileAsync(plain, Path.Combine(dir.Path, "x.enc"), Password, iterations: TestIterations);
        await BackupEncryption.DecryptFileAsync(Path.Combine(dir.Path, "x.enc"), Path.Combine(dir.Path, "out.bin"), Password);

        (await File.ReadAllBytesAsync(Path.Combine(dir.Path, "out.bin"))).Should().Equal(original);
    }

    [Fact]
    public async Task Encrypt_UsesCurrentIterationsByDefault_AndRandomSaltAndIvPerFile()
    {
        using var dir = new TempDirectory("enc");
        var plain = dir.File("plain.txt", "same content");

        await BackupEncryption.EncryptFileAsync(plain, Path.Combine(dir.Path, "a.enc"), Password);
        await BackupEncryption.EncryptFileAsync(plain, Path.Combine(dir.Path, "b.enc"), Password);

        var a = await File.ReadAllBytesAsync(Path.Combine(dir.Path, "a.enc"));
        var b = await File.ReadAllBytesAsync(Path.Combine(dir.Path, "b.enc"));
        Encoding.ASCII.GetString(a, 0, 4).Should().Be("SMBK");
        a[4].Should().Be(2);
        BitConverter.ToInt32(a, 5).Should().Be(BackupEncryption.CurrentIterations);
        a[9..41].Should().NotEqual(b[9..41], "salt and IV must be random for every file");
    }

    [Fact]
    public async Task Decrypt_WrongPassword_ThrowsAndCreatesNoOutput()
    {
        using var dir = new TempDirectory("enc");
        var plain = dir.File("plain.txt", "secret");
        var encrypted = Path.Combine(dir.Path, "x.enc");
        await BackupEncryption.EncryptFileAsync(plain, encrypted, Password, iterations: TestIterations);
        var output = Path.Combine(dir.Path, "out.txt");

        var act = () => BackupEncryption.DecryptFileAsync(encrypted, output, "wrong password");

        await act.Should().ThrowAsync<BackupPasswordException>();
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData(20)]     // salt
    [InlineData(-40)]    // ciphertext
    [InlineData(-1)]     // authentication tag
    public async Task Decrypt_AlteredFile_Throws(int offset)
    {
        using var dir = new TempDirectory("enc");
        var plain = Path.Combine(dir.Path, "plain.bin");
        await File.WriteAllBytesAsync(plain, RandomNumberGenerator.GetBytes(4096));
        var encrypted = Path.Combine(dir.Path, "x.enc");
        await BackupEncryption.EncryptFileAsync(plain, encrypted, Password, iterations: TestIterations);

        var bytes = await File.ReadAllBytesAsync(encrypted);
        var index = offset >= 0 ? offset : bytes.Length + offset;
        bytes[index] ^= 0x01;
        await File.WriteAllBytesAsync(encrypted, bytes);

        var act = () => BackupEncryption.DecryptFileAsync(encrypted, Path.Combine(dir.Path, "out.bin"), Password);
        await act.Should().ThrowAsync<BackupPasswordException>();
    }

    [Fact]
    public async Task Decrypt_LegacyFormatFromEarlierVersions_IsReadable()
    {
        using var dir = new TempDirectory("enc");
        var original = RandomNumberGenerator.GetBytes(50_000);
        var legacy = Path.Combine(dir.Path, "legacy.zip.enc");
        await File.WriteAllBytesAsync(legacy, EncryptLikeEarlierVersions(original, Password, new byte[16]));

        await BackupEncryption.DecryptFileAsync(legacy, Path.Combine(dir.Path, "out.bin"), Password);

        (await File.ReadAllBytesAsync(Path.Combine(dir.Path, "out.bin"))).Should().Equal(original);
    }

    [Fact]
    public async Task Decrypt_LegacyFormat_WrongPassword_Throws()
    {
        using var dir = new TempDirectory("enc");
        var legacy = Path.Combine(dir.Path, "legacy.zip.enc");
        await File.WriteAllBytesAsync(legacy, EncryptLikeEarlierVersions(Encoding.UTF8.GetBytes("payload"), Password, new byte[16]));

        var act = () => BackupEncryption.DecryptFileAsync(legacy, Path.Combine(dir.Path, "out.bin"), "wrong password");

        await act.Should().ThrowAsync<BackupPasswordException>();
    }

    [Fact]
    public async Task FailedOperations_NeverDeleteAPreExistingOutputFile()
    {
        using var dir = new TempDirectory("enc");
        var plain = dir.File("plain.txt", "secret");
        var encrypted = Path.Combine(dir.Path, "x.enc");
        await BackupEncryption.EncryptFileAsync(plain, encrypted, Password, iterations: TestIterations);
        var existing = dir.File("existing.txt", "keep me");

        var decryptOverExisting = () => BackupEncryption.DecryptFileAsync(encrypted, existing, Password);
        var wrongPassword = () => BackupEncryption.DecryptFileAsync(encrypted, existing, "wrong password");
        var encryptOverExisting = () => BackupEncryption.EncryptFileAsync(plain, existing, Password, iterations: TestIterations);

        await decryptOverExisting.Should().ThrowAsync<IOException>();
        await wrongPassword.Should().ThrowAsync<BackupPasswordException>();
        await encryptOverExisting.Should().ThrowAsync<IOException>();
        File.ReadAllText(existing).Should().Be("keep me");
    }

    /// <summary>The format written before this change: IV | AES-256-CBC ciphertext, constant salt, 100k iterations.</summary>
    private static byte[] EncryptLikeEarlierVersions(byte[] data, string password, byte[] iv)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes("SysMonitorBackup2024"), 100000, HashAlgorithmName.SHA256);
        using var aes = Aes.Create();
        aes.Key = pbkdf2.GetBytes(32);
        aes.IV = iv;
        using var ms = new MemoryStream();
        ms.Write(iv);
        using (var crypto = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
        {
            crypto.Write(data);
        }
        return ms.ToArray();
    }
}
