using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A console instrument is a class in Controls/Instruments and an implicit style in Styles/Console/Instruments.xaml,
/// and it is only proved to work where something shows it: the UI smoke run shows Diagnostics/ConsoleSpecimen.xaml
/// in both shifts and fails on an instrument that drew nothing.
/// <para>
/// So each half needs the others. A class with no implicit style draws nothing, and one with a key instead is
/// never applied by itself; a style whose class is gone fails when the dictionary loads; and an instrument the
/// specimen does not show - directly, or inside another instrument's template - is not checked by anything
/// until a page uses it.
/// </para>
/// </summary>
public class InstrumentSpecimenTests
{
    private const string InstrumentFolder = "src/SysMonitor.App/Controls/Instruments";
    private const string Styles = "src/SysMonitor.App/Styles/Console/Instruments.xaml";
    private const string Specimen = "src/SysMonitor.App/Diagnostics/ConsoleSpecimen.xaml";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string InstrumentNamespace = "using:SysMonitor.App.Controls.Instruments";

    /// <summary>A public class deriving from a WinUI control type, named <c>Control</c> or ending in it, or RangeBase.</summary>
    private static readonly Regex InstrumentClass = new(
        @"public\s+(?:sealed\s+)?(?:partial\s+)?class\s+(?<name>\w+)\s*:\s*(?<base>\w*Control|RangeBase)\b", RegexOptions.Compiled);

    [Fact]
    public void EveryInstrumentHasOneImplicitStyle()
    {
        var instruments = Instruments();
        var implicitStyles = StyledTypes(keyed: false);

        foreach (var instrument in instruments)
        {
            implicitStyles.Count(type => type == instrument).Should().Be(1,
                $"{instrument} draws nothing without an implicit style in {Styles}, and two would leave one of them dead");
        }
    }

    [Fact]
    public void EveryInstrumentStyleHasItsClass()
    {
        var instruments = Instruments();
        StyledTypes(keyed: false).Concat(StyledTypes(keyed: true)).Distinct()
            .Where(type => !instruments.Contains(type))
            .Should().BeEmpty("a style for a class that is gone fails when the dictionary loads, at startup");
    }

    [Fact]
    public void TheSpecimenShowsEveryInstrument()
    {
        var shown = UsedTypes(Specimen).Concat(UsedTypes(Styles)).ToHashSet(StringComparer.Ordinal);

        Instruments().Where(instrument => !shown.Contains(instrument)).Should().BeEmpty(
            "the smoke run proves an instrument's template only by showing it; one the specimen leaves out waits for the first page");
    }

    private static IReadOnlyList<string> Instruments()
    {
        var names = RepoSource.FilesUnder(InstrumentFolder)
            .SelectMany(file => InstrumentClass.Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value))
            .ToList();

        names.Should().Contain("Faceplate", "this test is worthless if it cannot find the instruments it judges");
        return names;
    }

    /// <summary>The instrument types the styles target, implicit (no x:Key) or keyed.</summary>
    private static IEnumerable<string> StyledTypes(bool keyed) =>
        XDocument.Load(PathOf(Styles)).Descendants(Presentation + "Style")
            .Where(style => (style.Attribute(Xaml + "Key") is not null) == keyed)
            .Select(style => InstrumentName(style, style.Attribute("TargetType")?.Value))
            .OfType<string>();

    /// <summary>Every instrument type a XAML file places as an element.</summary>
    private static IEnumerable<string> UsedTypes(string relative) =>
        XDocument.Load(PathOf(relative)).Descendants()
            .Where(element => element.Name.NamespaceName == InstrumentNamespace)
            .Select(element => element.Name.LocalName);

    /// <summary>The class a <c>prefix:Name</c> TargetType names, when the prefix is the instruments namespace.</summary>
    private static string? InstrumentName(XElement context, string? targetType)
    {
        if (targetType is null || targetType.Split(':') is not [var prefix, var name])
            return null;

        return context.GetNamespaceOfPrefix(prefix)?.NamespaceName == InstrumentNamespace ? name : null;
    }

    private static string PathOf(string relative) => Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
