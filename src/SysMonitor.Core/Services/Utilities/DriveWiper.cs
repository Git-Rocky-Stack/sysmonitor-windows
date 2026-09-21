using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

using SysMonitor.Core.Helpers;

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

    /// <summary>
    /// Whether a path sits on a solid-state drive, where overwriting a file cannot promise the flash that
    /// held it has been written over.
    /// </summary>
    bool IsSolidStateDrive(string path);
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

    /// <summary>Regular files that were overwritten and deleted.</summary>
    public int FilesWiped { get; set; }

    /// <summary>Symbolic links and junctions that were removed without touching their targets.</summary>
    public int LinksRemoved { get; set; }

    /// <summary>
    /// Files whose last pass was read back and matched. A file that was wiped but not verified is listed in
    /// <see cref="FailedPaths"/>, because an overwrite nobody checked is not one to claim.
    /// </summary>
    public int FilesVerified { get; set; }

    /// <summary>Files, links, or folders that could not be wiped or removed.</summary>
    public List<string> FailedPaths { get; } = new();
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
            // Asked before anything else, including whether the file is there. The folder wipe has asked
            // this since it was written; the single-file path did not, so picking a file inside Windows or
            // Program Files in the file dialog went straight through to the overwrite. Whether the file
            // happens to exist is not the point - nothing in that place is this app's to destroy.
            if (IsProtectedLocation(filePath))
            {
                result.ErrorMessage = "The selected file is inside a protected system location and was not wiped.";
                return result;
            }

            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "File not found";
                return result;
            }

            // Opening a symbolic link opens its target, so a link must never be overwritten:
            // that would destroy a file the user did not select.
            if (File.GetAttributes(filePath).HasFlag(FileAttributes.ReparsePoint))
            {
                result.ErrorMessage = "The selected file is a symbolic link or other reparse point; its target was not wiped.";
                return result;
            }

            var (bytesWiped, passes, verified) = await WipeRegularFileAsync(filePath, method, progress, cancellationToken);
            result.Success = true;
            result.FilesWiped = 1;
            result.BytesWiped = bytesWiped;
            result.PassesCompleted = passes;
            result.FilesVerified = verified ? 1 : 0;
            if (!verified)
            {
                result.FailedPaths.Add($"{filePath} (the last pass could not be read back, so the overwrite is unconfirmed)");
            }
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Secure delete failed for {FilePath}", filePath);
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
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
            if (!Directory.Exists(root))
            {
                result.ErrorMessage = "Directory not found";
                return result;
            }

            if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                result.ErrorMessage = "The selected folder is a link to another location; select the target folder itself. Nothing was wiped.";
                return result;
            }

            if (IsProtectedLocation(root))
            {
                result.ErrorMessage = "Wiping the system drive root or Windows, Program Files, or ProgramData folders is not allowed. Nothing was wiped.";
                return result;
            }

            var plan = BuildWipePlan(root, result.FailedPaths);

            for (int i = 0; i < plan.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = plan.Files[i];
                var filesDone = i;
                var fileProgress = new Progress<double>(p => progress?.Report((filesDone + p) / plan.Files.Count));

                try
                {
                    var (bytesWiped, passes, verified) = await WipeRegularFileAsync(file, method, fileProgress, cancellationToken);
                    result.FilesWiped++;
                    result.BytesWiped += bytesWiped;
                    result.PassesCompleted = passes;
                    if (verified)
                    {
                        result.FilesVerified++;
                    }
                    else
                    {
                        result.FailedPaths.Add($"{file} (the last pass could not be read back, so the overwrite is unconfirmed)");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not wipe {FilePath}", file);
                    result.FailedPaths.Add(file);
                }
            }

            // Remove links themselves; their targets are outside the selection and are never touched.
            foreach (var (linkPath, isDirectory) in plan.Links)
            {
                try
                {
                    if (isDirectory)
                        Directory.Delete(linkPath); // non-recursive: removes only the junction/symlink
                    else
                        File.Delete(linkPath);
                    result.LinksRemoved++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove link {LinkPath}", linkPath);
                    result.FailedPaths.Add(linkPath);
                }
            }

            // Folders were collected parent-first; delete children first. Folders that still hold
            // items which could not be wiped cannot be removed and are reported.
            for (int i = plan.Directories.Count - 1; i >= 0; i--)
            {
                try
                {
                    Directory.Delete(plan.Directories[i]);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not remove folder {DirectoryPath}", plan.Directories[i]);
                    result.FailedPaths.Add(plan.Directories[i]);
                }
            }

            result.Success = result.FailedPaths.Count == 0;
            if (!result.Success)
            {
                result.ErrorMessage =
                    $"{result.FailedPaths.Count} item(s) could not be wiped or removed (first: {result.FailedPaths[0]}). " +
                    $"{result.FilesWiped} file(s) were wiped.";
            }

            _logger.LogInformation(
                "Secure delete of {Root}: {FilesWiped} files wiped, {LinksRemoved} links removed without following, {Failed} failures",
                root, result.FilesWiped, result.LinksRemoved, result.FailedPaths.Count);
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Secure delete failed for folder {DirectoryPath}", directoryPath);
            result.ErrorMessage = ex.Message;
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    /// <summary>
    /// Lists everything under <paramref name="root"/> without following symbolic links or junctions.
    /// Reparse points are returned as links to be removed, never descended into or opened.
    /// </summary>
    internal static WipePlan BuildWipePlan(string root, List<string> failedPaths)
    {
        var plan = new WipePlan();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,          // include hidden and system items
            IgnoreInaccessible = false,    // unreadable folders are reported, not silently skipped
            ReturnSpecialDirectories = false
        };

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            plan.Directories.Add(directory);

            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    if (!IsWithin(root, entry.FullName))
                        throw new InvalidOperationException($"Enumeration left the selected folder: {entry.FullName}");

                    var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        plan.Links.Add((entry.FullName, isDirectory));
                    else if (isDirectory)
                        pending.Push(entry.FullName);
                    else
                        plan.Files.Add(entry.FullName);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                failedPaths.Add(directory);
            }
        }

        return plan;
    }

    private static bool IsWithin(string root, string path) => PathHelper.IsPathWithinDirectory(path, root);

    /// <summary>
    /// Whether a path sits on a solid-state drive. Windows reports the media type of the physical disk
    /// behind a partition; anything it will not answer for is treated as not one, since the warning is only
    /// worth showing when it is known to apply.
    /// </summary>
    public bool IsSolidStateDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return false;

            var letter = root.TrimEnd('\\', ':');
            using var partitions = new System.Management.ManagementObjectSearcher(
                @"\\.\ROOT\Microsoft\Windows\Storage",
                $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'");

            foreach (var partition in partitions.Get())
            {
                var diskNumber = Convert.ToInt32(partition["DiskNumber"]);
                using var disks = new System.Management.ManagementObjectSearcher(
                    @"\\.\ROOT\Microsoft\Windows\Storage",
                    $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{diskNumber}'");

                foreach (var disk in disks.Get())
                {
                    // 4 is solid state, 3 is spinning, 0 and 5 are unspecified kinds.
                    if (Convert.ToInt32(disk["MediaType"] ?? 0) == 4) return true;
                }
            }
        }
        catch
        {
            // Best effort: nothing can be said about the media, so nothing is said.
        }

        return false;
    }

    /// <summary>
    /// True for the system drive root and for the Windows, Program Files, and ProgramData folders
    /// (or anything inside them), which a file wiper must never target.
    /// </summary>
    /// <summary>Places nothing in this app may overwrite, whether asked for one file or a whole folder.</summary>
    public static bool IsProtectedLocation(string fullPath)
    {
        var path = Path.TrimEndingDirectorySeparator(fullPath);
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!string.IsNullOrEmpty(systemRoot) &&
            string.Equals(path, Path.TrimEndingDirectorySeparator(systemRoot), StringComparison.OrdinalIgnoreCase))
            return true;

        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        return protectedFolders
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Select(Path.TrimEndingDirectorySeparator)
            .Any(folder => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || IsWithin(folder, path));
    }

    internal sealed class WipePlan
    {
        public List<string> Files { get; } = new();
        public List<(string Path, bool IsDirectory)> Links { get; } = new();
        public List<string> Directories { get; } = new();
    }

    /// <summary>Overwrites a regular (non-link) file with every pass, then renames and deletes it.</summary>
    private static async Task<(long BytesWiped, int Passes, bool Verified)> WipeRegularFileAsync(string filePath, WipeMethod method,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;
        var passes = GetPassCount(method);

        if (fileInfo.IsReadOnly)
            fileInfo.IsReadOnly = false;

        byte[]? lastPattern = null;
        for (int pass = 1; pass <= passes; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pattern = GetWipePattern(method, pass);
            var completedPasses = pass - 1;
            var passProgress = new Progress<double>(p => progress?.Report((completedPasses + p) / passes));
            await OverwriteFileAsync(filePath, fileSize, pattern, passProgress, cancellationToken);
            lastPattern = pattern;
        }

        // Read it back: a write that never reached the disk would otherwise be reported as a wipe.
        var verified = fileSize == 0 || lastPattern == null ||
                       await VerifyOverwriteAsync(filePath, lastPattern, cancellationToken);

        // Truncate, rename to a random name (obscures the original name), then delete.
        await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(0);
        }

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var randomName = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        File.Move(filePath, randomName);
        File.Delete(randomName);

        return (fileSize * passes, passes, verified);
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
                    try { File.Delete(tempPath); } catch (Exception ex) { _logger.LogDebug(ex, "WipeFreeSpaceAsync failed"); }
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

    internal static int GetPassCount(WipeMethod method) => method switch
    {
        WipeMethod.SinglePass => 1,
        WipeMethod.DoD3Pass => 3,
        WipeMethod.DoD7Pass => 7,
        WipeMethod.Gutmann => 35,
        _ => 1
    };

    internal static byte[] GetWipePattern(WipeMethod method, int pass)
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
                // Passes 1-4 and 32-35 are random; 5-31 are the patterns from the paper.
                if (pass <= 4 || pass > 31)
                {
                    RandomNumberGenerator.Fill(buffer);
                }
                else
                {
                    FillRepeating(buffer, GutmannPatterns[pass - 5]);
                }

                break;
        }

        return buffer;
    }

    /// <summary>
    /// Passes 5 to 31 of the Gutmann method, in order, as three-byte sequences repeated across the region.
    /// Taken from Peter Gutmann, "Secure Deletion of Data from Magnetic and Solid-State Memory" (1996);
    /// see https://en.wikipedia.org/wiki/Gutmann_method for the same table.
    /// The previous code wrote (pass * 17) % 256 here, which is not that method by any reading.
    /// </summary>
    internal static readonly byte[][] GutmannPatterns =
    [
        [0x55, 0x55, 0x55], [0xAA, 0xAA, 0xAA], [0x92, 0x49, 0x24], [0x49, 0x24, 0x92], [0x24, 0x92, 0x49],
        [0x00, 0x00, 0x00], [0x11, 0x11, 0x11], [0x22, 0x22, 0x22], [0x33, 0x33, 0x33], [0x44, 0x44, 0x44],
        [0x55, 0x55, 0x55], [0x66, 0x66, 0x66], [0x77, 0x77, 0x77], [0x88, 0x88, 0x88], [0x99, 0x99, 0x99],
        [0xAA, 0xAA, 0xAA], [0xBB, 0xBB, 0xBB], [0xCC, 0xCC, 0xCC], [0xDD, 0xDD, 0xDD], [0xEE, 0xEE, 0xEE],
        [0xFF, 0xFF, 0xFF], [0x92, 0x49, 0x24], [0x49, 0x24, 0x92], [0x24, 0x92, 0x49], [0x6D, 0xB6, 0xDB],
        [0xB6, 0xDB, 0x6D], [0xDB, 0x6D, 0xB6],
    ];

    /// <summary>Lays a short pattern down across a buffer, repeating it.</summary>
    internal static void FillRepeating(byte[] buffer, byte[] pattern)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = pattern[i % pattern.Length];
        }
    }

    /// <summary>
    /// Reads the file back and checks it holds what the last pass wrote. It says the write reached the disk
    /// and was not held in a cache or quietly dropped; it says nothing about what a drive keeps elsewhere.
    /// </summary>
    internal static async Task<bool> VerifyOverwriteAsync(string filePath, byte[] pattern, CancellationToken cancellationToken)
    {
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, BufferSize,
                FileOptions.SequentialScan);

            var buffer = new byte[BufferSize];
            long position = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;

                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != pattern[(int)((position + i) % pattern.Length)])
                    {
                        return false;
                    }
                }

                position += read;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
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
            // Best effort: filling the free space is the point, so running out of it is success.
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
