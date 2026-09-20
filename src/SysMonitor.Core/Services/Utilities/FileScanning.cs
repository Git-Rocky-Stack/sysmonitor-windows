using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
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
    public static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken = default)
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
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!IsSkippedFolder(entry.Name))
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
    /// Removes a file to the Recycle Bin, where the person who did not mean it can get it back. Returns false
    /// when it is gone already, is a link, or will not go.
    /// </summary>
    public static bool SendToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            // Deleting a link would remove someone's shortcut to a file, not a copy of it.
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return true;
        }
        catch
        {
            return false;
        }
    }

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
