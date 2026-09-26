using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>What became of a file handed to the Recycle Bin.</summary>
public enum RecycleOutcome
{
    /// <summary>In the Recycle Bin, and found there afterwards, so it can be restored.</summary>
    Recycled,

    /// <summary>
    /// Left where it is, untouched. Windows could not have recycled it and would have deleted it permanently
    /// instead, or it is a link or a placeholder rather than an ordinary file.
    /// </summary>
    Refused,

    /// <summary>Left where it is: Windows would not move it, or it could not be read.</summary>
    Failed,

    /// <summary>Not there any more, so nothing was done.</summary>
    Missing,

    /// <summary>Gone from where it was, and not in the Recycle Bin either. Treat it as deleted permanently.</summary>
    NotInRecycleBin,
}

/// <summary>
/// What became of one file, with the reason in words that complete "1 file ..." and "3 files ..." alike, so a
/// page can say it.
/// </summary>
/// <param name="Outcome">Where the file is now.</param>
/// <param name="Reason">Why, as a phrase: "on a network drive", "that Windows would not move".</param>
/// <param name="Error">What Windows said, when it refused to move the file; for the log, not the screen.</param>
public readonly record struct RecycleResult(RecycleOutcome Outcome, string Reason, Exception? Error = null)
{
    public bool IsRecycled => Outcome == RecycleOutcome.Recycled;

    internal static RecycleResult Recycled { get; } = new(RecycleOutcome.Recycled, "moved to the Recycle Bin");
}

/// <summary>One file handed to the Recycle Bin, how much it held, and what became of it.</summary>
public readonly record struct RecycledFile(string Path, long Bytes, RecycleResult Result);

/// <summary>
/// Hands files to the Recycle Bin - and only to the Recycle Bin.
/// <para>
/// The shell call underneath asks Windows to recycle a file without confirmation. When Windows cannot recycle
/// it - on a network or removable drive, on a drive whose Recycle Bin is turned off, or when the file is larger
/// than the Recycle Bin is set to hold - that call deletes it permanently, without a word, and it used to be
/// reported as moved to the Recycle Bin. Large Files lists files of 100 MB and more, so the last case was not
/// rare.
/// </para>
/// <para>
/// Now each of those cases is recognised first and the file is left where it is, with the reason. Whatever
/// is handed over is looked for in the Recycle Bin afterwards, so "moved to the Recycle Bin" is only ever said
/// of a file that is there. A case nobody foresaw is reported as gone but not in the Recycle Bin, never as
/// recycled.
/// </para>
/// </summary>
public sealed class RecycleBin
{
    /// <summary>Longer paths than this are not ones the Recycle Bin can promise to hold.</summary>
    private const int MaxPath = 260;

    private readonly IRecycleBinHost _host;

    /// <summary>The Recycle Bin of the machine this runs on.</summary>
    public static RecycleBin Windows { get; } = new(new WindowsRecycleBinHost());

    internal RecycleBin(IRecycleBinHost host) => _host = host;

    /// <summary>
    /// Moves <paramref name="path"/> to the Recycle Bin when Windows can put it there, and leaves it where it is
    /// when Windows cannot. Never follows a link, and never deletes anything outright.
    /// </summary>
    public RecycleResult Send(string path)
    {
        string fullPath;
        FileInfo file;
        try
        {
            fullPath = Path.GetFullPath(path);
            file = new FileInfo(fullPath);
            if (!file.Exists)
                return new(RecycleOutcome.Missing, "no longer there");

            // A link is a way to somewhere else: removing it removes someone's shortcut, not a copy of anything.
            // A placeholder stands for a file OneDrive or the like keeps elsewhere, and recycling it here is not
            // something this can promise.
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return file.LinkTarget is not null
                    ? new(RecycleOutcome.Refused, "linking to another file")
                    : new(RecycleOutcome.Refused, "handled by OneDrive or another storage feature");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException)
        {
            return new(RecycleOutcome.Failed, "that could not be read", ex);
        }

        var refusal = WhyWindowsWouldNotRecycle(fullPath, file.Length, out var root);
        if (refusal is not null)
            return new(RecycleOutcome.Refused, refusal);

        var handedOver = DateTime.UtcNow;
        try
        {
            _host.MoveToRecycleBin(fullPath);
        }
        catch (Exception ex)
        {
            // In use, not allowed, or cancelled at the error dialog Windows showed: the file stayed put.
            return new(RecycleOutcome.Failed, "that Windows would not move", ex);
        }

        if (File.Exists(fullPath))
            return new(RecycleOutcome.Failed, "that Windows would not move");

        return _host.IsInRecycleBin(root, fullPath, handedOver)
            ? RecycleResult.Recycled
            : new(RecycleOutcome.NotInRecycleBin, "that Windows removed without putting in the Recycle Bin");
    }

