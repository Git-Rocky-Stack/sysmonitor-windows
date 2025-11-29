using Microsoft.VisualBasic.FileIO;

namespace SysMonitor.Core.Services.Utilities;

public class LargeFileFinder : ILargeFileFinder
{
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

    public async Task<List<LargeFileInfo>> ScanAsync(string path, long minSizeBytes = 100 * 1024 * 1024,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var largeFiles = new List<LargeFileInfo>();

        await Task.Run(() =>
        {
            try
            {
                var allFiles = GetAllFiles(path, cancellationToken);
                var totalFiles = allFiles.Count;
                var scanned = 0;

                foreach (var filePath in allFiles)
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

                        scanned++;
                        if (scanned % 100 == 0 || scanned == totalFiles)
                        {
                            progress?.Report(new ScanProgress
                            {
                                FilesScanned = scanned,
                                TotalFiles = totalFiles,
                                CurrentFile = fileInfo.Name,
                                Status = $"Scanning: {fileInfo.Name}"
                            });
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }, cancellationToken);

        return largeFiles.OrderByDescending(f => f.SizeBytes).ToList();
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> MoveToRecycleBinAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    private static List<string> GetAllFiles(string path, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(path);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDir = directories.Pop();

            try
            {
                files.AddRange(Directory.GetFiles(currentDir));

                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    // Skip system directories
                    var dirName = Path.GetFileName(subDir);
                    if (dirName.StartsWith("$") || dirName == "System Volume Information" ||
                        dirName == "Windows" || dirName == "Program Files" ||
                        dirName == "Program Files (x86)")
                        continue;

                    directories.Push(subDir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return files;
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
