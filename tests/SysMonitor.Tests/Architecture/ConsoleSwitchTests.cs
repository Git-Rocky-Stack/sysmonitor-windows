using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A switch on the console is a bat lever: a recessed track cut into the chassis with a machined cap for a thumb,
/// armed red when it is on (System-X styles.css .switch, :2323, at the pinned system-x-app@eaba14b). WinUI draws
/// a pill with a round knob, so the shape is re-templated here rather than recoloured - FluentOverrides.xaml can
/// only reach the colours.
/// <para>
/// A stock control's template is also a contract with the control's own code. ToggleSwitch measures
/// SwitchKnobBounds and SwitchKnob to work out how far the knob travels, sets KnobTranslateTransform while a
/// finger drags it, and puts the focus rectangle on SwitchAreaGrid. A template missing one of those parts still
/// compiles, still draws, and loses dragging with nothing to say so - which is why the parts are a test and not a
/// comment.
/// </para>
/// <para>
/// That the switch then draws in those colours on a real switch, off and on, in both shifts, is measured where it
/// can only be measured: the UI smoke run (UiSmokeRun.CheckSwitchAsync).
/// </para>
/// </summary>
public class ConsoleSwitchTests
{
    private const string Controls = "src/SysMonitor.App/Styles/Console/Controls.xaml";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The parts ToggleSwitch's own code looks up by name. Renaming one is how a re-template quietly breaks the
    /// control rather than the build.
    /// </summary>
    private static readonly string[] PartsWinUiDrives =
    [
        "HeaderContentPresenter", "SwitchAreaGrid", "OffContentPresenter", "OnContentPresenter",
        "OuterBorder", "SwitchKnobBounds", "SwitchKnob", "SwitchKnobOn", "SwitchKnobOff",
        "KnobTranslateTransform", "SwitchThumb",
    ];

    /// <summary>The bat lever's measurements: the track, the area the thumb travels in, and the thumb.</summary>
    private static readonly (string Part, string Property, string Value)[] Measurements =
    [
        ("OuterBorder", "Width", "42"),          // width: 42px
        ("OuterBorder", "Height", "22"),         // height: 22px
        ("SwitchKnobBounds", "Width", "40"),     // the track inside its 1px border
        ("SwitchKnobBounds", "Height", "20"),
        ("SwitchKnob", "Width", "20"),           // 16 of thumb and the 2 either side of it
        ("SwitchKnob", "Height", "20"),
        ("SwitchKnobOff", "Width", "16"),        // ::after, width: 16px
        ("SwitchKnobOff", "Height", "16"),
        ("SwitchKnobOn", "Width", "16"),
        ("SwitchKnobOn", "Height", "16"),
    ];

    /// <summary>Each surface of the switch and the one palette token it is drawn from.</summary>
    private static readonly (string Part, string Property, string Token)[] Surfaces =
    [
        ("OuterBorder", "Background", "SwitchTrackBrush"),           // the recess, dark in both shifts
        ("OuterBorder", "BorderBrush", "SwitchEdgeBrush"),           // border: 1px solid rgba(0, 0, 0, .8)
        ("SwitchKnobBounds", "Background", "SwitchTrackArmedBrush"), // [data-on] background
        ("SwitchKnobOff", "Background", "SwitchThumbBrush"),         // ::after background
        ("SwitchKnobOn", "Background", "SwitchThumbArmedBrush"),     // [data-on]::after background
    ];

    /// <summary>Every token the switch adds to the palette, which has to be written for all three shifts.</summary>
    private static readonly string[] SwitchTokens =
    [
        "SwitchTrackBrush", "SwitchTrackArmedBrush", "SwitchThumbBrush", "SwitchThumbArmedBrush",
        "SwitchShadeTopBrush", "SwitchEdgeBrush", "SwitchThumbLipBrush", "SwitchThumbArmedLipBrush",
    ];

