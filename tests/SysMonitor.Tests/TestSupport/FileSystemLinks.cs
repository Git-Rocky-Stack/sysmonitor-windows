using System.Diagnostics;

namespace SysMonitor.Tests.TestSupport;

/// <summary>Creates real NTFS junctions and symbolic links for tests.</summary>
internal static class FileSystemLinks
{
    /// <summary>Creates a directory junction (no special privileges required).</summary>
    public static void CreateJunction(string linkPath, string targetDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetDirectory}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !new DirectoryInfo(linkPath).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"Could not create junction {linkPath} -> {targetDirectory}: {output}");
    }

    /// <summary>Creates a hard link: a second name for the same file (no special privileges required).</summary>
    public static void CreateHardLink(string linkPath, string targetFile)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{linkPath}\" \"{targetFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !File.Exists(linkPath))
            throw new InvalidOperationException($"Could not create hard link {linkPath} -> {targetFile}: {output}");
    }

    /// <summary>
    /// Creates a file symbolic link. Windows requires Developer Mode or administrator rights for this;
    /// the test fails loudly (rather than passing vacuously) when the environment cannot create one.
    /// </summary>
    public static void CreateFileSymlink(string linkPath, string targetFile)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetFile);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Creating a file symbolic link failed. Enable Windows Developer Mode or run the tests elevated.", ex);
        }
    }
}
