using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A <c>{ThemeResource}</c> is looked up in the dictionary for the theme the element is showing. A key defined for
/// the dark theme and forgotten in the light one resolves on every screen a developer checks in dark, and throws
/// the first time someone opens the page in light.
/// <para>
/// So every set of theme dictionaries has to define the same keys in each theme, and has to cover the three the
/// app shows: Default (the dark Night Ops shift, which Dark lookups fall back to), Light (Day Shift) and
/// HighContrast.
/// </para>
/// <para>
/// Inside a theme, a <c>StaticResource</c> is resolved once, when the dictionary loads, so it has to name a key of
/// that same theme - a brush in the Light theme built from the Default theme's colour would be Night on Day - or
/// one WinUI supplies, as the High Contrast theme's system colours are.
/// </para>
/// </summary>
public class ThemeDictionaryParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] RequiredThemes = ["Default", "Light", "HighContrast"];

    /// <summary>
    /// How many sets of theme dictionaries the app has at least: Styles/Console/Tokens.xaml. It rises with each
    /// new set, so the test can never quietly pass over nothing.
    /// </summary>
    private const int MinimumThemeSets = 1;

    private static readonly Regex StaticReference = new(
        @"\{StaticResource\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s,{}]+)\s*\}", RegexOptions.Compiled);

    [Fact]
    public void TheRuleSeesAKeyMissingFromOneTheme()
    {
        const string Dictionary = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Default">
                        <SolidColorBrush x:Key="PlateBrush" Color="Black"/>
                        <SolidColorBrush x:Key="WellBrush" Color="Black"/>
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Light">
                        <SolidColorBrush x:Key="PlateBrush" Color="White"/>
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """;

        Mismatches(XDocument.Parse(Dictionary), "tokens.xaml").Should().BeEquivalentTo(
            ["tokens.xaml: no HighContrast theme", "tokens.xaml: Light has no WellBrush"]);
    }

    [Fact]
    public void TheRuleSeesAStaticResourceReachingOutOfItsTheme()
    {
        const string Dictionary = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Default">
                        <Color x:Key="PlateColor">#1C1C1C</Color>
                        <Color x:Key="NightOnlyColor">#000000</Color>
                        <SolidColorBrush x:Key="PlateBrush" Color="{StaticResource PlateColor}"/>
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Light">
                        <SolidColorBrush x:Key="PlateBrush" Color="{StaticResource NightOnlyColor}"/>
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="HighContrast">
                        <StaticResource x:Key="PlateColor" ResourceKey="SystemColorWindowColor"/>
                        <SolidColorBrush x:Key="PlateBrush" Color="{ThemeResource SystemColorWindowColor}"/>
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """;

        OutOfThemeReferences(XDocument.Parse(Dictionary), "tokens.xaml", new HashSet<string> { "SystemColorWindowColor" })
            .Should().Equal(["tokens.xaml: Light asks for NightOnlyColor"],
                "a sibling in the same theme and a key WinUI supplies are reachable; another theme's key is not");
    }

    [Fact]
    public void EveryThemeDefinesTheSameKeys()
    {
        var mismatches = new List<string>();
        var themeSets = 0;

        foreach (var file in RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml"))
        {
            var document = XDocument.Load(file);
            themeSets += document.Descendants().Count(IsThemeSet);
            mismatches.AddRange(Mismatches(document, RepoSource.Relative(file)));
        }

        themeSets.Should().BeGreaterThanOrEqualTo(MinimumThemeSets);
        mismatches.Should().BeEmpty("a key one theme lacks throws the first time a page opens in that theme");
    }

    [Fact]
    public void EveryStaticResourceInAThemeNamesAKeyOfThatTheme()
    {
        var reaching = RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml")
            .SelectMany(file => OutOfThemeReferences(XDocument.Load(file), RepoSource.Relative(file), WinUIResources.Keys));

        reaching.Should().BeEmpty("a theme's StaticResource binds once, to that theme's key or to one WinUI supplies");
    }

    private static bool IsThemeSet(XElement element) => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries";

    private static IEnumerable<string> OutOfThemeReferences(XDocument document, string file, IReadOnlySet<string> supplied)
    {
        foreach (var theme in document.Descendants().Where(IsThemeSet).SelectMany(set => set.Elements()))
        {
            var name = theme.Attribute(Xaml + "Key")?.Value ?? "(unnamed)";
            var own = theme.Descendants().Select(entry => entry.Attribute(Xaml + "Key")?.Value).OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            var asked = theme.Descendants().SelectMany(entry => entry.Attributes()
                    .SelectMany(attribute => StaticReference.Matches(attribute.Value).Select(match => match.Groups["key"].Value))
                    .Concat(entry.Name.LocalName == "StaticResource" && entry.Attribute("ResourceKey") is { } key ? [key.Value] : []));

            foreach (var key in asked.Distinct().Where(key => !own.Contains(key) && !supplied.Contains(key)).Order(StringComparer.Ordinal))
                yield return $"{file}: {name} asks for {key}";
        }
    }

    private static IEnumerable<string> Mismatches(XDocument document, string file)
    {
        foreach (var set in document.Descendants().Where(IsThemeSet))
        {
            var themes = set.Elements()
                .Where(theme => theme.Attribute(Xaml + "Key") is not null)
                .ToDictionary(
                    theme => theme.Attribute(Xaml + "Key")!.Value,
                    theme => theme.Descendants().Select(entry => entry.Attribute(Xaml + "Key")?.Value)
                        .OfType<string>().ToHashSet(StringComparer.Ordinal));

            foreach (var required in RequiredThemes.Where(required => !themes.ContainsKey(required)))
                yield return $"{file}: no {required} theme";

            var every = themes.Values.SelectMany(keys => keys).ToHashSet(StringComparer.Ordinal);
            foreach (var (theme, keys) in themes)
            {
                foreach (var missing in every.Where(key => !keys.Contains(key)).Order(StringComparer.Ordinal))
                    yield return $"{file}: {theme} has no {missing}";
            }
        }
    }
}
