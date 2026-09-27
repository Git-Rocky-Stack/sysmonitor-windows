using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Styles/Console/FluentOverrides.xaml restates WinUI's own resources in the console palette, and either half of
/// that can go wrong without anything saying so.
/// <para>
/// An override under a key WinUI does not have - one letter off - is a resource nobody asks for. The build
/// passes, the smoke run passes, and the control keeps WinUI's colour. So every key is checked against the list
/// of WinUI's own (scripts/list-winui-keys.py).
/// </para>
/// <para>
/// And an override written in a colour of its own is exactly the drift the palette exists to stop, in the one
/// file besides it that is allowed to write colours. So every colour in it has to be one of the palette's
/// (Styles/Console/Tokens.xaml).
/// </para>
/// </summary>
public class FluentOverrideTests
{
    private const string Overrides = "src/SysMonitor.App/Styles/Console/FluentOverrides.xaml";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary><c>#RRGGBB</c> or <c>#AARRGGBB</c>, wherever it is written.</summary>
    private static readonly Regex Colour = new(@"#(?<hex>[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b", RegexOptions.Compiled);

    [Fact]
    public void EveryOverrideReplacesAWinUIResource()
    {
        // A theme dictionary's own key (Default, Light, HighContrast) names a theme, not a resource.
        var keys = XDocument.Load(PathOf(Overrides)).Descendants()
            .Where(element => element.Parent?.Name.LocalName != "ResourceDictionary.ThemeDictionaries")
            .Select(element => element.Attribute(Xaml + "Key")?.Value)
            .OfType<string>()
            .ToList();

        keys.Should().HaveCountGreaterThan(20, "this test is worthless if it cannot find the overrides it judges");
        keys.Where(key => !WinUIResources.Keys.Contains(key)).Distinct().Should().BeEmpty(
            "an override WinUI never asks for changes nothing: the control keeps WinUI's colour, and nothing else says so");
    }

    [Fact]
    public void EveryOverrideColourIsOneOfThePalettes()
    {
        var palette = Colours(File.ReadAllText(PathOf(Tokens))).ToHashSet(StringComparer.Ordinal);
        var used = Colours(File.ReadAllText(PathOf(Overrides))).Distinct().ToList();

        used.Should().NotBeEmpty("the overrides restate WinUI's colours in the palette's");
        used.Where(colour => !palette.Contains(colour)).Should().BeEmpty(
            "the overrides apply the palette; a colour of their own belongs in Tokens.xaml first, where every theme has it");
    }

    private static string PathOf(string relative) => Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every colour written in a file, with #RRGGBB read as the opaque #FFRRGGBB it is.</summary>
    private static IEnumerable<string> Colours(string xaml) =>
        Colour.Matches(xaml).Select(match => match.Groups["hex"].Value.ToUpperInvariant())
            .Select(hex => hex.Length == 6 ? "FF" + hex : hex);
}
