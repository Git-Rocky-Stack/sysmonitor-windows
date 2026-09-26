using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Which file a path names. Two paths with the same identity are two names for one file - through a
/// junction, a symbolic link, or a hard link - not two copies of it.
/// </summary>
public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);

/// <summary>Walking and removing files the way a tool that deletes things has to.</summary>
public static class FileScanning
{
    /// <summary>
    /// Every file under a folder, never following a junction, symbolic link or mount point. Following one
    /// shows the same file twice, and a finder that calls the second one a duplicate deletes the only copy.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken = default) =>
        Walk(root, recurse: true, cancellationToken);

    /// <summary>
    /// The files sitting directly in a folder, without descending into it. A caller that walks each
    /// subfolder separately uses this for the folder itself, rather than a second recursive walk that
    /// would see everything below it twice.
    /// </summary>
    public static IEnumerable<string> EnumerateFilesIn(string folder, CancellationToken cancellationToken = default) =>
        Walk(folder, recurse: false, cancellationToken);

    private static IEnumerable<string> Walk(string root, bool recurse, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,          // hidden and system entries still have to be seen to be skipped
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
        };

        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = directories.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (IOException)
                {
                    continue;
                }

                // A link is a way to somewhere else, not a place of its own.
                if (IsLinkToElsewhere(attributes, LinkTargetOf(entry)))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (recurse && !IsSkippedFolder(entry.Name))
                    {
                        directories.Push(entry.FullName);
                    }
                }
                else
                {
                    yield return entry.FullName;
                }
            }
        }
    }

    /// <summary>
    /// Whether an entry is a way to somewhere else rather than a place of its own.
    ///
    /// <para>
    /// The reparse-point attribute alone is not the question. A junction and a symbolic link have it, and
    /// following one shows the same file twice. So do two things that are not links at all: a OneDrive
    /// Files On-Demand placeholder, and a file stored by Data Deduplication. Both are real files, at that
    /// path, with a real size — and skipping every reparse point meant a user with 400 GB in OneDrive got
    /// no large-file results at all, with nothing on screen to say why.
    /// </para>
    /// <para>
    /// <c>LinkTarget</c> is what tells them apart: it is set for a link and null for everything else.
    /// </para>
    /// </summary>
    public static bool IsLinkToElsewhere(FileAttributes attributes, string? linkTarget) =>
        attributes.HasFlag(FileAttributes.ReparsePoint) && linkTarget is not null;

    /// <summary>The path an entry points at, or null when it is not a link or cannot be read.</summary>
    private static string? LinkTargetOf(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget;
        }
        catch (IOException)
        {
            // Best effort: an entry that will not say where it points is treated as a link, which is the
            // answer that keeps a scan from walking into it twice.
            return entry.FullName;
        }
    }

    /// <summary>Folders a file finder has no business walking into.</summary>
    internal static bool IsSkippedFolder(string name) =>
        name.StartsWith('$') ||
        name is "System Volume Information" or "Windows" or "Program Files" or "Program Files (x86)" or ".git";

    /// <summary>
    /// Which file this path names, or null when it cannot be opened. Used to tell two names for one file
    /// apart from two files that happen to match.
    /// </summary>
    public static FileIdentity? TryGetIdentity(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var information))
            {
                return null;
            }

            return new FileIdentity(
                information.VolumeSerialNumber,
                ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves a file to the Recycle Bin, where the person who did not mean it can get it back - and leaves it where
    /// it is when Windows could not put it there, rather than letting Windows delete it outright. Links are never
    /// followed. See <see cref="RecycleBin"/>.
    /// </summary>
    public static RecycleResult SendToRecycleBin(string path) => RecycleBin.Windows.Send(path);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
