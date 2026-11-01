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
    }
}
