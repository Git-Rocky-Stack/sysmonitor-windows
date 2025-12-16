using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace SysMonitor.Core.Services.Utilities;

public interface IDriveWiper
{
    Task<WipeResult> SecureDeleteFileAsync(string filePath, WipeMethod method = WipeMethod.DoD3Pass,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<WipeResult> SecureDeleteDirectoryAsync(string directoryPath, WipeMethod method = WipeMethod.DoD3Pass,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<WipeResult> WipeFreeSpaceAsync(string driveLetter, WipeMethod method = WipeMethod.SinglePass,
        IProgress<WipeProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<long> GetFreeSpaceAsync(string driveLetter);
}

public enum WipeMethod
{
    SinglePass,      // 1 pass - zeros (fast)
    DoD3Pass,        // 3 passes - DoD 5220.22-M short
    DoD7Pass,        // 7 passes - DoD 5220.22-M extended
    Gutmann         // 35 passes - Gutmann method (paranoid)
}

public class WipeProgress
{
    public int CurrentPass { get; set; }
    public int TotalPasses { get; set; }
    public double PercentComplete { get; set; }
    public long BytesWritten { get; set; }
    public long TotalBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan EstimatedTimeRemaining { get; set; }
}

public class WipeResult
{
    public bool Success { get; set; }
    public long BytesWiped { get; set; }
    public int PassesCompleted { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DriveWiper : IDriveWiper
{
    private readonly ILogger<DriveWiper> _logger;
    private const int BufferSize = 1024 * 1024; // 1MB buffer

    public DriveWiper(ILogger<DriveWiper> logger)
    {
        _logger = logger;
    }

    public async Task<WipeResult> SecureDeleteFileAsync(string filePath, WipeMethod method = WipeMethod.DoD3Pass,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new WipeResult();
        var startTime = DateTime.Now;

        try
        {
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "File not found";
                return result;
            }

            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var passes = GetPassCount(method);

            // Remove read-only attribute if set
            if (fileInfo.IsReadOnly)
                fileInfo.IsReadOnly = false;

            // Overwrite file content
            for (int pass = 1; pass <= passes; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pattern = GetWipePattern(method, pass);
                var passProgress = new Progress<double>(p =>
                {
                    var overallProgress = ((pass - 1) + p) / passes;
                    progress?.Report(overallProgress);
                });
                await OverwriteFileAsync(filePath, fileSize, pattern, passProgress, cancellationToken);
                result.PassesCompleted = pass;
            }

            // Truncate file to 0 bytes
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write))
            {
                fs.SetLength(0);
            }

            // Rename file to random name before deletion (obscures original name)
            var directory = Path.GetDirectoryName(filePath) ?? "";
            var randomName = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            File.Move(filePath, randomName);

            // Finally delete
            File.Delete(randomName);

            result.Success = true;
            result.BytesWiped = fileSize * passes;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    public async Task<WipeResult> SecureDeleteDirectoryAsync(string directoryPath, WipeMethod method = WipeMethod.DoD3Pass,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new WipeResult();
        var startTime = DateTime.Now;

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                result.ErrorMessage = "Directory not found";
                return result;
            }

            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileProgress = new Progress<double>(p =>
                {
                    var overallProgress = (processedFiles + p) / totalFiles;
                    progress?.Report(overallProgress);
                });

                var fileResult = await SecureDeleteFileAsync(file, method, fileProgress, cancellationToken);

                if (fileResult.Success)
                {
                    result.BytesWiped += fileResult.BytesWiped;
                    result.PassesCompleted = fileResult.PassesCompleted;
                }

                processedFiles++;
            }

            // Delete empty directories
            foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories).Reverse())
            {
                try { Directory.Delete(dir); } catch { }
            }

            try { Directory.Delete(directoryPath); } catch { }

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    public async Task<WipeResult> WipeFreeSpaceAsync(string driveLetter, WipeMethod method = WipeMethod.SinglePass,
        IProgress<WipeProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new WipeResult();
        var startTime = DateTime.Now;

        try
        {
            driveLetter = driveLetter.TrimEnd('\\', ':') + ":\\";
            var driveInfo = new DriveInfo(driveLetter);

            if (!driveInfo.IsReady)
            {
                result.ErrorMessage = "Drive not ready";
                return result;
            }

            var freeSpace = driveInfo.AvailableFreeSpace;
            var passes = GetPassCount(method);
            var tempPath = Path.Combine(driveLetter, $"SysMonitor_Wipe_{Guid.NewGuid():N}.tmp");

            progress?.Report(new WipeProgress
            {
                Status = $"Wiping free space on {driveLetter} ({FormatSize(freeSpace)} free)...",
                TotalPasses = passes
            });

            for (int pass = 1; pass <= passes; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pattern = GetWipePattern(method, pass);
                var bytesWritten = await FillFreeSpaceAsync(tempPath, freeSpace, pattern, pass, passes, progress, cancellationToken);

                result.BytesWiped += bytesWritten;
                result.PassesCompleted = pass;

                // Delete temp file after each pass
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    public async Task<long> GetFreeSpaceAsync(string driveLetter)
    {
        return await Task.Run(() =>
        {
            try
            {
                driveLetter = driveLetter.TrimEnd('\\', ':') + ":\\";
                var driveInfo = new DriveInfo(driveLetter);
                return driveInfo.IsReady ? driveInfo.AvailableFreeSpace : 0;
            }
            catch
            {
                return 0;
            }
        });
    }

    private static int GetPassCount(WipeMethod method) => method switch
    {
        WipeMethod.SinglePass => 1,
        WipeMethod.DoD3Pass => 3,
        WipeMethod.DoD7Pass => 7,
        WipeMethod.Gutmann => 35,
        _ => 1
    };

    private static byte[] GetWipePattern(WipeMethod method, int pass)
    {
        var buffer = new byte[BufferSize];

        switch (method)
        {
            case WipeMethod.SinglePass:
                // All zeros
                Array.Clear(buffer, 0, buffer.Length);
                break;

            case WipeMethod.DoD3Pass:
                // Pass 1: zeros, Pass 2: ones, Pass 3: random
                if (pass == 1)
                    Array.Clear(buffer, 0, buffer.Length);
                else if (pass == 2)
                    Array.Fill(buffer, (byte)0xFF);
                else
                    RandomNumberGenerator.Fill(buffer);
                break;

            case WipeMethod.DoD7Pass:
                // Alternating patterns + random
                switch (pass)
                {
                    case 1: Array.Clear(buffer, 0, buffer.Length); break;
                    case 2: Array.Fill(buffer, (byte)0xFF); break;
                    case 3: RandomNumberGenerator.Fill(buffer); break;
                    case 4: Array.Fill(buffer, (byte)0x96); break;
                    case 5: Array.Clear(buffer, 0, buffer.Length); break;
                    case 6: Array.Fill(buffer, (byte)0xFF); break;
                    case 7: RandomNumberGenerator.Fill(buffer); break;
                }
                break;

            case WipeMethod.Gutmann:
            default:
                // Gutmann uses specific patterns for first 4 and last 4 passes, random in between
                if (pass <= 4 || pass > 31)
                    RandomNumberGenerator.Fill(buffer);
                else
                {
                    // Various specific patterns
                    var patternByte = (byte)((pass * 17) % 256);
                    Array.Fill(buffer, patternByte);
                }
                break;
        }

        return buffer;
    }

    private static async Task OverwriteFileAsync(string filePath, long fileSize, byte[] pattern,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough);

        long bytesWritten = 0;

        while (bytesWritten < fileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesToWrite = (int)Math.Min(pattern.Length, fileSize - bytesWritten);
            await fs.WriteAsync(pattern.AsMemory(0, bytesToWrite), cancellationToken);
            bytesWritten += bytesToWrite;

            if (bytesWritten % (BufferSize * 10) == 0)
            {
                progress?.Report((double)bytesWritten / fileSize);
            }
        }

        await fs.FlushAsync(cancellationToken);
    }

    private static async Task<long> FillFreeSpaceAsync(string tempPath, long targetSize, byte[] pattern,
        int currentPass, int totalPasses, IProgress<WipeProgress>? progress, CancellationToken cancellationToken)
    {
        long bytesWritten = 0;
        var startTime = DateTime.Now;

        try
        {
            await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.WriteThrough | FileOptions.DeleteOnClose);

            // Write until disk is full or we reach target
            while (bytesWritten < targetSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await fs.WriteAsync(pattern, cancellationToken);
                    bytesWritten += pattern.Length;

                    if (bytesWritten % (BufferSize * 50) == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = bytesWritten / Math.Max(elapsed.TotalSeconds, 1);
                        var remaining = TimeSpan.FromSeconds((targetSize - bytesWritten) / Math.Max(rate, 1));

                        progress?.Report(new WipeProgress
                        {
                            CurrentPass = currentPass,
                            TotalPasses = totalPasses,
                            BytesWritten = bytesWritten,
                            TotalBytes = targetSize,
                            PercentComplete = (double)bytesWritten / targetSize * 100,
                            Status = $"Pass {currentPass}/{totalPasses}: {FormatSize(bytesWritten)} written",
                            EstimatedTimeRemaining = remaining
                        });
                    }
                }
                catch (IOException)
                {
                    // Disk full - this is expected
                    break;
                }
            }

            await fs.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // Disk full during creation - expected
        }

        return bytesWritten;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
