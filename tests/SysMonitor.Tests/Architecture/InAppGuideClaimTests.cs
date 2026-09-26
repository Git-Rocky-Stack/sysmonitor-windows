using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// <c>Views/UserGuidePage.xaml</c> is the guide that ships inside the application, so a sentence that is
/// wrong here is wrong on every installed copy until the next release. That makes it a stricter surface
/// than the markdown guide, which nothing reads at runtime.
/// <para>
/// Two sentences in it described a result the code does not produce. "One-click cleanup to free space and
/// boost performance instantly" promised a speed-up: Quick Clean deletes temporary files from eight fixed
/// locations (<c>src/SysMonitor.Core/Services/Cleaners/TempFileCleaner.cs:72-79</c>) and reports a file
/// count. Whether the machine then runs faster is not measured, not reported and, on a disk that was not
/// close to full, not true. "Free space and boost performance" subtitled a card whose six tools include a
/// registry cleaner and a startup manager, neither of which frees space.
/// </para>
/// <para>
/// This follows <see cref="UserGuideClaimTests"/>: each fact reads the guide as well as the code, so the
/// sentence cannot be quietly deleted and leave the test passing against nothing. The second fact bans the
/// vocabulary rather than the sentence, because the failure mode is a new string with the same problem,
/// not the old string coming back.
/// </para>
/// </summary>
public class InAppGuideClaimTests
{
    private const string Guide = "src/SysMonitor.App/Views/UserGuidePage.xaml";
    private const string Dashboard = "src/SysMonitor.App/ViewModels/DashboardViewModel.cs";

    /// <summary>
    /// Claims that promise the user an outcome this application does not measure. Every one of these
    /// shipped in the interface at some point, and none can be checked by the person reading it - which is
    /// what makes the vocabulary worth banning rather than each sentence worth rewording.
    /// </summary>
    private static readonly (Regex Pattern, string Why)[] UnmeasurableClaims =
    [
        (Claim(@"boost(s|ing|ed)?[ _-]*(performance|speed)"),
            "nothing in the app measures performance before and after any operation"),
        (Claim(@"instantly"),
            "every operation here scans a filesystem or the registry before it does anything"),
        (Claim(@"speed(s)?[ _-]+up[ _-]+your"),
            "no operation is timed against a baseline, so there is no speed-up to report"),
        (Claim(@"(free(s|d|ing)?[ _-]+up[ _-]+(ram|memory)|(ram|memory)[ _-]+freed|freed\b.{0,24}\bof[ _-]+(ram|memory)|boost(s|ing|ed)?[ _-]*(ram|memory))"),
            "trimming a working set moves pages to the standby list; Windows can page them straight " +
            "back, so no RAM is freed. This is the BOOST RAM claim under its other names - and under " +
            "\"memory\" as well as \"RAM\": the Memory page reported the same trim as freed memory, and " +
            "the guide listed Game Mode's session stats as memory freed, after both RAM wordings had gone"),
    ];

    /// <summary>Every XAML page and view model the application ships: all the text a user can read in it.</summary>
    private static readonly string[] ShippedInterface =
    [
        "src/SysMonitor.App/Views",
        "src/SysMonitor.App/ViewModels",
    ];

