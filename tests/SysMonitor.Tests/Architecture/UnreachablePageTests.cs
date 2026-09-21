using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A page reaches the user in exactly one way: something navigates to it. Navigation in this app always
/// spells the destination <c>typeof(SomePage)</c> — MainWindow's page map turns a navigation tag into a
/// page type, and the one page opened directly says <c>frame.Navigate(typeof(PdfEditorPage))</c>. A page
/// that no line outside its own file names that way is a screen which compiles, ships inside the
/// installer, and can never be opened.
/// <para>
/// PlaceholderPage was exactly this. It rendered "COMING SOON" for a feature under development; every
/// navigation tag in MainWindow.xaml had since grown a real page underneath it, and the placeholder was
/// left behind — carried in every build, reachable from nothing, and still telling anyone reading the
/// source that some feature was unfinished.
/// </para>
/// <para>
/// Registration in the DI container is deliberately NOT accepted as reachability. Adding a page to the
/// service collection makes it constructible, not reachable; PlaceholderPage would have been just as
/// invisible to the user with an <c>AddTransient</c> line next to it.
/// </para>
/// </summary>
public class UnreachablePageTests
{
    private const string ViewFolder = "src/SysMonitor.App/Views";
    private static readonly string[] CodeFolders = ["src/SysMonitor.App", "src/SysMonitor.Core"];

    /// <summary>`sealed partial class FooPage : Page`, the declaration of a navigable screen.</summary>
    private static readonly Regex PageDeclaration = new(
        @"\bclass\s+(?<name>\w+)\s*:\s*Page\b", RegexOptions.Compiled);

    [Fact]
    public void EveryPageIsNavigatedToFromSomewhere()
    {
        // Comments are stripped first, for the same reason OrphanHandlerTests strips them: a page named in
        // the prose of a doc comment reads, to a plain text search, exactly like a navigation to it.
        var sourcesByFile = CodeFolders
            .SelectMany(RepoSource.FilesUnder)
            .ToDictionary(file => file, file => WithoutComments(File.ReadAllText(file)));

        var unreachable = new List<string>();
        var checkedPages = 0;

        foreach (var file in RepoSource.FilesUnder(ViewFolder)
                     .Where(path => path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var declaration = PageDeclaration.Match(sourcesByFile[file]);
            if (!declaration.Success)
                continue; // A Window or a UserControl living in Views/ — not a navigation destination.

            var name = declaration.Groups["name"].Value;
            checkedPages++;

            // Every other file in the app, so a page naming itself never counts as reaching itself.
            var navigatedTo = sourcesByFile
                .Where(entry => !string.Equals(entry.Key, file, StringComparison.OrdinalIgnoreCase))
                .Any(entry => Regex.IsMatch(entry.Value, $@"typeof\(\s*{Regex.Escape(name)}\s*\)"));

            if (!navigatedTo)
                unreachable.Add($"{RepoSource.Relative(file)}  {name} is never the target of typeof(...) navigation");
        }

        checkedPages.Should().BeGreaterThan(30,
            "this test is worthless if it cannot find the pages it is meant to judge");
        unreachable.Should().BeEmpty(
            "a page nothing navigates to is a screen the user can never open, and it ships in every build");
    }

    /// <summary>The source with <c>//</c> and <c>/* */</c> comments removed, so a name in prose is not a navigation.</summary>
    private static string WithoutComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"//[^\r\n]*", " ");
    }
}
