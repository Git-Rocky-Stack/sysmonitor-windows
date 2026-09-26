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
/// </summary>
public class ThemeDictionaryParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] RequiredThemes = ["Default", "Light", "HighContrast"];

    /// <summary>
    /// How many sets of theme dictionaries the app has at least. None yet: the restyle's token dictionary is the
    /// first, and this rises with it so the test can never quietly pass over nothing.
    /// </summary>
    private const int MinimumThemeSets = 0;

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

    private static bool IsThemeSet(XElement element) => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries";

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