    private static Regex Claim(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Release notes have to name the label they replaced - "the button that read BOOST RAM now reads
    /// TRIM MEMORY" is the sentence a reader needs, and it cannot be written without the old words. A
    /// quotation of a corrected claim is not a claim, so the guide marks those lines and this test skips
    /// them. The exemption is bounded below: one region, and short enough that it cannot become a place
    /// to hide a new promise.
    /// </summary>
    private const string QuotingStart = "claim-check:quoting-start";

    private const string QuotingEnd = "claim-check:quoting-end";

    /// <summary>Long enough for a release's notes, too short to shelter a page.</summary>
    private const int LongestQuotingRegion = 30;

    [Fact]
    public void TheQuickCleanTipDescribesWhatQuickCleanActuallyDoes()
    {
        StillClaimed("One click deletes temporary files and tells you how many it removed.");

        // "deletes temporary files": the command runs the temp-file cleaner, not some general optimiser.
        ShouldMatch(Dashboard, @"QuickCleanAsync[\s\S]{0,400}?_tempFileCleaner\.CleanAsync",
            "the tip promises deletion, so the command has to be the one that deletes");

        // "tells you how many it removed": the count is the only result the user is shown.
        ShouldMatch(Dashboard, @"QuickCleanAsync[\s\S]{0,400}?ShowActionStatus\(\$""Cleaned \{cleaned\.FilesDeleted\}",
            "the tip promises a count of removed files, and this is the line that reports it");
    }

    [Fact]
    public void TheCleanupCardSubtitleNamesOnlyThingsTheToolsUnderItDo()
    {
        StillClaimed("Reclaim disk space, clear traces, and control what runs at startup");

        // One tool behind each of the three promises. A subtitle is a summary of the card, so a promise
        // with nothing under it is the same defect as a promise the code cannot keep.
        ShouldContain(Guide, "Directory Cleaner", "\"reclaim disk space\" needs the tool that removes files");
        ShouldContain(Guide, "Browser Privacy", "\"clear traces\" needs the tool that clears browsing data");
        ShouldContain(Guide, "Startup Manager", "\"control what runs at startup\" needs the startup tool");

        // Reachable means two things: a menu entry carrying the tag, and a tag the router maps to a page.
        // Either half alone is a dead end, and a dead end is not a capability a subtitle can promise.
        Reachable("DirectoryCleaner", "CleanerPage");
        Reachable("BrowserPrivacy", "BrowserPrivacyPage");
        Reachable("Startup", "StartupPage");
    }

    /// <summary>
    /// The guide was not the only place this vocabulary lived. The Game Mode page called the same
    /// working-set trim "Frees Up RAM", subtitled it "Optimizes memory for gaming", and labelled its
    /// result "RAM Freed" - three copies of the claim the Dashboard button had already been corrected
    /// for. So the ban is on the interface, not on one file in it.
    /// </summary>
    [Fact]
    public void NoShippedScreenPromisesSomethingTheAppDoesNotMeasure()
    {
        var screens = ShippedInterface
            .SelectMany(folder => Directory.EnumerateFiles(
                Path.Combine(RepoSource.Root, folder.Replace('/', Path.DirectorySeparatorChar)),
                "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Append(Path.Combine(RepoSource.Root, "src", "SysMonitor.App", "MainWindow.xaml"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        screens.Should().HaveCountGreaterThan(30,
            "this test is worthless if it cannot find the pages it is meant to read");

        var found = new List<string>();
        var regions = 0;

        foreach (var path in screens)
        {
            var lines = File.ReadAllLines(path);
            var quoting = false;
            var regionStartedAt = 0;

            for (var number = 1; number <= lines.Length; number++)
            {
                var line = lines[number - 1];

                if (line.Contains(QuotingStart, StringComparison.Ordinal))
                {
                    quoting.Should().BeFalse($"{RepoSource.Relative(path)}:{number} opens a quoting region inside one");
                    quoting = true;
                    regionStartedAt = number;
                    regions++;
                    continue;
                }

                if (line.Contains(QuotingEnd, StringComparison.Ordinal))
                {
                    quoting.Should().BeTrue($"{RepoSource.Relative(path)}:{number} closes a quoting region that never opened");
                    (number - regionStartedAt).Should().BeLessThanOrEqualTo(LongestQuotingRegion,
                        $"the quoting region at {RepoSource.Relative(path)}:{regionStartedAt} is long enough " +
                        "to hide a new promise in; release notes for one release fit in far less");
                    quoting = false;
                    continue;
                }

                if (quoting)
                    continue;

                foreach (var (pattern, why) in UnmeasurableClaims)
                {
                    var hit = pattern.Match(line);
                    if (hit.Success)
                        found.Add($"{RepoSource.Relative(path)}:{number} \"{hit.Value}\" - {why}");
                }
            }

            quoting.Should().BeFalse($"{RepoSource.Relative(path)} opens a quoting region and never closes it");
        }

        // If the marker is ever deleted, the release notes start failing rather than the exemption
        // silently widening - but a marker nothing uses would mean this skip logic is untested, so the
        // count is asserted rather than assumed.
        regions.Should().Be(1,
            "exactly one region quotes corrected claims: the release notes card in the in-app guide");

        found.Should().BeEmpty(
            "this text ships inside the application, so an unverifiable promise here reaches every install");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// The guide still says this. Without it a fact could go on passing against a sentence someone deleted.
    /// </summary>
    private static void StillClaimed(string sentence) =>
        Source(Guide).Contains(sentence, StringComparison.Ordinal).Should().BeTrue(
            $"this fact is worthless if {Guide} has stopped saying \"{sentence}\"");

    /// <summary>
    /// The navigation menu offers this tag, and the router turns that tag into this page. Asserting on
    /// the pair is what makes "the user can get to it" a fact rather than a layout detail.
    /// </summary>
    private static void Reachable(string navigationTag, string page)
    {
        ShouldMatch("src/SysMonitor.App/MainWindow.xaml", $@"<NavigationViewItem[^>]*Tag=""{navigationTag}""",
            $"the menu has to offer {page}");
        ShouldMatch("src/SysMonitor.App/MainWindow.xaml.cs", $@"{{\s*""{navigationTag}""\s*,\s*typeof\({page}\)\s*}}",
            $"the {navigationTag} entry has to route somewhere, and {page} is where");
    }

    private static void ShouldContain(string relative, string marker, string because) =>
        Source(relative).Contains(marker, StringComparison.Ordinal).Should().BeTrue(
            $"{relative} has to contain \"{marker}\" - {because}");

    private static void ShouldMatch(string relative, string pattern, string because) =>
        Regex.IsMatch(Source(relative), pattern).Should().BeTrue(
            $"{relative} has to match /{pattern}/ - {because}");

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