    public static TheoryData<string, string, string> Metrics
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (part, property, value) in Measurements)
                data.Add(part, property, value);
            return data;
        }
    }

    public static TheoryData<string, string, string> Faces
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (part, property, token) in Surfaces)
                data.Add(part, property, token);
            return data;
        }
    }

    /// <summary>
    /// One style, no key. A keyed style is never applied by itself, so the eleven switches on the four pages that
    /// have one would keep WinUI's pill and nothing in the dictionary would say so.
    /// </summary>
    [Fact]
    public void TheSwitchIsTheOneStyleEveryToggleSwitchTakes()
    {
        var styles = SwitchStyles();

        styles.Should().HaveCount(1, "two styles for one control leaves one of them dead");
        styles[0].Attribute(Xaml + "Key").Should().BeNull(
            "a switch that asks for no style still has to be a bat lever, and only an implicit style does that");
    }

    [Theory]
    [MemberData(nameof(Metrics))]
    public void TheTrackAndThumbAreCutToTheBatLeversMeasurements(string part, string property, string value)
    {
        Named(part).Attribute(property)?.Value.Should().Be(value,
            $"{part}'s {property} is transcribed from .switch; a lever measured differently is a different lever");
    }

    /// <summary>
    /// The thumb travels 20: <c>translateX(20px)</c>. ToggleSwitch works the same number out for itself, from
    /// SwitchKnobBounds' width less SwitchKnob's, and uses it to slide the knob and to clamp a drag. If the two
    /// disagree the knob lands in one place when it is clicked and another when it is dragged, which no static
    /// reading of either alone would catch.
    /// </summary>
    [Fact]
    public void TheKnobTravelsTheDistanceWinUIWorksOutForItself()
    {
        var bounds = Width("SwitchKnobBounds");
        var knob = Width("SwitchKnob");
        var travel = OnStateKnobTravel();

        travel.Should().Be(20, "translateX(20px) is what .switch[data-on] moves its thumb");
        (bounds - knob).Should().Be(travel,
            $"WinUI slides the knob by SwitchKnobBounds ({bounds}) less SwitchKnob ({knob}), so a click and a " +
            "drag only agree when that difference is the travel the On state sets");
    }

    [Theory]
    [MemberData(nameof(Faces))]
    public void EverySurfaceOnTheSwitchComesFromThePalette(string part, string property, string token)
    {
        Named(part).Attribute(property)?.Value.Should().Be($"{{ThemeResource {token}}}",
            $"{part} is drawn from {token}; a colour of its own is a colour that ignores the shift");
    }

    [Fact]
    public void TheTemplateKeepsEveryPartWinUIDrivesFromCode()
    {
        var present = Template().Descendants()
            .Select(element => element.Attribute(Xaml + "Name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        PartsWinUiDrives.Where(part => !present.Contains(part)).Should().BeEmpty(
            "ToggleSwitch looks these up by name; one missing still builds and still draws, and loses the " +
            "behaviour it drove with nothing to say so");
    }

    /// <summary>
    /// A token the palette does not write throws when Controls.xaml loads, which is at startup on the first page.
    /// High Contrast is the shift most easily forgotten, since nothing on a developer's machine shows it.
    /// </summary>
    [Fact]
    public void EverySwitchTokenIsInThePaletteInEveryShift()
    {
        var themes = XDocument.Load(PathOf(Tokens))
            .Descendants(Presentation + "ResourceDictionary.ThemeDictionaries").Single()
            .Elements()
            .ToDictionary(
                theme => theme.Attribute(Xaml + "Key")!.Value,
                theme => theme.Elements().Select(brush => brush.Attribute(Xaml + "Key")?.Value).OfType<string>()
                    .ToHashSet(StringComparer.Ordinal));

        themes.Keys.Should().BeEquivalentTo(["Default", "Light", "HighContrast"],
            "the palette is written for three shifts, and the switch has to be drawn in each");

        foreach (var (shift, written) in themes)
        {
            SwitchTokens.Where(token => !written.Contains(token)).Should().BeEmpty(
                $"a token the {shift} palette does not write throws when the dictionary loads, at startup");
        }
    }

    private static List<XElement> SwitchStyles() =>
        XDocument.Load(PathOf(Controls)).Descendants(Presentation + "Style")
            .Where(style => style.Attribute("TargetType")?.Value == "ToggleSwitch")
            .ToList();

    private static XElement Template()
    {
        var styles = SwitchStyles();
        styles.Should().NotBeEmpty("there is no switch style to read, so every test here would be vacuous");
        // The switch's own template, not the transparent one its drag Thumb carries.
        return styles[0].Descendants(Presentation + "ControlTemplate")
            .Single(template => template.Attribute("TargetType")?.Value == "ToggleSwitch");
    }

    /// <summary>The one element in the template with this name.</summary>
    private static XElement Named(string name)
    {
        var found = Template().Descendants()
            .Where(element => element.Attribute(Xaml + "Name")?.Value == name)
            .ToList();

        found.Should().HaveCount(1, $"the template has no single element named {name}");
        return found[0];
    }

    private static double Width(string part) =>
        double.Parse(Named(part).Attribute("Width")!.Value, CultureInfo.InvariantCulture);

    /// <summary>How far the On state moves KnobTranslateTransform, however the animation spells it.</summary>
    private static double OnStateKnobTravel()
    {
        var on = Template().Descendants(Presentation + "VisualState")
            .Single(state => state.Attribute(Xaml + "Name")?.Value == "On");

        var moves = on.Descendants()
            .Where(animation =>
                animation.Attribute(Presentation + "Storyboard.TargetName")?.Value == "KnobTranslateTransform" ||
                animation.Attribute("Storyboard.TargetName")?.Value == "KnobTranslateTransform")
            .SelectMany(animation => animation.DescendantsAndSelf())
            .Select(node => node.Attribute("To")?.Value ?? node.Attribute("Value")?.Value)
            .OfType<string>()
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToList();

        moves.Should().NotBeEmpty("the On state has to move the knob, or the thumb never leaves the left");
        return moves[^1];
    }

    private static string PathOf(string relative) =>
        Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
