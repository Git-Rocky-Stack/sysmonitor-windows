using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// How the console's dictionaries are put together, which decides whether its colours follow the shift.
/// <para>
/// WinUI chooses the theme dictionary for an element's theme - a well's Dark, or a shift switched while the app
/// runs - only in App.xaml's own theme dictionaries and in those of the files App.xaml merges itself. The UI smoke
/// run measured it on Day Shift: a colour from App.xaml's own theme dictionaries, from a file App.xaml merges, and
/// from an element's own came out light, while the palette, whose file was merged into the text styles, which were
/// merged into the instruments, which App.xaml merged, stayed Night Ops however a colour was named - by a style, on
/// an element, or in a template. So a file that has theme dictionaries is merged by App.xaml and by nothing else,
/// and the console's other dictionaries find the palette, the faces and each other among the files App.xaml merges
/// ahead of them.
/// </para>
/// </summary>
public class ConsoleDictionaryTests
{
    private const string AppXaml = "src/SysMonitor.App/App.xaml";
    private const string ConsoleFolder = "src/SysMonitor.App/Styles/Console";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void TheMergeRuleCatchesAPaletteMergedBelowAppXaml()
    {
        var palette = Dictionary("""<ResourceDictionary.ThemeDictionaries><ResourceDictionary x:Key="Default"/></ResourceDictionary.ThemeDictionaries>""");
        var styles = Dictionary("""<ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Palette.xaml"/></ResourceDictionary.MergedDictionaries>""");
        var app = Dictionary("""<ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Palette.xaml"/></ResourceDictionary.MergedDictionaries>""");
        var files = new Dictionary<string, XDocument> { ["App.xaml"] = app, ["Styles.xaml"] = styles, ["Palette.xaml"] = palette };
        (string, XDocument)? Open(string declaring, string source) => files.TryGetValue(source, out var document) ? (source, document) : null;

        ThemeDictionariesMergedBelowApp(files, "App.xaml", Open).Select(merge => $"{merge.File} merges {merge.Target}")
            .Should().Equal(["Styles.xaml merges Palette.xaml"],
            "a palette App.xaml merges itself follows an element's theme, and the same palette merged by another file does not");
    }

    [Fact]
    public void AFileWithThemeDictionariesIsMergedByAppXamlAlone()
    {
        var files = RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml")
            .ToDictionary(file => file, file => XDocument.Load(file), StringComparer.OrdinalIgnoreCase);
        var app = Path.Combine(RepoSource.Root, AppXaml);

        ThemeDictionariesMergedBelowApp(files, app, ProjectDictionary.Open)
            .Select(merge => $"{RepoSource.Relative(merge.File)} merges {RepoSource.Relative(merge.Target)}")
            .Should().BeEmpty("a theme dictionary merged below App.xaml keeps Night Ops colours on Day Shift");

        AppMerges(app).Select(merge => RepoSource.Relative(merge.Path)).Should().Contain(
            [$"{ConsoleFolder}/Tokens.xaml", $"{ConsoleFolder}/FluentOverrides.xaml"],
            "the palette and the Fluent overrides are the dictionaries whose themes have to follow an element's");
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
        var merges = AppMerges(Path.Combine(RepoSource.Root, AppXaml));
        merges.Count(merge => IsConsole(merge.Path)).Should().BeGreaterThan(4,
            "this test is worthless if it cannot find the console's dictionaries in App.xaml");

        Unresolved(merges, IsConsole, ProjectDictionary.Open).Should().BeEmpty(
            "App.xaml merges the console's dictionaries in the order they lean on each other, and a key that only " +
            "arrives after a dictionary is not there when that dictionary is read");
    }

    /// <summary>Each merge of a file that has theme dictionaries, by anything but App.xaml.</summary>
    private static IEnumerable<(string File, string Target)> ThemeDictionariesMergedBelowApp(
        IReadOnlyDictionary<string, XDocument> files,
        string app, Func<string, string, (string Path, XDocument Document)?> open)
    {
        foreach (var (file, document) in files)
        {
            if (string.Equals(file, app, StringComparison.OrdinalIgnoreCase))
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
