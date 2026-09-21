using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A claim about this codebase written in a document is only checkable if the reader can get to the code
/// it describes. That is what a <c>path:line</c> anchor is for.
/// <para>
/// An anchor rots silently. Insert twenty lines above it and it still looks like evidence while pointing
/// at something else entirely - which is worse than no anchor at all, because it reads as though someone
/// verified it. Nothing about editing the code tells you a document somewhere has gone stale.
/// </para>
/// <para>
/// So the anchors are checked here: every one has to name a file that exists and a line that has
/// something on it. This cannot tell you the line still says what the document claims - only a reader
/// can - but it catches the whole class of anchor that points at nothing, at a blank line, or off the
/// end of a file.
/// </para>
/// </summary>
public class DocumentationAnchorTests
{
    /// <summary>Extensions worth anchoring into. A `.md:12` is nearly always prose, not a citation.</summary>
    private static readonly string[] AnchorableExtensions =
        [".cs", ".xaml", ".csproj", ".ps1", ".iss", ".appxmanifest", ".txt", ".json", ".xml", ".manifest"];

    /// <summary>Folders whose documents describe some other codebase, or are not ours to police.</summary>
    private static readonly string[] SkippedFolders = [".git", "obj", "bin", "node_modules", ".vs"];

    /// <summary>`` `path/to/File.cs:42` `` or `` `path/to/File.cs:42-58` ``, in backticks.</summary>
    private static readonly Regex Anchor =
        new(@"`(?<path>[A-Za-z0-9_./\\-]+\.[A-Za-z0-9]+):(?<start>\d+)(?:-(?<end>\d+))?`");

    [Fact]
    public void EveryPathLineAnchorInTheDocumentsPointsAtSomething()
    {
        var files = SourceFileIndex();
        var broken = new List<string>();
        var checkedAnchors = 0;

        foreach (var document in Documents())
        {
            var text = File.ReadAllText(document);

            foreach (Match match in Anchor.Matches(text))
            {
                var cited = match.Groups["path"].Value.Replace('\\', '/');
                if (!AnchorableExtensions.Contains(Path.GetExtension(cited), StringComparer.OrdinalIgnoreCase))
                    continue;

                var start = int.Parse(match.Groups["start"].Value);
                var end = match.Groups["end"].Success ? int.Parse(match.Groups["end"].Value) : start;
                var where = $"{RepoSource.Relative(document)} -> {match.Value}";

                checkedAnchors++;

                var resolved = Resolve(cited, files);
                if (resolved is null)
                {
                    broken.Add($"{where}: no such file");
                    continue;
                }

                var lines = File.ReadAllLines(resolved);
                if (end < start)
                {
                    broken.Add($"{where}: range runs backwards");
                }
                else if (end > lines.Length)
                {
                    broken.Add($"{where}: file has only {lines.Length} lines");
                }
                else if (Enumerable.Range(start, end - start + 1).All(n => lines[n - 1].Trim().Length == 0))
                {
                    broken.Add($"{where}: that line is blank");
                }
            }
        }

        checkedAnchors.Should().BeGreaterThan(20,
            "this test is worthless if it cannot find the anchors it is meant to judge");
        broken.Should().BeEmpty(
            "an anchor that points at nothing reads as evidence and is not; see the list for which document");
    }

    /// <summary>Every document in the repository that might cite code.</summary>
    private static IEnumerable<string> Documents() =>
        Directory.EnumerateFiles(RepoSource.Root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsSkipped(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool IsSkipped(string path)
    {
        var relative = Path.GetRelativePath(RepoSource.Root, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A repository-relative path is used as given. A bare file name is accepted only when exactly one
    /// file in the repository carries it - an ambiguous name is not an anchor, it is a guess.
    /// </summary>
    private static string? Resolve(string cited, ILookup<string, string> files)
    {
        var direct = Path.Combine(RepoSource.Root, cited.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
            return direct;

        if (cited.Contains('/'))
            return null;

        var candidates = files[cited].ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>File name to full path, for every source file a document might cite by bare name.</summary>
    private static ILookup<string, string> SourceFileIndex() =>
        Directory.EnumerateFiles(RepoSource.Root, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsSkipped(path))
            .Where(path => AnchorableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            // GetFileName is declared nullable for nullable input; these all come from an enumeration
            // of real files, so every one has a name.
            .ToLookup(path => Path.GetFileName(path)!, path => path, StringComparer.OrdinalIgnoreCase);
}
