using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A <c>{StaticResource}</c> or <c>{ThemeResource}</c> that names nothing compiles, and throws when the page is
/// opened. Nothing checks it before a user does: the XAML compiler does not resolve resource keys.
/// <para>
/// The restyle replaces nearly every brush, style and font on every page with a named token, so this is checked
/// here, statically, for every page at once. An element sees its own <c>Resources</c>, its ancestors', and the
/// application's: App.xaml, the dictionaries it merges, and through XamlControlsResources every key WinUI
/// provides, as scripts/list-winui-keys.py read them out of the Windows App SDK (<see cref="WinUIResources"/>).
/// </para>
/// <para>
/// The same list says when an app resource takes a WinUI name. That replaces WinUI's resource for every control
/// that reads it, which is how the console restyles the framework's own controls - and, done by accident, how a
/// token would quietly restyle them wrong. So each one the app makes is named below with its reason.
/// </para>
/// <para>
/// Code is held to the same rule. <c>Application.Current.Resources["X"]</c> sees the application's dictionaries;
/// a page's own <c>Resources["X"]</c> sees only that page's dictionary, not App.xaml's. The PDF editor's sticky
/// note asked its page for <c>AccentButtonStyle</c>, which lives in App.xaml's merged Fluent resources, and threw
/// the moment the note opened.
/// </para>
/// </summary>
public class ResourceKeyTests
{
    private const string AppFolder = "src/SysMonitor.App";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string AppProject = "src/SysMonitor.App/SysMonitor.App.csproj";

    /// <summary>
    /// Every key in the Fluent overrides replaces one of WinUI's, which is that file's whole purpose;
    /// FluentOverrideTests holds it to that. Everywhere else, an override has to be named below.
    /// </summary>
    private const string FluentOverrides = "src/SysMonitor.App/Styles/Console/FluentOverrides.xaml";

    /// <summary>The WinUI resources the app replaces on purpose outside the Fluent overrides, each with what it does.</summary>
    private static readonly Dictionary<string, string> DeliberateOverrides = new(StringComparer.Ordinal)
    {
        ["ContentControlThemeFontFamily"] = "Fonts.xaml: the framework's own controls set their text in Public Sans",
        ["NavigationViewContentBackground"] = "MainWindow.xaml: the page area behind the navigation rail",
        ["NavigationViewDefaultPaneBackground"] = "MainWindow.xaml: the navigation rail's pane",
        ["NavigationViewExpandedPaneBackground"] = "MainWindow.xaml: the navigation rail's pane, expanded",
    };

    private static readonly Regex CodeLookup = new(
        @"(?<application>Application\.Current\.)?Resources\[\s*""(?<key>[^""]+)""\s*\]", RegexOptions.Compiled);

    // ---------------------------------------------------------------- the rule, on cases whose answer is known

