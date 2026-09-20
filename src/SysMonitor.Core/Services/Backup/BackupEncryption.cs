using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SysMonitor.Core.Services.Backup;

/// <summary>
/// Thrown when a backup cannot be decrypted with the supplied password, or its contents were altered.
/// </summary>
public sealed class BackupPasswordException : Exception
{
    public BackupPasswordException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

/// <summary>
/// Password-based encryption for backup archive files.
/// <para>
/// Format v2 (written by this version):
/// <c>"SMBK"</c> (4 bytes) | version <c>0x02</c> (1) | PBKDF2 iterations, int32 little-endian (4) | salt (16) | IV (16)
/// | AES-256-CBC/PKCS7 ciphertext | HMAC-SHA256 tag (32) computed over every preceding byte.
/// PBKDF2-HMAC-SHA256(password, salt, iterations) yields 64 bytes: the encryption key followed by the MAC key.
/// </para>
/// <para>
/// Legacy format (written by earlier versions, read-only here): IV (16) | AES-256-CBC/PKCS7 ciphertext, with the key
/// PBKDF2-HMAC-SHA256(password, "SysMonitorBackup2024", 100000). It has no authentication tag.
/// </para>
/// </summary>
public static class BackupEncryption
{
    public const int CurrentIterations = 600_000;

    private static readonly byte[] Magic = "SMBK"u8.ToArray();
    private const byte FormatVersion = 2;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int TagSize = 32;
    private const int HeaderSize = 4 + 1 + 4 + SaltSize + IvSize;
    private const int BufferSize = 81920;

    private static readonly byte[] LegacySalt = Encoding.UTF8.GetBytes("SysMonitorBackup2024");
    private const int LegacyIterations = 100_000;

    private const string WrongPasswordMessage = "Incorrect password, or the backup file has been altered or damaged.";

    /// <summary>Encrypts <paramref name="inputPath"/> into a new file at <paramref name="outputPath"/> (format v2).</summary>
    public static async Task EncryptFileAsync(string inputPath, string outputPath, string password,
        CancellationToken cancellationToken = default, int iterations = CurrentIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var (encryptionKey, macKey) = DeriveKeys(password, salt, iterations);

        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[4] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5, 4), iterations);
        salt.CopyTo(header, 9);
        iv.CopyTo(header, 9 + SaltSize);

        var outputCreated = false;
        try
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            outputCreated = true;

            await output.WriteAsync(header, cancellationToken);
            hmac.AppendData(header);

            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                var tee = new MacTeeStream(output, hmac);
                await using (var crypto = new CryptoStream(tee, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
                await using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
                {
                    await input.CopyToAsync(crypto, cancellationToken);
                } // disposing the CryptoStream writes the final padded block through the tee
            }

            await output.WriteAsync(hmac.GetHashAndReset(), cancellationToken);
        }
        catch
        {
            // Only remove a partial file this method created; never a file that already existed.
            if (outputCreated) TryDelete(outputPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    /// <summary>
    /// Decrypts a v2 or legacy encrypted backup into a new file at <paramref name="outputPath"/>.
    /// Throws <see cref="BackupPasswordException"/> when the password is wrong or a v2 file was altered.
    /// </summary>
    public static async Task DecryptFileAsync(string inputPath, string outputPath, string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        var header = new byte[HeaderSize];
        var headerBytes = await input.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken);
        var isV2 = headerBytes == HeaderSize && header.AsSpan(0, 4).SequenceEqual(Magic) && header[4] == FormatVersion;

        var outputCreated = false;
        FileStream CreateOutput()
        {
            var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            outputCreated = true;
            return stream;
        }

        try
        {
            if (isV2)
                await DecryptV2Async(input, header, CreateOutput, password, cancellationToken);
            else
                await DecryptLegacyAsync(input, CreateOutput, password, cancellationToken);
        }
        catch
        {
            // Only remove a partial file this method created; never a file that already existed.
            if (outputCreated) TryDelete(outputPath);
            throw;
        }
    }

    private static async Task DecryptV2Async(FileStream input, byte[] header, Func<FileStream> createOutput, string password,
        CancellationToken cancellationToken)
    {
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5, 4));
        if (iterations is < 10_000 or > 10_000_000)
            throw new BackupPasswordException(WrongPasswordMessage);

