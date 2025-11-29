using System.Security.Cryptography;

namespace SysMonitor.Core.Services.Utilities;

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
                foreach (var filePath in EnumerateFiles(path, cancellationToken))
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

                    foreach (var filePath in sizeGroup.Value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
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
                                IsOriginal = hashGroups[hash].Count == 0 // First found is "original"
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
                        duplicateGroups.Add(new DuplicateGroup
                        {
                            Hash = hashGroup.Key[..16] + "...", // Truncate for display
                            FileSize = sizeGroup.Key,
                            FormattedSize = FormatSize(sizeGroup.Key),
                            Files = hashGroup.Value.OrderBy(f => f.LastModified).ToList()
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

    public async Task<long> DeleteDuplicatesAsync(IEnumerable<string> filesToDelete)
    {
        long bytesFreed = 0;

        await Task.Run(() =>
        {
            foreach (var filePath in filesToDelete)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var size = fileInfo.Length;

                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        bytesFreed += size;
                    }
                }
                catch { }
            }
        });

        return bytesFreed;
    }

    private static IEnumerable<string> EnumerateFiles(string path, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(path);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDir = directories.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in files)
            {
                yield return file;
            }

            try
            {
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    // Skip system directories
                    if (dirName.StartsWith("$") || dirName == "System Volume Information" ||
                        dirName == "Windows" || dirName == "Program Files" ||
                        dirName == "Program Files (x86)" || dirName == ".git")
                        continue;

                    directories.Push(subDir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);

        // For very large files, only hash first and last 1MB
        if (stream.Length > 10 * 1024 * 1024) // > 10MB
        {
            var buffer = new byte[1024 * 1024]; // 1MB
            stream.Read(buffer, 0, buffer.Length);
            stream.Seek(-buffer.Length, SeekOrigin.End);
            var endBuffer = new byte[buffer.Length];
            stream.Read(endBuffer, 0, endBuffer.Length);

            var combinedBuffer = new byte[buffer.Length + endBuffer.Length + 8];
            Buffer.BlockCopy(buffer, 0, combinedBuffer, 0, buffer.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(stream.Length), 0, combinedBuffer, buffer.Length, 8);
            Buffer.BlockCopy(endBuffer, 0, combinedBuffer, buffer.Length + 8, endBuffer.Length);

            var hash = md5.ComputeHash(combinedBuffer);
            return BitConverter.ToString(hash).Replace("-", "");
        }

        var fullHash = md5.ComputeHash(stream);
        return BitConverter.ToString(fullHash).Replace("-", "");
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
