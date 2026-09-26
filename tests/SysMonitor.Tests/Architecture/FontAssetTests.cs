using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A font that fails to load does not fail anything: DirectWrite quietly sets the text in Segoe UI and the page
/// looks nearly right. A <c>ms-appx:///Assets/Fonts/File.ttf#Family</c> reference loads only when the file ships
/// and holds a family by exactly that name, so both are checked here, for every reference the app makes.
/// <para>
/// The faces are licensed under the SIL Open Font License 1.1, which asks that every copy carries the copyright
/// notice and the license. Each family's OFL file has to sit beside its fonts, carrying the notice from the font
/// itself, and the family has to be named in THIRD-PARTY-NOTICES.md.
/// </para>
/// </summary>
public class FontAssetTests
{
    private const string FontFolder = "src/SysMonitor.App/Assets/Fonts";

    /// <summary>References meant not to resolve, each with its reason.</summary>
    private static readonly Dictionary<string, string> DeliberatelyMissing = new(StringComparer.Ordinal)
    {
        ["NotAFont.ttf"] = "the smoke run's baseline: a face that fails to load measures as this does",
    };

    private static readonly Regex FontReference = new(
        @"ms-appx:///Assets/Fonts/(?<file>[^#""<\s]+)#(?<family>[^""<,]+)", RegexOptions.Compiled);

    [Fact]
    public void EveryFontReferenceNamesAFileThatShipsAndAFamilyItHolds()
    {
        var unresolved = new List<string>();
        var references = 0;

        foreach (var file in RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml").Concat(RepoSource.FilesUnder("src/SysMonitor.App")))
        {
            foreach (Match reference in FontReference.Matches(File.ReadAllText(file)))
            {
                if (DeliberatelyMissing.ContainsKey(reference.Groups["file"].Value))
                    continue;

                references++;
                var font = Path.Combine(RepoSource.Root, FontFolder, reference.Groups["file"].Value);
                var family = reference.Groups["family"].Value.Trim();

                if (!File.Exists(font))
                    unresolved.Add($"{RepoSource.Relative(file)}: {reference.Value} - no such file");
                else if (TrueTypeNames.Family(TrueTypeNames.Read(font)) is var actual && actual != family)
                    unresolved.Add($"{RepoSource.Relative(file)}: {reference.Value} - the file's family is \"{actual}\"");
            }
        }

        references.Should().BeGreaterThan(9, "this test is worthless if it cannot find the references it judges");
        unresolved.Should().BeEmpty("each of these would fall back to Segoe UI without a word");
    }

    [Fact]
    public void EveryFontShipsWithTheApp()
    {
        var project = File.ReadAllText(Path.Combine(RepoSource.Root, "src/SysMonitor.App/SysMonitor.App.csproj"));

        project.Should().Contain(@"<Content Include=""Assets\Fonts\**\*"">",
            "a font the package leaves out loads nowhere but the developer's machine");
    }

    [Fact]
    public void EveryFamilyCarriesItsCopyrightNoticeAndTheLicense()
    {
        var fonts = Directory.GetFiles(Path.Combine(RepoSource.Root, FontFolder), "*.ttf");
        fonts.Should().NotBeEmpty();

        var notices = File.ReadAllText(Path.Combine(RepoSource.Root, "THIRD-PARTY-NOTICES.md"));
        var problems = new List<string>();

        foreach (var font in fonts)
        {
            var names = TrueTypeNames.Read(font);
            var family = Path.GetFileNameWithoutExtension(font).Split('-')[0];
            var licence = Path.Combine(Path.GetDirectoryName(font)!, $"OFL-{family}.txt");

            if (!File.Exists(licence))
            {
                problems.Add($"{Path.GetFileName(font)}: no OFL-{family}.txt beside it");
                continue;
            }

            var text = File.ReadAllText(licence);
            var notice = Ascii(names.GetValueOrDefault(0) ?? "");
            if (notice.Length == 0 || !text.StartsWith(notice, StringComparison.Ordinal))
                problems.Add($"OFL-{family}.txt does not open with the font's own notice, \"{notice}\"");
            if (!text.Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal))
                problems.Add($"OFL-{family}.txt does not carry the license");

            // "PublicSans" is Public Sans, "DepartureMono" Departure Mono: the family as the notices write it.
            var displayName = Regex.Replace(family, "(?<=[a-z])(?=[A-Z])", " ");
            if (!notices.Contains($"| {displayName} |", StringComparison.Ordinal))
                problems.Add($"THIRD-PARTY-NOTICES.md has no row for {displayName}");
        }

        problems.Distinct().Should().BeEmpty("the SIL Open Font License asks that every copy carries both");
    }

    /// <summary>The notices ship as ASCII, so a dash in a font's notice is written as a hyphen.</summary>
    private static string Ascii(string text) => text.Replace('–', '-').Replace('—', '-');
}
