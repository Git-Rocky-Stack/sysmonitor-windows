using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// What a locked control looks like, and the fact that every locked control looks the same.
/// <para>
/// System-X writes one rule for it and points the switch at the buttons' copy of it: <c>opacity: .42</c> with
/// <c>filter: saturate(.5)</c> on <c>.btn</c>, <c>.btn-armed</c>, <c>.btn-chrome</c> and <c>.btn-ghost</c>
/// (:2016-2023), and the same again on <c>.switch:disabled</c> under a comment saying it is the same treatment
/// "so a locked control reads the same way everywhere on the console rather than inventing a second idiom"
/// (:2367-2369).
/// </para>
/// <para>
/// The dimming is a resource rather than a number because the shift decides it. Dropping a control to 42% of
/// whatever it is standing on is what High Contrast must not do - the user picked those colours for contrast,
/// and 42% of them is not the contrast they picked - so the palette writes the opacity per shift and High
/// Contrast writes 1, where the greying is carried by the system's own GrayText instead.
/// </para>
/// <para>
/// This is the test that makes "everywhere" true. A second idiom is easy to add by accident: a Disabled state is
/// written per template, each one looks reasonable on its own, and nothing compares them.
/// </para>
/// </summary>
public class ConsoleLockedStateTests
{
    private const string Controls = "src/SysMonitor.App/Styles/Console/Controls.xaml";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";
    private const string Opacity = "{ThemeResource ConsoleDisabledOpacity}";
    private const string Foreground = "{ThemeResource ConsoleDisabledForegroundBrush}";
    private const string FieldForeground = "{ThemeResource ConsoleDisabledFieldForegroundBrush}";

    /// <summary>Where a field draws its own words: the one part allowed the field's twin of the locked word.</summary>
    private const string FieldText = "ContentElement.Foreground";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EveryLockedControlOnTheConsoleDimsByTheOneSharedAmount()
    {
        var undimmed = DisabledStates()
            .Where(state => !Setters(state).Any(setter =>
                setter.Value == Opacity &&
                setter.Target.EndsWith(".Opacity", StringComparison.Ordinal)))
            .Select(state => Owner(state))
            .ToList();

        undimmed.Should().BeEmpty(
            $"a locked control dims to {Opacity}; one that dims by some other amount, or not at all, is the " +
            "second idiom the stylesheet's own comment exists to prevent");
    }

    /// <summary>
    /// The word on a locked control is the one thing High Contrast still has to change, because it is not
    /// dimming there. A template that names Graphite directly gets Night Ops' grey in a shift the user set to
    /// something else.
    /// <para>
    /// One place takes a twin of it: the words typed into a field. A field is a dark well in both shifts
    /// (styles.css :2285), and the shared locked word is the chassis' - dark on Day Shift, where it would vanish
    /// into the well. The twin keeps the field's own white in the working shifts and is GrayText in High Contrast,
    /// so it is the same idiom on a different ground, and it is allowed only on the part a field draws its text
    /// in. A label above the field is on the chassis, and takes the shared word like everything else.
    /// </para>
    /// </summary>
    [Fact]
    public void ALockedControlTakesItsWordFromTheSharedLockedForeground()
    {
        var wrong = DisabledStates()
            .SelectMany(state => Setters(state).Select(setter => (Owner: Owner(state), setter.Target, setter.Value)))
            .Where(setter => setter.Target.EndsWith(".Foreground", StringComparison.Ordinal))
            .Where(setter => setter.Value != Foreground && !(setter.Target == FieldText && setter.Value == FieldForeground))
            .Select(setter => $"{setter.Owner} sets {setter.Target} to {setter.Value}")
            .ToList();

        wrong.Should().BeEmpty(
            $"a locked word is {Foreground}, which is the shift's own grey - and in High Contrast the user's, " +
            $"which is the only greying that shift gets; only a field's own text ({FieldText}) takes {FieldForeground}");
    }

    /// <summary>
    /// The two resources exist in all three shifts, and High Contrast does not dim. A resource missing from a
    /// shift throws when the dictionary loads, at startup, on the first page.
    /// </summary>
    [Fact]
    public void ThePaletteWritesTheLockedTreatmentForEveryShiftAndHighContrastDoesNotDim()
    {
        var themes = XDocument.Load(PathOf(Tokens))
            .Descendants(Presentation + "ResourceDictionary.ThemeDictionaries").Single()
            .Elements()
            .ToDictionary(theme => theme.Attribute(Xaml + "Key")!.Value, theme => theme);

        foreach (var (shift, theme) in themes)
        {
            var written = theme.Elements()
                .Select(resource => resource.Attribute(Xaml + "Key")?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            written.Should().Contain("ConsoleDisabledOpacity", $"the {shift} shift has to say how far a locked control dims");
            written.Should().Contain("ConsoleDisabledForegroundBrush", $"the {shift} shift has to say what a locked word is");
            written.Should().Contain("ConsoleDisabledFieldForegroundBrush", $"the {shift} shift has to say what a locked field's words are");
        }

        foreach (var key in new[] { "ConsoleDisabledForegroundBrush", "ConsoleDisabledFieldForegroundBrush" })
        {
            BrushColour(themes["HighContrast"], key).Should().Be("{ThemeResource SystemColorGrayTextColor}",
                $"{key} is the only greying High Contrast gets, so it is the user's own GrayText");
        }

        Dimming(themes["HighContrast"]).Should().Be("1",
            "High Contrast does not dim: the user chose those colours for contrast, and 42% of them is not it");

        Dimming(themes["Default"]).Should().Be("0.42", "opacity: .42 is what the stylesheet dims a locked control to");
        Dimming(themes["Light"]).Should().Be("0.42");
    }

    private static string? BrushColour(XElement theme, string key) =>
        theme.Elements().FirstOrDefault(r => r.Attribute(Xaml + "Key")?.Value == key)?.Attribute("Color")?.Value;

    private static string? Dimming(XElement theme) =>
        theme.Elements().FirstOrDefault(r => r.Attribute(Xaml + "Key")?.Value == "ConsoleDisabledOpacity")?.Value.Trim();

    /// <summary>Every Disabled visual state in the console's control templates.</summary>
    private static List<XElement> DisabledStates() =>
        XDocument.Load(PathOf(Controls)).Descendants(Presentation + "VisualState")
            .Where(state => state.Attribute(Xaml + "Name")?.Value == "Disabled")
            .ToList();

    private static IEnumerable<(string Target, string Value)> Setters(XElement state) =>
        state.Descendants(Presentation + "Setter")
            .Select(setter => (setter.Attribute("Target")?.Value ?? string.Empty,
                               setter.Attribute("Value")?.Value ?? string.Empty));

    /// <summary>Which style a disabled state belongs to, for a failure a reader can act on.</summary>
    private static string Owner(XElement state) =>
        state.Ancestors(Presentation + "Style")
            .Select(style => style.Attribute(Xaml + "Key")?.Value ?? style.Attribute("TargetType")?.Value)
            .FirstOrDefault(name => name is not null) ?? "an unnamed style";

    private static string PathOf(string relative) =>
        Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
