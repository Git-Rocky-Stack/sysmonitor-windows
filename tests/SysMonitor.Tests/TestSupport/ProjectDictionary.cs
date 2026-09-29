using System.Xml.Linq;

namespace SysMonitor.Tests.TestSupport;

/// <summary>Opens a merged dictionary the way the app finds it, for the rules that follow merges.</summary>
internal static class ProjectDictionary
{
    private const string AppFolder = "src/SysMonitor.App";
    private const string AppScheme = "ms-appx:///";

    /// <summary>A merged dictionary's Source: <c>ms-appx:///</c> from the project, anything else from the file naming it.</summary>
    public static (string Path, XDocument Document)? Open(string declaringFile, string source)
    {
        var path = source.StartsWith(AppScheme, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(RepoSource.Root, AppFolder, source[AppScheme.Length..])
            : Path.Combine(Path.GetDirectoryName(declaringFile)!, source);

        path = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? (path, XDocument.Load(path)) : null;
    }
}
