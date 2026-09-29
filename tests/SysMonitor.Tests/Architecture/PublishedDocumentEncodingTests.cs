using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Every document this project hands to a reader is plain ASCII, and so is every word of the User's Guide the
/// app shows.
/// <para>
/// Not a style preference. These files are read in places that do not all agree on encoding: a console
/// window running <c>type README.md</c> under the default OEM code page, a text editor that guesses
/// Windows-1252, a diff in a terminal, a pull request on a phone. A U+2014 em dash read as anything but
/// UTF-8 becomes <c>â€"</c>; a U+00B0 degree sign becomes <c>Â°</c>, so "45°C" reads "45Â°C"; and the
/// box-drawing characters in a project tree collapse into noise that no longer lines up as a tree. The em
/// dash and the hyphen carry the same meaning to the reader and only one of them survives the trip. Em
/// dashes, emoji and other decoration have no place in the project's documentation at all.
/// </para>
/// <para>
/// The repository is public, so every document in it is published: the README is the project's front page,
/// and GitHub shows the contributor notes (HANDOFF.md, CLAUDE.md, the signing and styling notes) to anyone
/// who opens them. All of them are covered, found by walking the repository rather than listed, so a new or
/// renamed document is checked from the day it appears. The User's Guide page is the documentation most
/// users actually read; its words are checked as the app shows them, character references resolved, while
/// its icons are left alone: a symbol font keeps its glyphs in Unicode's Private Use Area, which no reader
/// ever sees as text.
/// </para>
/// <para>
/// If a document genuinely needs a character outside ASCII, the fix is to add it to this test with the
/// reason - not to delete the assertion.
/// </para>
/// </summary>
public class PublishedDocumentEncodingTests
{
    private static readonly string[] DocumentExtensions = [".md", ".txt", ".html"];

    /// <summary>Build output and tooling, which hold copies and caches rather than documents of ours.</summary>
    private static readonly string[] SkippedFolders = [".git", ".vs", "bin", "obj", "node_modules", "publish", "TestResults", "AppPackages"];

    private const string InAppGuide = "src/SysMonitor.App/Views/UserGuidePage.xaml";

    [Fact]
    public void EveryDocumentIsPlainAscii()
    {
        var documents = Documents();

        documents.Select(RepoSource.Relative).Should().Contain(
            ["README.md", "CHANGELOG.md", "LICENSE", "HANDOFF.md", "docs/index.html", "docs/tutorial-getting-started.md"],
            "this test is worthless if it cannot see the documents it is meant to judge");

        var offences = documents.SelectMany(NonAsciiIn).ToList();

        offences.Should().BeEmpty(
            "a character outside ASCII reads as mojibake wherever the file is opened as anything but UTF-8");
    }

    [Fact]
    public void NoDocumentStartsWithAByteOrderMark()
    {
        // A UTF-8 BOM is three bytes of ASCII-invisible punctuation at the top of the file. Markdown
        // renderers cope; `findstr`, a shell heredoc and a naive parser read it as part of the first
        // heading. The files here are all ASCII, so the BOM has nothing to declare either.
        var withBom = new List<string>();

        foreach (var path in Documents())
        {
            var head = new byte[3];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                    withBom.Add(RepoSource.Relative(path));
            }
        }

        withBom.Should().BeEmpty("a byte order mark is invisible in an editor and not in a parser");
    }

    [Fact]
    public void TheInAppGuideShowsOnlyPlainAscii()
    {
        var path = Path.Combine(RepoSource.Root, InAppGuide.Replace('/', Path.DirectorySeparatorChar));
        var guide = XDocument.Load(path, LoadOptions.SetLineInfo);
        var offences = new List<string>();
        var words = 0;

        foreach (var element in guide.Descendants())
        {
            var shown = element.Nodes().OfType<XText>().Select(text => text.Value)
                .Concat(element.Attributes().Select(attribute => attribute.Value));

            foreach (var text in shown)
            {
                words += text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                foreach (var character in text.Where(c => c > '~' && !IsIcon(c)).Distinct())
                {
                    offences.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1} U+{2:X4} {3}",
                        InAppGuide, ((IXmlLineInfo)element).LineNumber, (int)character, CharUnicodeInfo.GetUnicodeCategory(character)));
                }
            }
        }

        words.Should().BeGreaterThan(1000, "this test is worthless if it cannot read the guide it is meant to judge");
        offences.Should().BeEmpty("the guide is documentation, held to the same plain typography as the documents");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A Private Use Area code point: an icon in a symbol font such as Segoe MDL2 Assets.</summary>
    private static bool IsIcon(char character) => character is >= '\uE000' and <= '\uF8FF';

    /// <summary>Every markdown, text and HTML document in the repository, and the licence.</summary>
    private static IReadOnlyList<string> Documents() =>
        Directory.EnumerateFiles(RepoSource.Root, "*", SearchOption.AllDirectories)
            .Where(path => DocumentExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                        || Path.GetFileName(path) == "LICENSE")
            .Where(path => !IsSkipped(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsSkipped(string path) =>
        Path.GetRelativePath(RepoSource.Root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase));

    /// <summary>Every character above U+007E, reported with the file, line and what it is.</summary>
    private static IEnumerable<string> NonAsciiIn(string path)
    {
        var relative = RepoSource.Relative(path);
        var lines = File.ReadAllLines(path, Encoding.UTF8);

        for (var number = 1; number <= lines.Length; number++)
        {
            foreach (var character in lines[number - 1].Where(c => c > '~').Distinct())
            {
                yield return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1} U+{2:X4} {3}",
                    relative, number, (int)character, CharUnicodeInfo.GetUnicodeCategory(character));
            }
        }
    }
}
