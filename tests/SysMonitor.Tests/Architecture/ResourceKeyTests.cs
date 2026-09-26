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
/// application's (App.xaml and the dictionaries it merges); the WinUI styles the app borrows are named below,
/// each with the reason it is safe.
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

    /// <summary>WinUI's own resources the app asks for by name. XamlControlsResources, merged first in App.xaml, defines them.</summary>
    private static readonly Dictionary<string, string> FrameworkResources = new(StringComparer.Ordinal)
    {
        ["AccentButtonStyle"] = "Fluent's accent button style",
        ["BodyTextBlockStyle"] = "Fluent's body text style",
        ["DefaultContentDialogStyle"] = "Fluent's dialog style, which a dialog built in code has to be given by name",
        ["DefaultTextBoxStyle"] = "Fluent's text box style",
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
    public void EveryFrameworkResourceNamedHereIsStillUsed()
    {
        var used = XamlFiles().SelectMany(file => XDocument.Load(file).Descendants().SelectMany(ResourceKeyRule.ReferencesOn))
            .Concat(RepoSource.FilesUnder(AppFolder).SelectMany(file =>
                CodeLookup.Matches(File.ReadAllText(file)).Select(match => match.Groups["key"].Value)))
            .ToHashSet(StringComparer.Ordinal);

        FrameworkResources.Keys.Where(key => !used.Contains(key)).Should().BeEmpty(
            "an exception nothing needs is a hole for the next missing key to fall through");
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<string> XamlFiles() => RepoSource.FilesUnder(AppFolder, "*.xaml");

    /// <summary>What every element can see: App.xaml's dictionaries and the framework resources named above.</summary>
    private static HashSet<string> ApplicationKeys()
    {
        var app = Path.Combine(RepoSource.Root, AppFolder, "App.xaml");
        var document = XDocument.Load(app);
        var keys = new HashSet<string>(FrameworkResources.Keys, StringComparer.Ordinal);

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

    /// <summary>A merged dictionary's Source: <c>ms-appx:///</c> from the project, anything else from the file naming it.</summary>
    private static (string Path, XDocument Document)? OpenDictionary(string declaringFile, string source)
    {
        const string AppScheme = "ms-appx:///";
        var path = source.StartsWith(AppScheme, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(RepoSource.Root, AppFolder, source[AppScheme.Length..])
            : Path.Combine(Path.GetDirectoryName(declaringFile)!, source);

        path = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? (path, XDocument.Load(path)) : null;
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;
}
