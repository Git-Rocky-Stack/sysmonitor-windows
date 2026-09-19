namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// A uniquely named directory under the user's temp folder, deleted on dispose.
/// Deletion never follows symbolic links or junctions.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix = "sysmon-test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>Creates a file (and any missing folders) relative to this directory.</summary>
    public string File(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        System.IO.File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose() => DeleteWithoutFollowingLinks(Path);

    /// <summary>Removes a tree; links inside it are removed as links, their targets untouched.</summary>
    public static void DeleteWithoutFollowingLinks(string root)
    {
        if (!Directory.Exists(root))
            return;

        var options = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false };
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (isDirectory) Directory.Delete(entry.FullName);
                else System.IO.File.Delete(entry.FullName);
            }
            else if (isDirectory)
            {
                DeleteWithoutFollowingLinks(entry.FullName);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                System.IO.File.Delete(entry.FullName);
            }
        }

        Directory.Delete(root);
    }
}
