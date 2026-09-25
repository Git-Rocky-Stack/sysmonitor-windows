using System.Globalization;
using System.Text;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The documents this project hands to a reader are plain ASCII.
/// <para>
/// Not a style preference. These files are read in places that do not all agree on encoding: a console
/// window running <c>type README.md</c> under the default OEM code page, a text editor that guesses
/// Windows-1252, a diff in a terminal, a pull request on a phone. A U+2014 em dash read as anything but
/// UTF-8 becomes <c>â€"</c>; a U+00B0 degree sign becomes <c>Â°</c>, so "45°C" reads "45Â°C"; and the
/// box-drawing characters in the README's project tree collapse into noise that no longer lines up as a
/// tree. The em dash and the hyphen carry the same meaning to the reader and only one of them survives
/// the trip.
/// </para>
/// <para>
/// The files below were converted once. Without this test the next edit reintroduces a smart quote from
/// somebody's word processor and nobody notices until a reader reports mojibake, so the rule is checked
/// here instead of remembered. Contributor-facing notes (HANDOFF.md, CLAUDE.md, the signing and styling
/// notes) are deliberately not covered: they are read in an editor by people working on this repository,
/// not shipped to users.
/// </para>
/// <para>
/// If a document genuinely needs a character outside ASCII, the fix is to add it to this test with the
/// reason - not to delete the assertion.
/// </para>
/// </summary>
public class PublishedDocumentEncodingTests
{
    /// <summary>Documents outside docs/ that a user reads: on GitHub, in the download, or beside the app.</summary>
    private static readonly string[] PublishedFiles =
    [
        "README.md",
        "FEATURES_AND_USER_GUIDE.md",
        "CHANGELOG.md",
        "THIRD-PARTY-NOTICES.md",
        "PRIVACY_POLICY.md",
        "LICENSE",
        "README_INSTALLER.txt",
        "installer/README_BEFORE.txt",
        "installer/README_AFTER.txt",
        "installer/INSTALLER_README.txt",
    ];

    [Fact]
    public void EveryPublishedDocumentIsPlainAscii()
    {
        var documents = PublishedDocuments();

        // The list is written out by hand, so a renamed file would otherwise silently stop being checked.
        foreach (var relative in PublishedFiles)
            File.Exists(Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{relative} is listed here as a published document but is not in the repository");

        documents.Should().HaveCountGreaterThan(PublishedFiles.Length,
            "the docs/ site is part of what readers get, and this test is worthless if it cannot see it");

        var offences = documents.SelectMany(NonAsciiIn).ToList();

        offences.Should().BeEmpty(
            "a character outside ASCII reads as mojibake wherever the file is opened as anything but UTF-8");
    }

    [Fact]
    public void NoPublishedDocumentStartsWithAByteOrderMark()
    {
        // A UTF-8 BOM is three bytes of ASCII-invisible punctuation at the top of the file. Markdown
        // renderers cope; `findstr`, a shell heredoc and a naive parser read it as part of the first
        // heading. The files here are all ASCII, so the BOM has nothing to declare either.
        var withBom = new List<string>();

        foreach (var path in PublishedDocuments())
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

    // ---------------------------------------------------------------- helpers

    /// <summary>The hand-listed files, plus every markdown source and text file the docs site publishes.</summary>
    private static IReadOnlyList<string> PublishedDocuments()
    {
        var paths = PublishedFiles
            .Select(relative => Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .ToList();

        var docs = Path.Combine(RepoSource.Root, "docs");
        paths.AddRange(Directory.EnumerateFiles(docs, "*.md", SearchOption.TopDirectoryOnly));
        paths.AddRange(Directory.EnumerateFiles(docs, "*.txt", SearchOption.TopDirectoryOnly));

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