    /// <summary>
    /// Why Windows would delete this file permanently rather than recycle it, or null when it would recycle it.
    /// Every phrase completes "1 file ..." and "3 files ..." alike.
    /// </summary>
    /// <param name="root">The drive's root when the answer is null, for looking in its Recycle Bin afterwards.</param>
    internal string? WhyWindowsWouldNotRecycle(string fullPath, long size, out string root)
    {
        root = "";
        var drive = _host.DriveOf(fullPath);

        if (drive.Type == DriveType.Network)
            return "on a network drive";

        if (drive.Type == DriveType.Removable)
            return "on a removable drive";

        if (drive.Type != DriveType.Fixed || drive.Root is not { } fixedRoot ||
            _host.LimitsOf(fixedRoot) is not { } limits)
        {
            return "on a drive with no Recycle Bin";
        }

        if (limits.TurnedOff)
            return "on a drive whose Recycle Bin is turned off";

        if (size > limits.CapacityBytes)
            return $"larger than the Recycle Bin on {fixedRoot.TrimEnd('\\')} is set to hold";

        if (fullPath.Length >= MaxPath)
            return "with a path too long for the Recycle Bin";

        root = fixedRoot;
        return null;
    }

    /// <summary>
    /// The drive letter root a path is on, such as <c>C:\</c>, or null for a network path. Long-path and device
    /// prefixes are looked through, so <c>\\?\C:\file</c> is on C: and <c>\\?\UNC\server\share</c> is not.
    /// </summary>
    internal static string? DriveRootOf(string fullPath)
    {
        var path = fullPath;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return null;

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            path = path[4..];

        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
            ? $"{char.ToUpperInvariant(path[0])}:\\"
            : null;
    }
}

/// <summary>What a drive's Recycle Bin will take.</summary>
/// <param name="TurnedOff">Files deleted there are removed at once, by a setting or a policy.</param>
/// <param name="CapacityBytes">The most it holds; a file larger than this is deleted, not recycled.</param>
internal readonly record struct RecycleBinLimits(bool TurnedOff, long CapacityBytes);

/// <summary>What <see cref="RecycleBin"/> needs to know about the machine, so a test can be a different one.</summary>
internal interface IRecycleBinHost
{
    /// <summary>The drive a file is on: its root (<c>C:\</c>, or null for a network path) and its type.</summary>
    (string? Root, DriveType Type) DriveOf(string fullPath);

    /// <summary>What the Recycle Bin on the drive at <paramref name="root"/> takes, or null when it has none.</summary>
    RecycleBinLimits? LimitsOf(string root);

    /// <summary>Asks Windows to move the file to the Recycle Bin. Throws when Windows will not move it.</summary>
    void MoveToRecycleBin(string fullPath);

    /// <summary>Whether the Recycle Bin on <paramref name="root"/> holds this file, put there since the given time.</summary>
    bool IsInRecycleBin(string root, string fullPath, DateTime sinceUtc);
}

/// <summary>The Recycle Bin as Windows keeps it: settings in the registry, contents in <c>$Recycle.Bin</c>.</summary>
internal sealed class WindowsRecycleBinHost : IRecycleBinHost
{
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string VolumesKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume";

    /// <summary>
    /// A drive whose Recycle Bin was never configured gets a size Windows works out from the drive, and that
    /// has never been below 5% of it. Assuming 5% can leave alone a file that would have fitted; it cannot let
    /// through one that would not.
    /// </summary>
    private const int SmallestDefaultPercent = 5;

    public (string? Root, DriveType Type) DriveOf(string fullPath)
    {
        var root = RecycleBin.DriveRootOf(fullPath);
        if (root is null)
            return (null, DriveType.Network);

        try
        {
            return (root, new DriveInfo(root).DriveType);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (root, DriveType.Unknown);
        }
    }

