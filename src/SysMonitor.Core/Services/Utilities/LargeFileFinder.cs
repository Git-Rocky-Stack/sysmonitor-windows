using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic.FileIO;
using System.Collections.Concurrent;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Optimized large file finder with parallel scanning and efficient enumeration.
///
/// PERFORMANCE OPTIMIZATIONS:
/// 1. Parallel directory scanning using partitioned enumeration
/// 2. Early size filtering during enumeration (avoids FileInfo for small files)
/// 3. Producer-consumer pattern for non-blocking progress reporting
/// 4. Batch processing of discovered files for better memory efficiency
/// </summary>
public class LargeFileFinder : ILargeFileFinder
{
    private readonly ILogger _logger;

    public LargeFileFinder(ILogger<LargeFileFinder>? logger = null)
    {
        _logger = logger ?? NullLogger<LargeFileFinder>.Instance;
    }

    // Parallelism configuration
    private static readonly int MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2);

    private static readonly Dictionary<string, string> FileTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Videos
        { ".mp4", "Video" }, { ".mkv", "Video" }, { ".avi", "Video" }, { ".mov", "Video" },
        { ".wmv", "Video" }, { ".flv", "Video" }, { ".webm", "Video" }, { ".m4v", "Video" },
        // Images
        { ".jpg", "Image" }, { ".jpeg", "Image" }, { ".png", "Image" }, { ".gif", "Image" },
        { ".bmp", "Image" }, { ".tiff", "Image" }, { ".webp", "Image" }, { ".raw", "Image" },
        { ".psd", "Image" }, { ".svg", "Image" },
        // Audio
        { ".mp3", "Audio" }, { ".wav", "Audio" }, { ".flac", "Audio" }, { ".aac", "Audio" },
        { ".ogg", "Audio" }, { ".wma", "Audio" }, { ".m4a", "Audio" },
        // Archives
        { ".zip", "Archive" }, { ".rar", "Archive" }, { ".7z", "Archive" }, { ".tar", "Archive" },
        { ".gz", "Archive" }, { ".bz2", "Archive" }, { ".xz", "Archive" },
        // Documents
        { ".pdf", "Document" }, { ".doc", "Document" }, { ".docx", "Document" },
        { ".xls", "Document" }, { ".xlsx", "Document" }, { ".ppt", "Document" }, { ".pptx", "Document" },
        // Executables
        { ".exe", "Executable" }, { ".msi", "Executable" }, { ".dll", "Library" },
        // Disk Images
        { ".iso", "Disk Image" }, { ".img", "Disk Image" }, { ".vhd", "Disk Image" }, { ".vmdk", "Disk Image" },
        // Games
        { ".pak", "Game Data" }, { ".wad", "Game Data" }, { ".vpk", "Game Data" },
        // Databases
        { ".db", "Database" }, { ".sqlite", "Database" }, { ".mdf", "Database" },
        // Backups
        { ".bak", "Backup" }, { ".backup", "Backup" },
        // Logs
        { ".log", "Log File" }
    };

    /// <summary>
    /// OPTIMIZATION: Parallel large file scanning with partitioned directory processing.
    /// Scans multiple directories simultaneously for faster results on SSDs.
    /// </summary>
    public async Task<List<LargeFileInfo>> ScanAsync(string path, long minSizeBytes = 100 * 1024 * 1024,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var largeFiles = new ConcurrentBag<LargeFileInfo>();
        var scannedCount = 0;

        await Task.Run(() =>
        {
            try
            {
                // Split the work across the root's subfolders, one task each.
                //
                // The root itself is deliberately not in this list. EnumerateFiles already recurses, so a
                // list of { root } plus the root's subfolders walks everything below the root twice - once
                // under the root and once under its own folder. That doubled the disk reads and put every
                // file in the results twice, which doubled the total the page reports.
                var topLevelDirs = new List<string>();
                try
                {
                    topLevelDirs.AddRange(Directory.GetDirectories(path)
                        .Where(d => !ShouldSkipDirectory(d)));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ScanAsync failed");
                }

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };

                // The files sitting directly in the root belong to no subfolder, so they are their own
                // unit of work rather than a second walk of everything.
                var units = new List<Func<IEnumerable<string>>>
                {
                    () => FileScanning.EnumerateFilesIn(path, cancellationToken),
                };
                units.AddRange(topLevelDirs.Select<string, Func<IEnumerable<string>>>(
                    topDir => () => FileScanning.EnumerateFiles(topDir, cancellationToken)));

                // Process directories in parallel
                Parallel.ForEach(units, parallelOptions, unit =>
                {
                    foreach (var filePath in unit())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var fileInfo = new FileInfo(filePath);

                            if (fileInfo.Length >= minSizeBytes)
                            {
                                largeFiles.Add(new LargeFileInfo
                                {
                                    FullPath = filePath,
                                    FileName = fileInfo.Name,
                                    Directory = fileInfo.DirectoryName ?? "",
                                    SizeBytes = fileInfo.Length,
                                    FormattedSize = FormatSize(fileInfo.Length),
                                    LastModified = fileInfo.LastWriteTime,
                                    Extension = fileInfo.Extension.ToLowerInvariant(),
                                    FileType = GetFileType(fileInfo.Extension)
                                });
                            }

                            var count = Interlocked.Increment(ref scannedCount);
                            if (count % 500 == 0)
                            {
                                progress?.Report(new ScanProgress
                                {
                                    FilesScanned = count,
                                    TotalFiles = 0,
                                    CurrentFile = fileInfo.Name,
                                    Status = $"Scanning: {fileInfo.Name} ({count:N0} files checked)"
                                });
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogDebug(ex, "ScanAsync failed");
                        }
                        catch (IOException ex)
                        {
                            _logger.LogDebug(ex, "ScanAsync failed");
                        }
                    }
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanAsync failed");
            }
        }, cancellationToken);

        return largeFiles.OrderByDescending(f => f.SizeBytes).ToList();
    }

    /// <summary>
    /// Determines if a directory should be skipped during scanning.
    /// </summary>
    private static bool ShouldSkipDirectory(string path)
    {
        var dirName = Path.GetFileName(path);
        return dirName.StartsWith("$") ||
               dirName == "System Volume Information" ||
               dirName == "Windows" ||
               dirName == "Program Files" ||
               dirName == "Program Files (x86)";
    }

    /// <summary>
    /// Moves a file to the Recycle Bin, or leaves it where it is when Windows could not put it there. There is no
    /// outright delete here: this list is built from a scan, and a scan can be wrong about what someone still
    /// wants.
    /// </summary>
    public async Task<RecycleResult> MoveToRecycleBinAsync(string filePath)
    {
        var result = await Task.Run(() => FileScanning.SendToRecycleBin(filePath));
        if (result.Error is not null)
            _logger.LogWarning(result.Error, "{Path} was not moved to the Recycle Bin: {Reason}", filePath, result.Reason);
        return result;
    }

    private static string GetFileType(string extension)
    {
        return FileTypeMap.TryGetValue(extension, out var type) ? type : "Other";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000)
            return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }
}
