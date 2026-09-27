using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// How the console's dictionaries are put together, which decides whether its colours follow the shift.
/// <para>
/// The UI smoke run switches the shift the way the app will while it runs - the window's theme set to Light while
/// the application's stays Dark - and reads colours back. A brush that wrote its colour out followed the shift in
/// every place its theme dictionary was tried: App.xaml's own, an element's own, a file App.xaml merges, and a file
/// merged into App.xaml's theme dictionary. The palette's brushes, which named their colours with
/// <c>{StaticResource XColor}</c> from the same theme, stayed Night Ops however they were used - by a style, on an
/// element, in a template - and still did once App.xaml merged the palette itself. So a brush in a shift's theme
/// dictionary writes its colour out. The palette also stays where it was measured to follow, merged by App.xaml
/// itself rather than three files down as it once was, and the console's other dictionaries find the palette, the
/// faces and each other among the files App.xaml merges ahead of them.
/// </para>
/// </summary>
public class ConsoleDictionaryTests
{
    private const string AppXaml = "src/SysMonitor.App/App.xaml";
    private const string ConsoleFolder = "src/SysMonitor.App/Styles/Console";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>App.xaml as the file system spells it, so it compares equal to the same file found by a search.</summary>
    private static string AppPath =>
        Path.GetFullPath(Path.Combine(RepoSource.Root, AppXaml.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void TheMergeRuleCatchesAPaletteMergedBelowAppXaml()
    {
        const string MergesPalette = """
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Palette.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            """;
        var palette = Dictionary("""
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Default"/>
            </ResourceDictionary.ThemeDictionaries>
            """);
        var files = new Dictionary<string, XDocument>
        {
            ["App.xaml"] = Dictionary(MergesPalette),
            ["Styles.xaml"] = Dictionary(MergesPalette),
            ["Palette.xaml"] = palette,
        };
        (string, XDocument)? Open(string declaring, string source) =>
            files.TryGetValue(source, out var document) ? (source, document) : null;

        ThemeDictionariesMergedBelowApp(files, "App.xaml", Open).Select(merge => $"{merge.File} merges {merge.Target}")
            .Should().Equal(["Styles.xaml merges Palette.xaml"],
                "a palette App.xaml merges itself is where it was measured to follow an element's theme, and another " +
                "file merging it takes it somewhere that never was");
    }

    [Fact]
    public void AFileWithThemeDictionariesIsMergedByAppXamlAlone()
    {
        var files = RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml")
            .ToDictionary(file => file, file => XDocument.Load(file), StringComparer.OrdinalIgnoreCase);
        var app = AppPath;

        ThemeDictionariesMergedBelowApp(files, app, ProjectDictionary.Open)
            .Select(merge => $"{RepoSource.Relative(merge.File)} merges {RepoSource.Relative(merge.Target)}")
            .Should().BeEmpty("a theme dictionary merged below App.xaml keeps Night Ops colours on Day Shift");

        AppMerges(app).Select(merge => RepoSource.Relative(merge.Path)).Should().Contain(
            [$"{ConsoleFolder}/Tokens.xaml", $"{ConsoleFolder}/FluentOverrides.xaml"],
            "the palette and the Fluent overrides are the dictionaries whose themes have to follow an element's");
    }

    [Fact]
    public void TheColourRuleCatchesANamedColourInAShiftAndLeavesHighContrastAlone()
    {
        var palette = Dictionary("""
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                    <Color x:Key="InkColor">#101010</Color>
                    <SolidColorBrush x:Key="InkBrush" Color="{StaticResource InkColor}"/>
                    <SolidColorBrush x:Key="PaperBrush" Color="#F0F0F0"/>
                </ResourceDictionary>
                <ResourceDictionary x:Key="HighContrast">
                    <StaticResource x:Key="InkColor" ResourceKey="SystemColorWindowTextColor"/>
                    <SolidColorBrush x:Key="InkBrush" Color="{ThemeResource SystemColorWindowTextColor}"/>
                </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            """);

        NamedColoursInAShift(palette).Should().Equal(["Light InkBrush"],
            "a brush in a shift that names its colour is caught, and one that writes it out, or High Contrast's " +
            "system colours, are not");
    }

    [Fact]
    public void ABrushInAShiftWritesItsColourOut()
    {
        var offenders = RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml")
            .SelectMany(file => NamedColoursInAShift(XDocument.Load(file))
                .Select(entry => $"{RepoSource.Relative(file)}: {entry}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a brush that names its colour from the same theme comes out in Night Ops' colour on Day Shift when the " +
            "shift is switched while the app runs");
    }

    [Fact]
    public void TheResolutionRuleAcceptsWhatIsMergedAheadAndCatchesWhatComesAfter()
    {
        var faces = Dictionary("""<FontFamily x:Key="MonoFace">ms-appx:///Assets/Fonts/Mono.ttf#Mono</FontFamily>""");
        var styles = Dictionary("""
            <Style x:Key="LabelStyle" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="{StaticResource MonoFace}"/>
                <Setter Property="Foreground" Value="{ThemeResource InkBrush}"/>
            </Style>
            """);
        var palette = Dictionary("""<SolidColorBrush x:Key="InkBrush" Color="Black"/>""");

        Unresolved([("Faces.xaml", faces), ("Styles.xaml", styles), ("Palette.xaml", palette)], _ => true,
                (_, _) => null)
            .Should().Equal(["Styles.xaml InkBrush"],
                "a key merged ahead of a dictionary is there when it is read, and one merged after it is not");
    }

    [Fact]
    public void EveryConsoleDictionaryResolvesFromWhatAppXamlMergesAheadOfIt()
    {
        var merges = AppMerges(AppPath);
        merges.Count(merge => IsConsole(merge.Path)).Should().BeGreaterThan(4,
            "this test is worthless if it cannot find the console's dictionaries in App.xaml");

        Unresolved(merges, IsConsole, ProjectDictionary.Open).Should().BeEmpty(
            "App.xaml merges the console's dictionaries in the order they lean on each other, and a key that only " +
            "arrives after a dictionary is not there when that dictionary is read");
    }

    /// <summary>
    /// "Theme Key" for each entry of a Night, Day or Dark theme dictionary that names another resource rather than
    /// writing its value out. High Contrast's are left alone: its colours are the system's, which it has to name.
    /// </summary>
    private static IEnumerable<string> NamedColoursInAShift(XDocument document)
    {
        var shifts = document.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .SelectMany(themes => themes.Elements(Presentation + "ResourceDictionary"))
            .Where(theme => theme.Attribute(Xaml + "Key")?.Value is "Default" or "Light" or "Dark");

        foreach (var theme in shifts)
        {
            foreach (var entry in theme.Elements().Where(entry => entry.Attribute(Xaml + "Key") is not null))
            {
                var named = entry.Name.LocalName is "StaticResource" or "ThemeResource" ||
                            entry.DescendantsAndSelf().Attributes()
                                .Any(attribute => attribute.Value.Contains("Resource ", StringComparison.Ordinal));
                if (named)
                    yield return $"{theme.Attribute(Xaml + "Key")!.Value} {entry.Attribute(Xaml + "Key")!.Value}";
            }
        }
    }

    /// <summary>Each merge of a file that has theme dictionaries, by anything but App.xaml.</summary>
    private static IEnumerable<(string File, string Target)> ThemeDictionariesMergedBelowApp(
        IReadOnlyDictionary<string, XDocument> files,
        string app, Func<string, string, (string Path, XDocument Document)?> open)
    {
        foreach (var (file, document) in files)
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(app), StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var source in document.Descendants(Presentation + "ResourceDictionary")
                         .Select(dictionary => dictionary.Attribute("Source")?.Value).OfType<string>())
            {
                if (open(file, source) is { } merged && merged.Document.Descendants()
                        .Any(element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries"))
                {
                    yield return (file, merged.Path);
                }
            }
        }
    }

    /// <summary>The files App.xaml merges, in order, as the app reads them.</summary>
    private static IReadOnlyList<(string Path, XDocument Document)> AppMerges(string app) =>
        XDocument.Load(app, LoadOptions.SetLineInfo).Descendants(Presentation + "ResourceDictionary.MergedDictionaries")
            .First().Elements(Presentation + "ResourceDictionary")
            .Select(dictionary => dictionary.Attribute("Source")?.Value).OfType<string>()
            .Select(source => ProjectDictionary.Open(app, source) ?? throw new FileNotFoundException(source))
            .Select(merge => (merge.Path, XDocument.Load(merge.Path, LoadOptions.SetLineInfo)))
            .ToList();

    /// <summary>
    /// "File:line Key" for each reference in a judged dictionary that neither it, its own merges, WinUI, nor a
    /// dictionary merged ahead of it defines.
    /// </summary>
    private static IEnumerable<string> Unresolved(IEnumerable<(string Path, XDocument Document)> merges,
        Func<string, bool> judged, Func<string, string, (string Path, XDocument Document)?> open)
    {
        var ahead = new HashSet<string>(WinUIResources.Keys, StringComparer.Ordinal);
        foreach (var (path, document) in merges)
        {
            if (judged(path))
            {
                foreach (var reference in ResourceKeyRule.Unresolved(document, path, ahead, open))
                    yield return $"{Name(path)}{(reference.Line > 0 ? $":{reference.Line}" : "")} {reference.Key}";
            }

            if (document.Root is { } root)
                ResourceKeyRule.AddKeys(root, path, ahead, new HashSet<string>(StringComparer.OrdinalIgnoreCase), open);
        }

        static string Name(string path) => Path.IsPathRooted(path) ? RepoSource.Relative(path) : path;
    }

    private static bool IsConsole(string path) =>
        RepoSource.Relative(path).StartsWith(ConsoleFolder + "/", StringComparison.Ordinal);

    private static XDocument Dictionary(string content) => XDocument.Parse($"""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
        {content}
        </ResourceDictionary>
        """);
}