    public RecycleBinLimits? LimitsOf(string root)
    {
        // A substituted drive letter or anything else that is not a volume of its own has no Recycle Bin.
        if (VolumeIdOf(root) is not { } volume)
            return null;

        if (PolicyValue("NoRecycleFiles") == 1)
            return new RecycleBinLimits(TurnedOff: true, CapacityBytes: 0);

        using var settings = Registry.CurrentUser.OpenSubKey($@"{VolumesKey}\{volume}");
        if (settings?.GetValue("NukeOnDelete") is int nuke && nuke == 1)
            return new RecycleBinLimits(TurnedOff: true, CapacityBytes: 0);

        long totalBytes;
        try
        {
            totalBytes = new DriveInfo(root).TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // A policy percentage overrides whatever size was set for the drive.
        if (PolicyValue("RecycleBinSize") is { } percent and >= 0)
            return new RecycleBinLimits(TurnedOff: false, CapacityBytes: totalBytes / 100 * percent);

        if (settings?.GetValue("MaxCapacity") is int megabytes and >= 0)
            return new RecycleBinLimits(TurnedOff: false, CapacityBytes: megabytes * 1024L * 1024L);

        return new RecycleBinLimits(TurnedOff: false, CapacityBytes: totalBytes / 100 * SmallestDefaultPercent);
    }

    public void MoveToRecycleBin(string fullPath) =>
        FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public bool IsInRecycleBin(string root, string fullPath, DateTime sinceUtc)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User?.Value;
        return user is not null &&
               RecycleBinRecords.Contains(Path.Combine(root, "$Recycle.Bin", user), fullPath, sinceUtc);
    }

    /// <summary>A machine policy wins over a user one, the way Group Policy applies them.</summary>
    private static int? PolicyValue(string name)
    {
        using var machine = Registry.LocalMachine.OpenSubKey(PolicyKey);
        if (machine?.GetValue(name) is int machineValue)
            return machineValue;

        using var user = Registry.CurrentUser.OpenSubKey(PolicyKey);
        return user?.GetValue(name) is int userValue ? userValue : null;
    }

    /// <summary>The volume's GUID, <c>{...}</c>, as its Recycle Bin settings are filed under.</summary>
    private static string? VolumeIdOf(string root)
    {
        var name = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(root, name, name.Capacity))
            return null;

        // \\?\Volume{GUID}\
        var text = name.ToString();
        var open = text.IndexOf('{');
        var close = text.IndexOf('}');
        return open >= 0 && close > open ? text[open..(close + 1)] : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, int length);
}

/// <summary>
/// Reads what the Recycle Bin records about each file it holds: a <c>$I</c> file beside the <c>$R</c> file that
/// holds the contents, naming the path the file was deleted from and when.
/// </summary>
internal static class RecycleBinRecords
{
    /// <summary>Room for the file system's coarse timestamps and a clock that moved.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    private static readonly long LatestFileTime = DateTime.MaxValue.ToFileTimeUtc();

    /// <summary>Whether <paramref name="folder"/> records <paramref name="fullPath"/> as deleted since the given time.</summary>
    public static bool Contains(string folder, string fullPath, DateTime sinceUtc)
    {
        if (!Directory.Exists(folder))
            return false;

        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        foreach (var record in new DirectoryInfo(folder).EnumerateFiles("$I*", options))
        {
            // Everything deleted before this one: the record is written when the file goes in.
            if (record.LastWriteTimeUtc < sinceUtc - Slack)
                continue;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(record.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a record that cannot be read cannot vouch for the file, so the search goes on.
                continue;
            }

            if (TryRead(bytes, out var originalPath, out var deletedUtc) &&
                deletedUtc >= sinceUtc - Slack &&
                string.Equals(WithoutPrefix(originalPath), WithoutPrefix(fullPath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The same path with any <c>\\?\</c> long-path prefix taken off, as the Recycle Bin records it.</summary>
    private static string WithoutPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? path[4..]
            : path;

    /// <summary>
    /// Reads a <c>$I</c> record. Two layouts exist. Both open with a version (8 bytes), the file's size (8) and
    /// when it was deleted (8, a FILETIME). Version 1, before Windows 10, then holds the path in 520 bytes of
    /// UTF-16 padded with zeros; version 2 holds its length in characters (4 bytes) and then the path.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out string originalPath, out DateTime deletedUtc)
    {
        originalPath = "";
        deletedUtc = default;
        if (bytes.Length < 24)
            return false;

        var version = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var fileTime = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        if (fileTime < 0 || fileTime > LatestFileTime)
            return false;

        ReadOnlySpan<byte> path;
        switch (version)
        {
            case 1 when bytes.Length >= 24 + 520:
                path = bytes.Slice(24, 520);
                break;
            case 2 when bytes.Length >= 28:
                var characters = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]);
                if (characters <= 0 || bytes.Length < 28 + characters * 2L)
                    return false;
                path = bytes.Slice(28, characters * 2);
                break;
            default:
                return false;
        }

        // The path ends at its terminating zero; anything after it is padding.
        var text = Encoding.Unicode.GetString(path);
        var end = text.IndexOf('\0');
        originalPath = end >= 0 ? text[..end] : text;
        deletedUtc = DateTime.FromFileTimeUtc(fileTime);
        return originalPath.Length > 0;
    }
}
