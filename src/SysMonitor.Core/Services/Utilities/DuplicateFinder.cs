using System.Security.Cryptography;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Finds files whose contents are the same. What it reports gets deleted, so a file is only ever called a
/// duplicate of another when the whole of it matches and it is a different file on disk.
/// </summary>
public class DuplicateFinder : IDuplicateFinder
{
    public async Task<List<DuplicateGroup>> ScanAsync(string path, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var duplicateGroups = new List<DuplicateGroup>();

        await Task.Run(() =>
        {
            try
            {
                // Phase 1: Group files by size (quick filter) - streaming approach
                progress?.Report(new ScanProgress { Status = "Indexing files by size..." });

                var filesBySize = new Dictionary<long, List<string>>();
                var filesIndexed = 0;

                // Stream files instead of loading all at once
                foreach (var filePath in FileScanning.EnumerateFiles(path, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length == 0) continue; // Skip empty files

                        if (!filesBySize.TryGetValue(fileInfo.Length, out var list))
                        {
                            list = [];
                            filesBySize[fileInfo.Length] = list;
                        }

                        list.Add(filePath);
                        filesIndexed++;

                        if (filesIndexed % 500 == 0)
                        {
                            progress?.Report(new ScanProgress
                            {
                                FilesScanned = filesIndexed,
                                Status = $"Indexed {filesIndexed} files..."
                            });
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                // Phase 2: Only check files with matching sizes
                var potentialDuplicates = filesBySize.Where(kvp => kvp.Value.Count > 1).ToList();
                var totalToCheck = potentialDuplicates.Sum(kvp => kvp.Value.Count);
                var filesChecked = 0;

                progress?.Report(new ScanProgress
                {
                    Status = $"Comparing {totalToCheck} files with matching sizes...",
                    TotalFiles = totalToCheck
                });

                foreach (var sizeGroup in potentialDuplicates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Phase 3: Calculate hashes for same-size files
                    var hashGroups = new Dictionary<string, List<DuplicateFileInfo>>();
                    var seenFiles = new Dictionary<FileIdentity, string>();

                    foreach (var filePath in sizeGroup.Value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // Two names for one file - a hard link, or a path through a junction - are not
                            // two copies, and deleting "the duplicate" would delete the only one there is.
                            var identity = FileScanning.TryGetIdentity(filePath);
                            if (identity is { } id)
                            {
                                if (seenFiles.ContainsKey(id)) continue;
                                seenFiles[id] = filePath;
                            }

                            var hash = ComputeFileHash(filePath);
                            var fileInfo = new FileInfo(filePath);

                            if (!hashGroups.ContainsKey(hash))
                                hashGroups[hash] = [];

                            hashGroups[hash].Add(new DuplicateFileInfo
                            {
                                FullPath = filePath,
                                FileName = fileInfo.Name,
                                Directory = fileInfo.DirectoryName ?? "",
                                LastModified = fileInfo.LastWriteTime,
                            });

                            filesChecked++;
                            if (filesChecked % 10 == 0)
                            {
                                progress?.Report(new ScanProgress
                                {
                                    FilesScanned = filesChecked,
                                    TotalFiles = totalToCheck,
                                    CurrentFile = fileInfo.Name,
                                    Status = $"Analyzing: {fileInfo.Name}"
                                });
                            }
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (IOException) { }
                    }

                    // Only add groups with actual duplicates
                    foreach (var hashGroup in hashGroups.Where(hg => hg.Value.Count > 1))
                    {
                        var files = hashGroup.Value.OrderBy(f => f.LastModified).ToList();

                        // The oldest is the one to keep; the rest are the copies made since.
                        files[0].IsOriginal = true;

                        duplicateGroups.Add(new DuplicateGroup
                        {
                            Hash = hashGroup.Key[..16] + "...", // Truncate for display
                            FileSize = sizeGroup.Key,
                            FormattedSize = FormatSize(sizeGroup.Key),
                            Files = files
                        });
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }, cancellationToken);

        // Sort by wasted space descending
        return duplicateGroups.OrderByDescending(g => g.WastedSpace).ToList();
    }

    /// <summary>
    /// Removes the given files to the Recycle Bin and returns how much they took up. Nothing is deleted
    /// outright: a wrong choice here costs someone their only copy.
    /// </summary>
    public async Task<long> DeleteDuplicatesAsync(IEnumerable<string> filesToDelete)
    {
        long bytesFreed = 0;

        await Task.Run(() =>
        {
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    var size = new FileInfo(filePath).Length;
                    if (FileScanning.SendToRecycleBin(filePath))
                    {
                        bytesFreed += size;
                    }
                }
                catch { }
            }
        });

        return bytesFreed;
    }

    /// <summary>
    /// Hashes the whole file. Files over 10 MB used to be judged by their first and last megabyte plus their
    /// length, which matches for every file a program writes with the same header, footer and size - videos
    /// from one camera, disk images, database files - and those were offered up for deletion.
    /// </summary>
    internal static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
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
