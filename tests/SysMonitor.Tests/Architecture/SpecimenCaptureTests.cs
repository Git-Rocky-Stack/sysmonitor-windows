using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The UI smoke run's pictures of the specimen are how the console's look is reviewed, so the readouts and meters -
/// the LCDs, VU meters, LED meters and ACT light - have to be in them. Two things kept them out. The run scrolled
/// with <c>ChangeView</c> and took the picture two frames later, before the scroll had landed, so pictures showed
/// wherever the scroller happened to be (one run's first picture was the foot of the page, and two in a row were the
/// same). And nothing aimed at the instruments: they were only ever in a picture by luck of the offsets.
/// </summary>
public class SpecimenCaptureTests
{
    private const string Specimen = "src/SysMonitor.App/Diagnostics/ConsoleSpecimen.xaml";
    private const string SpecimenCode = "src/SysMonitor.App/Diagnostics/ConsoleSpecimen.xaml.cs";
    private const string SmokeRun = "src/SysMonitor.App/Diagnostics/UiSmokeRun.cs";

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheSpecimenNamesTheFaceplateThatHoldsTheReadoutsAndMeters()
    {
        var probe = XDocument.Load(Path.Combine(RepoSource.Root, Specimen)).Descendants()
            .SingleOrDefault(element => element.Attribute(X + "Name")?.Value == "InstrumentsProbe");

        probe.Should().NotBeNull("the smoke run finds the readouts and meters by name, not by guessing an offset");
        probe!.Name.LocalName.Should().Be("Faceplate");

        var held = probe.Descendants().Select(element => element.Name.LocalName).ToHashSet();
        held.Should().Contain(["Lcd", "VuMeter", "ProgressBar", "ActivityLed"],
            "the named faceplate is the one the review needs: LCDs, VU meters, LED meters and the ACT light");

        File.ReadAllText(Path.Combine(RepoSource.Root, SpecimenCode)).Should()
            .MatchRegex(@"internal\s+Faceplate\s+Instruments\s*=>\s*InstrumentsProbe\s*;");
    }

    [Fact]
    public void TheSmokeRunPhotographsTheReadoutsAndMetersAfterEachScrollHasLanded()
    {
        var source = File.ReadAllText(Path.Combine(RepoSource.Root, SmokeRun));

        source.Should().Contain("specimen.Instruments", "the run aims a picture at the readouts and meters");
        source.Should().Contain("-specimen-instruments-", "those pictures are named for what they show");

        Regex.Matches(source, @"\.ChangeView\(").Count.Should().Be(1,
            "every scroll goes through the one helper that waits for it to land");
        source.Should().MatchRegex(@"ScrolledToAsync\([^)]*\)[\s\S]*?\.ChangeView\([\s\S]*?VerticalOffset",
            "that helper scrolls, then reads the offset back until the scroller has reached it");

        // Landing is not enough. The picture is PrintWindow's, which is the frame Windows last put on the screen,
        // and that trails XAML's own frame: with the offset read back but no settle, each picture showed the
        // scroll before it - two in a row the same, and the instruments' picture the foot of the page. A page's
        // picture waits SettledAsync for the same reason.
        // To the method's closing brace, whichever line ending the checkout has: CRLF on Windows.
        var helper = Regex.Match(source, @"Task<bool>\s+ScrolledToAsync\([\s\S]*?\r?\n    }\r?\n").Value;
        helper.Should().NotBeEmpty("this test has to find the helper before it can judge it");
        helper.Should().Contain("await SettledAsync()",
            "the scroll is settled on the screen, as a page is, before anything is photographed");
    }
}