    [Fact]
    public void TheRuleSeesWhatAnElementCanReachAndNothingElse()
    {
        const string Page = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Page.Resources>
                    <SolidColorBrush x:Key="PageBrush" Color="Red"/>
                </Page.Resources>
                <StackPanel>
                    <Grid>
                        <Grid.Resources>
                            <SolidColorBrush x:Key="SiblingBrush" Color="Blue"/>
                        </Grid.Resources>
                    </Grid>
                    <Border Background="{StaticResource PageBrush}"/>
                    <Border Background="{ThemeResource AppBrush}"/>
                    <TextBlock Visibility="{Binding Shown, Converter={StaticResource ResourceKey=MissingConverter}}"/>
                    <Border Background="{StaticResource SiblingBrush}"/>
                </StackPanel>
            </Page>
            """;

        var unresolved = ResourceKeyRule.Unresolved(XDocument.Parse(Page, LoadOptions.SetLineInfo), "page.xaml",
            new HashSet<string> { "AppBrush" }, (_, _) => null);

        unresolved.Select(reference => reference.Key).Should().BeEquivalentTo(
            ["MissingConverter", "SiblingBrush"],
            "an ancestor's resources and the application's are reachable; a sibling's and a missing one are not");
    }

    [Fact]
    public void AMergedDictionaryCountsAsPartOfTheOneThatMergesIt()
    {
        const string Tokens = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Default">
                        <SolidColorBrush x:Key="PlateBrush" Color="Black"/>
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """;
        const string Styles = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="Tokens.xaml"/>
                </ResourceDictionary.MergedDictionaries>
                <Style x:Key="PlateStyle" TargetType="Border">
                    <Setter Property="Background" Value="{ThemeResource PlateBrush}"/>
                </Style>
            </ResourceDictionary>
            """;

        var tokens = XDocument.Parse(Tokens);
        ResourceKeyRule.Unresolved(XDocument.Parse(Styles, LoadOptions.SetLineInfo), "Styles.xaml", new HashSet<string>(),
                (_, source) => source == "Tokens.xaml" ? ("Tokens.xaml", tokens) : null)
            .Should().BeEmpty("a theme token in a merged dictionary is reachable from the dictionary that merges it");
    }

    // ---------------------------------------------------------------- the application

    [Fact]
    public void EveryResourceTheXamlAsksForIsOneItCanReach()
    {
        var applicationKeys = ApplicationKeys();
        var unresolved = new List<string>();
        var references = 0;

        foreach (var file in XamlFiles())
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            references += document.Descendants().Sum(element => ResourceKeyRule.ReferencesOn(element).Count());

            foreach (var reference in ResourceKeyRule.Unresolved(document, file, applicationKeys, OpenDictionary))
                unresolved.Add($"{RepoSource.Relative(file)}:{reference.Line} {reference.Key}");
        }

        references.Should().BeGreaterThan(500, "this test is worthless if it cannot find the references it judges");
        unresolved.Should().BeEmpty("each of these throws when its page opens, and nothing else would say so first");
    }

    [Fact]
    public void EveryResourceTheCodeAsksForIsOneItCanReach()
    {
        var applicationKeys = ApplicationKeys();
        var unresolved = new List<string>();

        foreach (var file in RepoSource.FilesUnder(AppFolder))
        {
            var source = File.ReadAllText(file);
            foreach (Match lookup in CodeLookup.Matches(source))
            {
                var key = lookup.Groups["key"].Value;
                var reachable = lookup.Groups["application"].Success
                    ? applicationKeys.Contains(key)
                    : OwnKeys(file).Contains(key);

                if (!reachable)
                    unresolved.Add($"{RepoSource.Relative(file)}:{LineOf(source, lookup.Index)} {lookup.Value}");
            }
        }

        unresolved.Should().BeEmpty(
            "a page's own Resources do not look in App.xaml, so a key that lives there has to be asked of Application.Current");
    }

    [Fact]
    public void AResourceTakesAWinUINameOnlyOnPurpose()
    {
        // A theme dictionary's own key (Default, Light, HighContrast) names a theme, not a resource.
        var defined = XamlFiles()
            .Where(file => RepoSource.Relative(file) != FluentOverrides)
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(element => element.Parent?.Name.LocalName != "ResourceDictionary.ThemeDictionaries")
                .Select(element => element.Attribute(Xaml + "Key")?.Value)
                .OfType<string>()
                .Select(key => (File: RepoSource.Relative(file), Key: key)))
            .ToList();

        defined.Where(entry => WinUIResources.Keys.Contains(entry.Key) && !DeliberateOverrides.ContainsKey(entry.Key))
            .Select(entry => $"{entry.File}: {entry.Key}")
            .Should().BeEmpty("a resource under a WinUI name replaces WinUI's for every control that reads it");

        DeliberateOverrides.Keys.Where(key => defined.All(entry => entry.Key != key)).Should().BeEmpty(
            "an override nothing defines any more is a hole for the next accidental one to fall through");
    }

    // ---------------------------------------------------------------- the list of WinUI's keys

    [Fact]
    public void TheWinUIKeysAreTheOnesOfTheWindowsAppSdkTheAppBuildsWith()
    {
        var reference = XDocument.Load(Path.Combine(RepoSource.Root, AppProject)).Descendants("PackageReference")
            .Single(element => (string?)element.Attribute("Include") == "Microsoft.WindowsAppSDK");

        WinUIResources.PackageVersion.Should().Be((string?)reference.Attribute("Version"),
            "the list is read from one version of the Windows App SDK; after an upgrade, run scripts/list-winui-keys.py again");
    }

    [Fact]
    public void TheWinUIKeysAreTheWholeThemeDictionary()
    {
        WinUIResources.Keys.Count.Should().BeGreaterThan(3000, "WinUI 1.5's theme dictionary defines more than 3,000 keys");
        WinUIResources.Keys.Should().Contain(
            ["AccentButtonStyle", "DefaultContentDialogStyle", "ControlCornerRadius", "TextFillColorPrimaryBrush",
             "SystemColorWindowColor", "SystemColorWindowTextColor", "SystemColorHighlightColor", "SystemAccentColor"],
            "these are keys the app relies on, from WinUI's styles, its theme brushes and the system colours it supplies");
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<string> XamlFiles() => RepoSource.FilesUnder(AppFolder, "*.xaml");

    /// <summary>What every element can see: App.xaml's dictionaries and WinUI's resources.</summary>
    private static HashSet<string> ApplicationKeys()
    {
        var app = Path.Combine(RepoSource.Root, AppFolder, "App.xaml");
        var document = XDocument.Load(app);
        var keys = new HashSet<string>(WinUIResources.Keys, StringComparer.Ordinal);

        foreach (var resources in document.Root!.Elements().Where(element => element.Name.LocalName == "Application.Resources"))
            ResourceKeyRule.AddKeys(resources, app, keys, new HashSet<string>(StringComparer.OrdinalIgnoreCase), OpenDictionary);

        return keys;
    }

    /// <summary>The keys in the page's own dictionary: the Resources on the root of the XAML a code-behind file belongs to.</summary>
    private static HashSet<string> OwnKeys(string codeFile)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var markup = codeFile.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) ? codeFile[..^3] : null;
        if (markup is null || !File.Exists(markup))
            return keys;

        var root = XDocument.Load(markup).Root!;
        foreach (var resources in root.Elements().Where(element => element.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
            ResourceKeyRule.AddKeys(resources, markup, keys, new HashSet<string>(StringComparer.OrdinalIgnoreCase), OpenDictionary);

        return keys;
    }

    private static (string Path, XDocument Document)? OpenDictionary(string declaringFile, string source) =>
        ProjectDictionary.Open(declaringFile, source);

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;
}
