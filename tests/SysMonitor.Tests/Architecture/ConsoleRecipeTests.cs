using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The Command Console restyle replaces the first-generation look page by page: colours become theme tokens, the
/// hand-copied chrome bezels around buttons become caps, spinners become busy panels, literal font names become
/// the console faces, and every dialog comes from one helper that gives it the right style, theme and window.
/// <para>
/// Each rule below names the files that still break it. The lists are a ratchet: a file leaves its list in the
/// commit that cleans it, and the test fails both ways - a file that breaks a rule without being on its list, and
/// a file on a list that no longer breaks it. So the old look can only shrink, and the lists say exactly how much
/// of it is left.
/// </para>
/// </summary>
public class ConsoleRecipeTests
{
    private const string AppFolder = "src/SysMonitor.App";
    private const string CoreFolder = "src/SysMonitor.Core";

    /// <summary>A colour written into XAML, as an attribute (<c>Color="#AA2024"</c>) or as an element's content.</summary>
    private static readonly Regex XamlColour = new(@"=\s*""#[0-9A-Fa-f]{3,8}""|>\s*#[0-9A-Fa-f]{3,8}\s*<", RegexOptions.Compiled);

    /// <summary>Where colours are meant to be written, and why. The XAML colour rule passes over these files.</summary>
    private static readonly Dictionary<string, string> Palettes = new(StringComparer.Ordinal)
    {
        ["src/SysMonitor.App/Styles/Console/Tokens.xaml"] = "the console palette: every theme's colours, written once",
        ["src/SysMonitor.App/Styles/Console/FluentOverrides.xaml"] =
            "WinUI's own resources restated in the palette's colours, each checked to be one of them (FluentOverrideTests)",
    };
    private static readonly Regex CodeColour = new(@"""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);
    private static readonly Regex FontFamilyLiteral = new(@"FontFamily=""(?!\{)[^""]*""", RegexOptions.Compiled);

    [Fact]
    public void ColourLiteralsInXamlOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml().Where(file => !Palettes.ContainsKey(RepoSource.Relative(file))), source => XamlColour.IsMatch(source),
            XamlColourLiterals, "a colour written into XAML ignores the theme; it belongs in the token dictionaries");

    [Fact]
    public void EveryPaletteStillHoldsColours()
    {
        foreach (var palette in Palettes.Keys)
        {
            var path = Path.Combine(RepoSource.Root, palette);
            File.Exists(path).Should().BeTrue($"{palette} is where colours are written; if it moves, its exemption moves with it");
            XamlColour.IsMatch(File.ReadAllText(path)).Should().BeTrue(
                $"{palette} holds no colours, so there is nothing left to pass over");
        }
    }

    [Fact]
    public void ColourStringsInCodeOnlyRemainWhereTheyAreListed() =>
        Ratchet(Code(AppFolder).Concat(Code(CoreFolder)), source => CodeColour.IsMatch(source), CodeColourLiterals,
            "a status carries a word and a lamp state, and the palette lives in one place, not in hex strings");

    [Fact]
    public void ProgressRingsOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => source.Contains("<ProgressRing", StringComparison.Ordinal), ProgressRings,
            "nothing on the console spins: work in progress is a busy panel or a travelling segment");

    [Fact]
    public void ChromeBezelsOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => source.Contains("GradientStop Color=\"#707070\"", StringComparison.Ordinal), ChromeBezels,
            "a button is a cap styled once, not a gradient border copied round it");

    [Fact]
    public void FontNamesOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => FontFamilyLiteral.IsMatch(source), FontFamilyLiterals,
            "a face is a named font resource, so a missing file is one fix and not forty");

    [Fact]
    public void DialogsBuiltByHandOnlyRemainWhereTheyAreListed() =>
        Ratchet(Code(AppFolder).Where(file => !file.EndsWith("ConsoleDialog.cs", StringComparison.Ordinal)),
            source => source.Contains("new ContentDialog", StringComparison.Ordinal), HandBuiltDialogs,
            "a dialog built by hand misses the dialog style and the theme; ConsoleDialog gives it both");

    private static void Ratchet(IEnumerable<string> files, Func<string, bool> breaksRule, string[] listed, string because)
    {
        var breaking = files.Where(file => breaksRule(File.ReadAllText(file))).Select(RepoSource.Relative)
            .ToHashSet(StringComparer.Ordinal);

        breaking.Except(listed).Should().BeEmpty($"these break the rule and are not listed: {because}");
        listed.Except(breaking).Should().BeEmpty("these no longer break the rule; take them off the list, which only shrinks");
    }

    private static IEnumerable<string> Xaml() => RepoSource.FilesUnder(AppFolder, "*.xaml");

    private static IEnumerable<string> Code(string folder) => RepoSource.FilesUnder(folder);

    // ---------------------------------------------------------------- what is left

    private static readonly string[] XamlColourLiterals =
    [
        "src/SysMonitor.App/Views/DonationPage.xaml",
        "src/SysMonitor.App/Views/FpsOverlayWindow.xaml",
        "src/SysMonitor.App/Views/PdfEditorPage.xaml",
    ];

    private static readonly string[] CodeColourLiterals =
    [
        "src/SysMonitor.App/ViewModels/PdfEditorViewModel.cs",
        "src/SysMonitor.Core/Services/Utilities/IPdfTools.cs",
    ];

    private static readonly string[] ProgressRings = [];

    private static readonly string[] ChromeBezels = [];

    private static readonly string[] FontFamilyLiterals =
    [
        "src/SysMonitor.App/Views/FpsOverlayWindow.xaml",
        "src/SysMonitor.App/Views/GameModePage.xaml",
        "src/SysMonitor.App/Views/NetworkPage.xaml",
        "src/SysMonitor.App/Views/PdfToolsPage.xaml",
        "src/SysMonitor.App/Views/SettingsPage.xaml",
        "src/SysMonitor.App/Views/SystemInfoPage.xaml",
        "src/SysMonitor.App/Views/UserGuidePage.xaml",
    ];

    private static readonly string[] HandBuiltDialogs = [];
}
