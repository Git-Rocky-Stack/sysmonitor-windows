using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SysMonitor.Core.Services.Backup;

/// <summary>
/// What a system image would be, worked out before Windows Backup is started: where it goes, how much it may
/// need, and - when it cannot be made there - why.
/// </summary>
public record SystemImagePlan
{
    /// <summary>Why no image can be made at this destination; null when one can.</summary>
    public string? Problem { get; init; }

    /// <summary>What Windows Backup is given as its target: a drive (<c>E:</c>) or a network folder.</summary>
    public string Target { get; init; } = "";

    /// <summary>The folder Windows Backup writes, <c>E:\WindowsImageBackup</c>; it holds every version it keeps.</summary>
    public string ImageFolder { get; init; } = "";

    /// <summary>The drive Windows runs from, <c>C:</c>. The image also takes the partitions Windows starts from.</summary>
    public string SystemDrive { get; init; } = "";

    /// <summary>Space in use on the system drive: about the most the first image needs.</summary>
    public long SystemUsedBytes { get; init; }

    /// <summary>Free space at the target; 0 for a network folder, whose space cannot be read here.</summary>
    public long TargetFreeBytes { get; init; }

    public bool IsNetworkTarget { get; init; }

    /// <summary>The target has less free space than the system drive uses, so the image may not fit.</summary>
    public bool MayRunOutOfSpace => !IsNetworkTarget && TargetFreeBytes < SystemUsedBytes;
}

/// <summary>What the app needs to know about a drive to put a system image on it.</summary>
internal sealed record DriveFacts(bool IsReady, string Format, long FreeBytes, long TotalBytes, DriveType Type);

/// <summary>
/// A system image is Windows Backup's own (<c>wbadmin start backup -allCritical</c>): the system drive and every
/// partition Windows needs to start, as virtual disks that Windows Recovery's System Image Recovery puts back.
/// This works out the command and reads its report; <see cref="BackupService"/> runs it.
/// </summary>
internal static class SystemImage
{
    /// <summary>Where Windows Backup's report of a run is kept, beside the app's own logs.</summary>
    internal const string LogPrefix = "wbadmin-";

