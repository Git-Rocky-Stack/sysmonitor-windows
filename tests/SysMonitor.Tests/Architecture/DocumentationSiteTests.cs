using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The site under <c>docs/</c> is generated from the markdown beside it by <c>docs/build-docs.py</c>, and
/// the rules that keep it coherent are checked here rather than by opening it in a browser.
/// <para>
/// The defect this was written for: <c>docs/README.md</c> was the documentation index, but it was not in
/// the generator's page list, so no <c>.html</c> was ever produced for it. The footer link called "All
/// documentation" therefore pointed at
/// <c>github.com/.../blob/main/docs/README.md</c> - it sent a reader off the documentation site, into a
/// source view, to read a page the site was supposed to be serving. Nothing was broken enough to notice:
/// the link worked, and the destination was the right content in the wrong place.
/// </para>
/// <para>
/// So three rules. Every markdown source has a published page. Every published page is in the sitemap.
/// And no page on the site links out to GitHub for a document the site itself publishes.
/// </para>
/// </summary>
public class DocumentationSiteTests
{
    private const string Docs = "docs";
    private const string Site = "https://git-rocky-stack.github.io/sysmonitor-windows/";

    /// <summary>Hand-written, not generated: it has no markdown source and is the sitemap's bare root URL.</summary>
    private const string FrontPage = "index.html";

    private static readonly Regex Href = new(@"href=""(?<target>[^""]+)""", RegexOptions.Compiled);

    /// <summary>An anchor with its text, so a link can be judged by where it claims to go.</summary>
    private static readonly Regex Anchor =
        new(@"<a\b[^>]*href=""(?<target>[^""]+)""[^>]*>(?<text>.*?)</a>",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// The one link to a GitHub source view that is not a defect. It offers to edit the page you are
    /// reading, so the source view is the destination, not a detour on the way to the content.
    /// </summary>
    private const string EditThisPage = "Edit this page on GitHub";

    /// <summary>The generator stamps this into every page it writes, naming the markdown it came from.</summary>
    private static readonly Regex EditLink =
        new(@"/blob/main/docs/(?<source>[A-Za-z0-9._-]+\.md)"">Edit this page on GitHub", RegexOptions.Compiled);

    [Fact]
    public void EveryMarkdownSourceHasAPublishedPage()
    {
        var sources = MarkdownSources();
        sources.Should().NotBeEmpty("this test is worthless if it cannot find the documentation");

        // Each generated page names its own source, so the mapping is read off the output rather than
        // reimplemented here - a page published under a renamed file is still matched to its markdown.
        var published = GeneratedPages()
            .Select(path => EditLink.Match(File.ReadAllText(path)))
            .Where(match => match.Success)
            .Select(match => match.Groups["source"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unpublished = sources.Where(source => !published.Contains(source)).ToList();

        unpublished.Should().BeEmpty(
            "a markdown file under docs/ with no generated page is a document the site cannot serve, so " +
            "every link to it has to leave the site; add it to PAGES in docs/build-docs.py");
    }

    [Fact]
    public void EveryPublishedPageIsInTheSitemap()
    {
        var sitemap = File.ReadAllText(Path.Combine(RepoSource.Root, Docs, "sitemap.xml"));

        sitemap.Should().Contain($"<loc>{Site}</loc>", "the front page is the site root");

        var missing = GeneratedPages()
            .Select(Path.GetFileName)
            .Where(name => !sitemap.Contains($"<loc>{Site}{name}</loc>", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty("a page absent from the sitemap is one search engines are not told about");
    }

    [Fact]
    public void NoPageLinksOutToGitHubForSomethingTheSitePublishes()
    {
        var sources = MarkdownSources();
        var offSite = new List<string>();
        var editLinks = 0;

        foreach (var page in AllPages())
        {
            foreach (Match match in Anchor.Matches(File.ReadAllText(page)))
            {
                var target = match.Groups["target"].Value;

                if (match.Groups["text"].Value.Trim() == EditThisPage)
                {
                    editLinks++;
                    continue;
                }

                // A link into the repository's docs folder: the reader is being sent to a source view of
                // a page this site renders itself.
                var blob = Regex.Match(target, @"github\.com/.+/blob/[^/]+/docs/(?<source>[A-Za-z0-9._-]+\.md)");
                if (blob.Success && sources.Contains(blob.Groups["source"].Value, StringComparer.OrdinalIgnoreCase))
                    offSite.Add($"{RepoSource.Relative(page)} -> {target}");
            }
        }

        // The exemption has to be earning its keep. If the edit links disappear, this test has quietly
        // stopped excluding anything and the rule below is being checked against a different site.
        editLinks.Should().Be(GeneratedPages().Count,
            $"every generated page carries exactly one \"{EditThisPage}\" link, which is the one " +
            "GitHub source link that is not a defect");

        offSite.Should().BeEmpty(
            "these documents are published on the site; linking to the GitHub source view of one sends " +
            "the reader away from the site to read a page it already serves");
    }

    [Fact]
    public void EveryRelativeLinkOnTheSiteResolvesToAFileThatExists()
    {
        var docs = Path.Combine(RepoSource.Root, Docs);
        var broken = new List<string>();
        var checkedLinks = 0;

        foreach (var page in AllPages())
        {
            foreach (Match match in Href.Matches(File.ReadAllText(page)))
            {
                var target = match.Groups["target"].Value;

                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith('#')
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    continue;

                // "./" and "./#docs" are the front page, with or without a fragment.
                var file = target.Split('#')[0];
                if (file is "" or "./")
                    file = FrontPage;

                checkedLinks++;

                if (!File.Exists(Path.Combine(docs, file.Replace('/', Path.DirectorySeparatorChar))))
                    broken.Add($"{RepoSource.Relative(page)} -> {target}");
            }
        }

        checkedLinks.Should().BeGreaterThan(30,
            "this test is worthless if it cannot find the links it is meant to follow");
        broken.Should().BeEmpty("a relative link with no file behind it is a 404 on the published site");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Every markdown file under docs/: the sources the site is built from.</summary>
    private static IReadOnlyList<string> MarkdownSources() =>
        Directory.GetFiles(Path.Combine(RepoSource.Root, Docs), "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The pages build-docs.py writes: every .html under docs/ except the hand-written front page.</summary>
    private static IReadOnlyList<string> GeneratedPages() =>
        AllPages()
            .Where(path => !Path.GetFileName(path).Equals(FrontPage, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static IReadOnlyList<string> AllPages() =>
        Directory.GetFiles(Path.Combine(RepoSource.Root, Docs), "*.html", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
