namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The source files of this repository, for the few rules that live in the shape of the code rather than in
/// its behaviour. Finding the root from the test binary means these keep working wherever the build puts it.
/// </summary>
internal static class RepoSource
{
    /// <summary>The folder holding SysMonitor.sln.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Every .cs file under a folder given relative to the root, excluding build output.</summary>
    public static IReadOnlyList<string> FilesUnder(string relativeFolder) => FilesUnder(relativeFolder, "*.cs");

    /// <summary>Every file matching <paramref name="pattern"/> under a folder given relative to the root, excluding build output.</summary>
    public static IReadOnlyList<string> FilesUnder(string relativeFolder, string pattern)
    {
        var folder = Path.Combine(Root, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"{folder} does not exist. The repository layout has changed.");

        return Directory.GetFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A path as it reads in a report: relative to the repository root, with forward slashes.</summary>
    public static string Relative(string path) =>
        Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SysMonitor.sln")))
                return folder.FullName;
        }

        throw new DirectoryNotFoundException(
            $"No SysMonitor.sln above {AppContext.BaseDirectory}; these tests read the repository's own source.");
    }
}