        var ciphertextLength = input.Length - HeaderSize - TagSize;
        if (ciphertextLength <= 0 || ciphertextLength % 16 != 0)
            throw new BackupPasswordException(WrongPasswordMessage);

        var salt = header.AsSpan(9, SaltSize).ToArray();
        var iv = header.AsSpan(9 + SaltSize, IvSize).ToArray();
        var (encryptionKey, macKey) = DeriveKeys(password, salt, iterations);

        try
        {
            // Pass 1: authenticate header + ciphertext before any plaintext is produced.
            using (var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey))
            {
                hmac.AppendData(header);
                input.Position = HeaderSize;
                await CopyRangeAsync(input, ciphertextLength, chunk => { hmac.AppendData(chunk.Span); return ValueTask.CompletedTask; }, cancellationToken);

                var expectedTag = new byte[TagSize];
                await input.ReadExactlyAsync(expectedTag, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), expectedTag))
                    throw new BackupPasswordException(WrongPasswordMessage);
            }

            // Pass 2: decrypt.
            input.Position = HeaderSize;
            using var aes = Aes.Create();
            aes.Key = encryptionKey;
            aes.IV = iv;
            await using var output = createOutput();
            await using var crypto = new CryptoStream(output, aes.CreateDecryptor(), CryptoStreamMode.Write);
            await CopyRangeAsync(input, ciphertextLength, chunk => crypto.WriteAsync(chunk, cancellationToken), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static async Task DecryptLegacyAsync(FileStream input, Func<FileStream> createOutput, string password,
        CancellationToken cancellationToken)
    {
        input.Position = 0;
        var iv = new byte[IvSize];
        if (await input.ReadAtLeastAsync(iv, IvSize, throwOnEndOfStream: false, cancellationToken) != IvSize)
            throw new BackupPasswordException(WrongPasswordMessage);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, LegacySalt, LegacyIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            await using var output = createOutput();
            await using var crypto = new CryptoStream(output, aes.CreateDecryptor(), CryptoStreamMode.Write);
            await input.CopyToAsync(crypto, cancellationToken);
            await crypto.FlushFinalBlockAsync(cancellationToken);
        }
        catch (CryptographicException ex)
        {
            // Legacy files carry no authentication tag; a wrong password surfaces as invalid padding.
            throw new BackupPasswordException(WrongPasswordMessage, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static (byte[] EncryptionKey, byte[] MacKey) DeriveKeys(string password, byte[] salt, int iterations)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 64);
        var keys = (material[..32], material[32..]);
        CryptographicOperations.ZeroMemory(material);
        return keys;
    }

    private static async Task CopyRangeAsync(Stream input, long length, Func<ReadOnlyMemory<byte>, ValueTask> sink,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var remaining = length;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await input.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
                throw new BackupPasswordException(WrongPasswordMessage);
            await sink(buffer.AsMemory(0, read));
            remaining -= read;
        }
    }

    private static void TryDelete(string path)
    {
        // Best effort: this only removes a partial file this class just wrote, and the caller is already
        // on its way out with the real failure.
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Write-only stream that forwards bytes to an inner stream and feeds them to an HMAC.</summary>
    private sealed class MacTeeStream : Stream
    {
        private readonly Stream _inner;
        private readonly IncrementalHash _hmac;

        public MacTeeStream(Stream inner, IncrementalHash hmac)
        {
            _inner = inner;
            _hmac = hmac;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hmac.AppendData(buffer);
            _inner.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _hmac.AppendData(buffer.Span);
            return _inner.WriteAsync(buffer, cancellationToken);
        }
    }
}
