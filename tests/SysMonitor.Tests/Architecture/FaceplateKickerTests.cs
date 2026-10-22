using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A page's major sections are Faceplates, and each stripe carries the section's code the way System-X writes it:
/// <c>{MODULE} - {SECTION} - {INDEX}{LETTER}</c>, the module and index the page's ViewHeader already carries, and the
/// letters running A, B, C down the page. A kicker typed by hand drifts the first time a section moves or a page is
/// renumbered, and the stripe then names a panel that is not there; the serial is stamped by SerialFor for the same
/// reason. Faceplates inside the instruments' own templates take their kicker by TemplateBinding and are not judged.
/// </summary>
public class FaceplateKickerTests
{
    private static readonly Regex ViewHeader = new(@"<instruments:ViewHeader\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Faceplate = new(@"<instruments:Faceplate(?=[\s>])[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"\b(?<name>Module|Index|Kicker|Serial)=""(?<value>[^""]*)""", RegexOptions.Compiled);

    [Fact]
    public void EverySectionFaceplateIsNumberedByItsPage()
    {
        var problems = new List<string>();
        var judged = 0;

        foreach (var file in RepoSource.FilesUnder("src/SysMonitor.App/Views", "*.xaml"))
        {
            var source = File.ReadAllText(file);
            var header = ViewHeader.Match(source);
            var plates = Faceplate.Matches(source).Select(match => Attributes(match.Value)).ToList();
            if (plates.Count == 0)
                continue;

            if (!header.Success)
            {
                problems.Add($"{RepoSource.Relative(file)}: has faceplates but no ViewHeader to number them by");
                continue;
            }

            var page = Attributes(header.Value);
            var letter = 'A';
            foreach (var plate in plates)
            {
                judged++;
                plate.TryGetValue("Kicker", out var kicker);
                plate.TryGetValue("Serial", out var serial);
                var expected = new Regex($@"^{Regex.Escape(page["Module"])} - .+ - {Regex.Escape(page["Index"])}{letter}$");

                if (kicker is null || !expected.IsMatch(kicker))
                    problems.Add($"{RepoSource.Relative(file)}: kicker \"{kicker}\" should read \"{page["Module"]} - <SECTION> - {page["Index"]}{letter}\"");
                if (serial is null || !serial.StartsWith("{instruments:SerialFor Module=" + page["Module"] + "-", StringComparison.Ordinal))
                    problems.Add($"{RepoSource.Relative(file)}: serial \"{serial}\" should be stamped by SerialFor from {page["Module"]}-<SECTION>");

                letter++;
            }
        }

        judged.Should().BeGreaterThan(80, "this test is worthless if it cannot find the faceplates it judges");
        problems.Should().BeEmpty();
    }

    private static Dictionary<string, string> Attributes(string tag) =>
        Attribute.Matches(tag).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value);
}
