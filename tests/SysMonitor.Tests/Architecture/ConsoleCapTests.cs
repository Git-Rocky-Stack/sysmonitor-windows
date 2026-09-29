using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A button on the console is a cap: a machined key with the palette's face, its key light across the top and a
/// machined radius (System-X styles.css :2062). Two carry more weight - the armed cap for a consequential command
/// (:2093) and the chrome cap for a page's single call to action (:2116) - and they differ from the plain cap only
/// in the face they are given.
/// <para>
/// The palette has written all three faces since the tokens were generated, and until the styles below existed
/// nothing read them: a brush the generator writes and no style names is a colour that cannot appear, and neither
/// the build nor the smoke run says so. So each rule here ties a style to the token it must draw from, which is
/// also what stops a cap being given a colour of its own (ConsoleRecipeTests forbids the literal; these say which
/// token replaces it).
/// </para>
/// <para>
/// That the cap then reaches a real button, in both shifts, is measured where it can only be measured - on WinUI,
/// in the UI smoke run, against the palette (UiSmokeRun.CheckCaps).
/// </para>
/// </summary>
public class ConsoleCapTests
{
    private const string Controls = "src/SysMonitor.App/Styles/Console/Controls.xaml";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";
    private const string App = "src/SysMonitor.App/App.xaml";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The cap every button takes, which the variants are cut from.</summary>
    private const string PlainCap = "ConsoleCapButtonStyle";

    /// <summary>Each cap style and the one face it draws from.</summary>
    private static readonly (string Key, string Face)[] CapFaces =
    [
        (PlainCap, "CapFaceBrush"),
        ("ArmedCapButtonStyle", "ArmedCapFaceBrush"),
        ("ChromeCapButtonStyle", "ChromeCapFaceBrush"),
    ];

    public static TheoryData<string, string> Caps
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (key, face) in CapFaces)
                data.Add(key, face);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Caps))]
    public void EachCapDrawsItsFaceFromThePalette(string key, string face)
    {
        var style = ButtonStyles().Where(candidate => candidate.Attribute(Xaml + "Key")?.Value == key).ToList();

        style.Should().HaveCount(1, $"{key} is how a cap is asked for, and two would leave one of them dead");
        Setter(style[0], "Background").Should().Be($"{{ThemeResource {face}}}",
            $"the palette writes {face} for every shift; a cap given anything else is a face that ignores the shift");
    }

    /// <summary>
    /// One style in the dictionary has no key, and it is the only reason a button nobody styled is a cap at all:
    /// a keyed style is never applied by itself. It carries nothing of its own but the cap it is based on, so
    /// there is one place the cap is defined and one place it is handed out.
    /// </summary>
    [Fact]
    public void ABareButtonTakesThePlainCap()
    {
        var bare = ButtonStyles().Where(style => style.Attribute(Xaml + "Key") is null).ToList();

        bare.Should().HaveCount(1,
            $"a button with no style of its own takes the implicit one; without it every bare button on the " +
            $"36 pages keeps WinUI's look, and nothing in {Controls} would say so");
        bare[0].Attribute("BasedOn")?.Value.Should().Be($"{{StaticResource {PlainCap}}}",
            "the implicit style hands out the cap; anything else and the two could drift apart");
    }

    /// <summary>
    /// A cap that carries more weight - armed, chrome - is the plain cap with another face, so it inherits the
    /// template, the metrics and every state. Copying the template instead is the drift that made the hand-copied
    /// chrome bezels a ratchet in the first place (ConsoleRecipeTests.ChromeBezels).
    /// </summary>
    [Fact]
    public void EveryCapVariantIsCutFromThePlainOne()
    {
        var variants = ButtonStyles()
            .Where(style => style.Attribute(Xaml + "Key") is { Value: not PlainCap })
            .ToList();

        variants.Should().NotBeEmpty("this test is worthless if it cannot find the caps it judges");
        foreach (var style in variants)
        {
            style.Attribute("BasedOn")?.Value.Should().Be($"{{StaticResource {PlainCap}}}",
                $"{style.Attribute(Xaml + "Key")?.Value} restates a face, not a template");
            style.Descendants(Presentation + "ControlTemplate").Should().BeEmpty(
                $"{style.Attribute(Xaml + "Key")?.Value} carries a template of its own, which is the plain cap's " +
                "states and metrics copied - and the copy is what stops following it");
        }
    }

    /// <summary>
    /// Every face a cap names has to be in the palette, in every shift it has. A style naming a brush the palette
    /// does not write throws when the dictionary loads - at startup, on the first page - and High Contrast is the
    /// shift most easily forgotten, because nothing on a developer's machine shows it.
    /// </summary>
    [Fact]
    public void EveryCapFaceIsInThePaletteInEveryShift()
    {
        var themes = XDocument.Load(PathOf(Tokens))
            .Descendants(Presentation + "ResourceDictionary.ThemeDictionaries").Single()
            .Elements()
            .ToDictionary(
                theme => theme.Attribute(Xaml + "Key")!.Value,
                theme => theme.Elements().Select(brush => brush.Attribute(Xaml + "Key")?.Value).OfType<string>().ToHashSet(StringComparer.Ordinal));

        themes.Keys.Should().BeEquivalentTo(["Default", "Light", "HighContrast"],
            "the palette is written for three shifts; a cap has to have a face in each");

        var faces = CapFaces.Select(cap => cap.Face).ToList();
        foreach (var (shift, written) in themes)
        {
            faces.Where(face => !written.Contains(face)).Should().BeEmpty(
                $"a face the {shift} palette does not write throws when Controls.xaml loads, which is at startup");
        }
    }

    /// <summary>
    /// A dictionary that is not merged is a file the app never reads. Controls.xaml has to come after the palette
    /// it draws from, and before Styles.xaml, whose named button styles are the first-generation look the caps
    /// replace: a later dictionary wins, and an implicit style there would take the bare buttons back.
    /// </summary>
    [Fact]
    public void TheAppMergesTheControlsAfterThePaletteAndBeforeTheOldStyles()
    {
        var merged = XDocument.Load(PathOf(App))
            .Descendants(Presentation + "ResourceDictionary")
            .Select(dictionary => dictionary.Attribute("Source")?.Value)
            .OfType<string>()
            .ToList();

        merged.Should().Contain("ms-appx:///Styles/Console/Controls.xaml",
            "a dictionary nobody merges is a file the app never reads, and the caps would simply not appear");

        var controls = merged.IndexOf("ms-appx:///Styles/Console/Controls.xaml");
        controls.Should().BeGreaterThan(merged.IndexOf("ms-appx:///Styles/Console/Tokens.xaml"),
            "the caps draw from the palette, so the palette is merged first");
        controls.Should().BeLessThan(merged.IndexOf("ms-appx:///Styles/Styles.xaml"),
            "Styles.xaml holds the look the caps replace; merged after them it would win");
    }

    /// <summary>Every <c>Style TargetType="Button"</c> in the console's controls dictionary.</summary>
    private static IEnumerable<XElement> ButtonStyles() =>
        XDocument.Load(PathOf(Controls)).Descendants(Presentation + "Style")
            .Where(style => style.Attribute("TargetType")?.Value == "Button");

    /// <summary>A style's value for a property, written as an attribute or as an element.</summary>
    private static string? Setter(XElement style, string property) =>
        style.Elements(Presentation + "Setter")
            .FirstOrDefault(setter => setter.Attribute("Property")?.Value == property)
            ?.Attribute("Value")?.Value;

    private static string PathOf(string relative) => Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