    internal static SystemImagePlan Plan(string destinationPath, string systemDirectory, Func<string, DriveFacts?> inspect)
    {
        static SystemImagePlan Refuse(string problem) => new() { Problem = problem };

        var destination = destinationPath.Trim();
        if (destination.Length == 0)
            return Refuse("Choose the drive to save the system image on.");

        // The target goes to wbadmin through cmd.exe; % would expand there and a quote would end the argument.
        if (destination.IndexOfAny(['%', '"', '\r', '\n']) >= 0)
            return Refuse($"{destination} cannot hold a system image: its name has a % or a quote in it. Choose another folder.");

        if (systemDirectory.Length < 2 || systemDirectory[1] != ':')
            return Refuse("Windows' own drive could not be found, so no system image can be made.");
        var systemDrive = systemDirectory[..2].ToUpperInvariant();
        var system = inspect(systemDrive + "\\");
        var systemUsed = system is { IsReady: true } ? system.TotalBytes - system.FreeBytes : 0;

        if (destination.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var share = destination.TrimEnd('\\');
            if (share[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                return Refuse($"{destination} is not a network folder. Choose one such as \\\\server\\backups.");

            return new SystemImagePlan
            {
                Target = share,
                ImageFolder = share + @"\WindowsImageBackup",
                SystemDrive = systemDrive,
                SystemUsedBytes = systemUsed,
                IsNetworkTarget = true,
            };
        }

        if (destination.Length < 2 || !char.IsAsciiLetter(destination[0]) || destination[1] != ':')
            return Refuse($"{destination} is not a drive or a network folder.");

        var target = destination[..2].ToUpperInvariant();
        if (target == systemDrive)
            return Refuse($"A system image cannot be saved on {systemDrive}, the drive it is an image of. " +
                          "Choose another drive, such as a USB drive, or a network folder.");

        var drive = inspect(target + "\\");
        if (drive is not { IsReady: true })
            return Refuse($"{target} is not ready. Connect the drive, or choose another one.");

        switch (drive.Type)
        {
            case DriveType.CDRom:
                return Refuse($"{target} is a disc drive. Choose a hard disk, a USB drive or a network folder.");
            case DriveType.Network:
                return Refuse($"{target} is a mapped network drive. Choose its network folder (\\\\server\\share) instead.");
        }

        if (!string.Equals(drive.Format, "NTFS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(drive.Format, "ReFS", StringComparison.OrdinalIgnoreCase))
            return Refuse($"{target} is formatted {drive.Format}, and Windows Backup saves system images only on NTFS. " +
                          "Reformat it as NTFS (which erases it), or choose another drive.");

        return new SystemImagePlan
        {
            Target = target,
            ImageFolder = target + @"\WindowsImageBackup",
            SystemDrive = systemDrive,
            SystemUsedBytes = systemUsed,
            TargetFreeBytes = drive.FreeBytes,
        };
    }

    /// <summary>
    /// <c>-allCritical</c> takes every partition Windows needs to start - the EFI and recovery partitions as well
    /// as the system drive - which is what makes the image restorable onto a new disk.
    /// </summary>
    internal static string Arguments(SystemImagePlan plan)
    {
        var target = plan.Target.Contains(' ') ? $"\"{plan.Target}\"" : plan.Target;
        return $"start backup -backupTarget:{target} -include:{plan.SystemDrive} -allCritical -quiet";
    }

    /// <summary>
    /// wbadmin.exe is 64-bit only; a 32-bit app asking for System32 is sent to SysWOW64, which has none, so it
    /// asks for Sysnative, the name that reaches the real System32.
    /// </summary>
    internal static string WbadminPath()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32";
        return Path.Combine(windows, system, "wbadmin.exe");
    }

    private static readonly Regex Percent = new(@"\((\d{1,3})%\)", RegexOptions.Compiled);

    /// <summary>How far Windows Backup says it is: the last "copied (NN%)" in its report.</summary>
    internal static int? LastPercent(string log)
    {
        var matches = Percent.Matches(log);
        return matches.Count == 0 ? null : Math.Clamp(int.Parse(matches[^1].Groups[1].Value), 0, 100);
    }

    /// <summary>The last thing Windows Backup said.</summary>
    internal static string? LastLine(string log) =>
        Lines(log).LastOrDefault();

    /// <summary>What went wrong, in Windows Backup's words: its last line naming an error, else its last line.</summary>
    internal static string FailureLine(string log, int exitCode) =>
        Lines(log).LastOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                                         || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
        ?? LastLine(log)
        ?? $"Windows Backup stopped with code {exitCode}";

    private static IEnumerable<string> Lines(string log) =>
        log.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Runs a program with administrator rights, its output written to <paramref name="logPath"/>. Windows asks
    /// the person first; a refusal is a <see cref="System.ComponentModel.Win32Exception"/> with code 1223.
    /// The output cannot be read directly: redirection needs UseShellExecute off, and "runas" needs it on, so
    /// cmd.exe writes it to the file, which the caller reads as it grows.
    /// </summary>
    internal static async Task<int> RunElevatedAsync(string program, string arguments, string logPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /c \"\"{program}\" {arguments} > \"{logPath}\" 2>&1\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Windows did not start Windows Backup.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>Reads a report another process is still writing.</summary>
    internal static string ReadLog(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return ""; // not written yet
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    internal static DriveFacts? InspectDrive(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            return drive.IsReady
                ? new DriveFacts(true, drive.DriveFormat, drive.AvailableFreeSpace, drive.TotalSize, drive.DriveType)
                : new DriveFacts(false, "", 0, 0, drive.DriveType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal const string RestoreInstructions =
        "A system image is put back from Windows Recovery, not from inside Windows: open Settings > System > " +
        "Recovery, choose Advanced startup > Restart now, then Troubleshoot > Advanced options > System Image " +
        "Recovery. It replaces everything on the drives in the image.";
}
