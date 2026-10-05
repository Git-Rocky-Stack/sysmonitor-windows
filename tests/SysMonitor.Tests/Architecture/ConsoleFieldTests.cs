using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A form field on the console is a recessed dark well, dark in both shifts (System-X styles.css .field, :2286,
/// at the pinned system-x-app@eaba14b). Three controls are typed into and take it - TextBox, PasswordBox and
/// NumberBox, whose own text box is pointed at the same style - so a form reads as one idiom.
/// <para>
/// Each is a re-template of WinUI's own, and a template is a contract with the control's code: TextBox draws its
/// text into ContentElement and shows DeleteButton by name, PasswordBox shows RevealButton, and NumberBox drives
/// InputBox, its spin buttons and their popup, and sends InputBox to its spin-button states. A template missing
/// one still builds and still draws, and loses what the part did with nothing to say so - so the parts and the
/// states are tests here, not comments there.
/// </para>
/// <para>
/// That the field then reaches a real control in both shifts, and arms itself when focused, is measured where it
/// can only be measured: the UI smoke run (UiSmokeRun.CheckFieldsAsync).
/// </para>
/// </summary>
public class ConsoleFieldTests
{
    private const string Controls = "src/SysMonitor.App/Styles/Console/Controls.xaml";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";
    private const string Typography = "src/SysMonitor.App/Styles/Console/Typography.xaml";
    private const string Views = "src/SysMonitor.App/Views";

    private const string FieldStyle = "ConsoleFieldTextBoxStyle";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] FieldControls = ["TextBox", "PasswordBox", "NumberBox"];

    /// <summary>The parts each control's own code looks up by name, as WinUI's templates name them.</summary>
    private static readonly Dictionary<string, string[]> PartsWinUiDrives = new(StringComparer.Ordinal)
    {
        ["TextBox"] =
        [
            "HeaderContentPresenter", "BorderElement", "ContentElement", "PlaceholderTextContentPresenter",
            "DeleteButton", "DescriptionPresenter",
            // NumberBox's InputBox is this template, and NumberBox's spin-button states reach these two.
            "PopupIndicator", "SpinButtonsColumn",
        ],
        ["PasswordBox"] =
        [
            "HeaderContentPresenter", "BorderElement", "ContentElement", "PlaceholderTextContentPresenter",
            "RevealButton", "DescriptionPresenter",
        ],
        ["NumberBox"] =
        [
            "HeaderContentPresenter", "InputBox", "UpDownPopup", "PopupUpSpinButton", "PopupDownSpinButton",
            "InputEater", "UpSpinButton", "DownSpinButton", "DescriptionPresenter",
        ],
    };

    /// <summary>The visual states each control's code sends its template to.</summary>
    private static readonly Dictionary<string, string[]> StatesWinUiDrives = new(StringComparer.Ordinal)
    {
        ["TextBox"] =
        [
            "Normal", "PointerOver", "Focused", "Disabled", "ButtonVisible", "ButtonCollapsed",
            "SpinButtonsCollapsed", "SpinButtonsPopup", "SpinButtonsVisible",
        ],
        ["PasswordBox"] = ["Normal", "PointerOver", "Focused", "Disabled", "ButtonVisible", "ButtonCollapsed"],
        ["NumberBox"] =
        [
            "Normal", "Disabled", "SpinButtonsCollapsed", "SpinButtonsVisible", "SpinButtonsPopup",
            "UpSpinButtonEnabled", "UpSpinButtonDisabled", "DownSpinButtonEnabled", "DownSpinButtonDisabled",
        ],
    };

    /// <summary>
    /// The field's measurements, the same on all three (styles.css :2286-2297): 8 above and below the words and 11
    /// either side, the 4px control radius, a 1px edge, Public Sans 13 - and the colours the palette writes for a
    /// well, which the stylesheet says is dark in both shifts.
    /// </summary>
    private static readonly (string Property, string Value)[] Metrics =
    [
        ("Padding", "11,8"),
        ("CornerRadius", "{StaticResource CapCornerRadius}"),
        ("BorderThickness", "1"),
        ("FontFamily", "{StaticResource ConsoleBodyFontFamily}"),
        ("FontSize", "13"),
        ("Foreground", "{ThemeResource FieldForegroundBrush}"),
        ("BorderBrush", "{ThemeResource FieldEdgeBrush}"),
    ];

    /// <summary>Every token the field adds to the palette, which has to be written for all three shifts.</summary>
    private static readonly string[] FieldTokens =
    [
        "FieldFaceBrush", "FieldShadeTopBrush", "FieldEdgeBrush", "FieldLipBrush", "FieldForegroundBrush",
        "FieldPlaceholderBrush", "ConsoleDisabledFieldForegroundBrush",
    ];

    /// <summary>The parts of the well that draw text or a glyph by theme rather than by Foreground.</summary>
    private static readonly (string Control, string Part)[] DarkParts =
    [
        ("TextBox", "ContentElement"), ("TextBox", "DeleteButton"), ("TextBox", "PopupIndicator"),
        ("PasswordBox", "ContentElement"), ("PasswordBox", "RevealButton"),
        ("NumberBox", "UpSpinButton"), ("NumberBox", "DownSpinButton"),
    ];

    public static TheoryData<string> Fields => new(FieldControls);

    public static TheoryData<string, string, string> FieldMetrics
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var control in FieldControls)
                foreach (var (property, value) in Metrics)
                    data.Add(control, property, value);
            return data;
        }
    }

    public static TheoryData<string, string> WellParts
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (control, part) in DarkParts)
                data.Add(control, part);
            return data;
        }
    }

    /// <summary>
    /// One style each that asks for no key. A keyed style is never applied by itself, so a field on a page that
    /// asks for nothing would keep WinUI's look and nothing in the dictionary would say so.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fields))]
    public void EachFieldControlTakesTheFieldWithoutAskingForIt(string control)
    {
        var bare = Styles(control).Where(style => style.Attribute(Xaml + "Key") is null).ToList();

        bare.Should().HaveCount(1, $"a {control} with no style of its own takes the implicit one, and two would leave one dead");

        if (control == "TextBox")
        {
            bare[0].Attribute("BasedOn")?.Value.Should().Be($"{{StaticResource {FieldStyle}}}",
                "the implicit text box style hands out the field; NumberBox's InputBox asks for the same one by name");
            bare[0].Elements().Should().BeEmpty("the field is defined once, in the keyed style, and handed out here");
        }
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void TheTemplateKeepsEveryPartWinUIDrivesFromCode(string control)
    {
        var present = Template(control).Descendants()
            .Select(element => element.Attribute(Xaml + "Name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        PartsWinUiDrives[control].Where(part => !present.Contains(part)).Should().BeEmpty(
            $"{control} looks these up by name; one missing still builds and still draws, and loses what it did");
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void TheTemplateHasEveryStateWinUISendsItTo(string control)
    {
        var states = Template(control).Descendants(Presentation + "VisualState")
            .Select(state => state.Attribute(Xaml + "Name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        StatesWinUiDrives[control].Where(state => !states.Contains(state)).Should().BeEmpty(
            $"{control}'s code goes to these states; one that is missing is a change the field silently never shows");
    }

    [Theory]
    [MemberData(nameof(FieldMetrics))]
    public void EveryFieldIsCutToTheStylesheetsMeasurements(string control, string property, string value)
    {
        Setter(FieldStyleFor(control), property).Should().Be(value,
            $"{property} is transcribed from .field; a {control} measured differently is half of a form in another idiom");
    }

    /// <summary>
    /// The face. TextBox and PasswordBox draw it from their Background; NumberBox draws none of its own, since its
    /// InputBox is the text box field and takes the face from there.
    /// </summary>
    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    public void TheWellIsThePalettesFieldFace(string control)
    {
        Setter(FieldStyleFor(control), "Background").Should().Be("{ThemeResource FieldFaceBrush}",
            "the face is the palette's; a colour of its own would ignore the shift it is shown in");
        Named(control, "Shade").Attribute("Background")?.Value.Should().Be("{ThemeResource FieldShadeTopBrush}",
            "inset 0 2px 4px is drawn as the palette's shade, since WinUI has no inset shadow");
        Named(control, "Lip").Attribute("Background")?.Value.Should().Be("{ThemeResource FieldLipBrush}",
            "inset 0 -1px 0 is the palette's light along the lower lip");
    }

    [Fact]
    public void ThePlaceholderIsTheFieldsOwnGrey()
    {
        Setter(FieldStyleFor("TextBox"), "PlaceholderForeground").Should().Be("{ThemeResource FieldPlaceholderBrush}",
            "::placeholder is #6a6a6a (:2300), which the palette writes as FieldPlaceholderBrush");

        // PasswordBox has no PlaceholderForeground to set, so its template names the grey.
        Named("PasswordBox", "PlaceholderTextContentPresenter").Attribute("Foreground")?.Value
            .Should().Be("{ThemeResource FieldPlaceholderBrush}");
    }

    /// <summary>
    /// <c>.field:focus</c> (:2303): the edge goes to armed edge, and a 2px armed outline stands 1px off it. The
    /// outline is drawn by the template, not left to the system focus rectangle, which shows only for the keyboard
    /// where a browser shows a text field's on any focus.
    /// </summary>
    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    public void FocusedTheFieldArmsItsEdgeAndShowsTheOutline(string control)
    {
        var focused = State(control, "Focused");
        Setters(focused).Should().Contain(("BorderElement.BorderBrush", "{ThemeResource ArmedEdgeBrush}"),
            "border-color: var(--armed-edge)");
        Setters(focused).Should().Contain(("FocusOutline.Opacity", "1"), "the outline shows while the field has focus");

        var outline = Named(control, "FocusOutline");
        outline.Attribute("BorderBrush")?.Value.Should().Be("{ThemeResource ArmedDisplayBrush}", "outline: 2px solid var(--armed-display)");
        outline.Attribute("BorderThickness")?.Value.Should().Be("2");
        outline.Attribute("Margin")?.Value.Should().Be("-3", "outline-offset: 1px, outside a 2px band");
        outline.Attribute("Opacity")?.Value.Should().Be("0", "an unfocused field shows no outline");

        Setter(FieldStyleFor(control), "UseSystemFocusVisuals").Should().Be("False",
            "the field draws its own outline; the system's on top of it would be two rings");
    }

    /// <summary>A browser gives a field no hover rule, so the console's field changes nothing under the pointer.</summary>
    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    public void UnderThePointerAFieldChangesNothing(string control)
    {
        Setters(State(control, "PointerOver")).Should().BeEmpty(".field has no :hover rule to transcribe");
    }

    /// <summary>
    /// A header is a field label (:2312), and the label is already a text style, which a presenter cannot take.
    /// So the presenters restate it - and this is what stops them drifting from it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fields))]
    public void TheHeaderIsAFieldLabel(string control)
    {
        var label = XDocument.Load(PathOf(Typography)).Descendants(Presentation + "Style")
            .Single(style => style.Attribute(Xaml + "Key")?.Value == "FieldLabelTextStyle");
        var header = Named(control, "HeaderContentPresenter");

        foreach (var property in new[] { "FontFamily", "FontSize", "CharacterSpacing", "Margin", "Foreground" })
        {
            header.Attribute(property)?.Value.Should().Be(Setter(label, property),
                $"{control}'s header is FieldLabelTextStyle's {property}, as .field-label sets it");
        }
    }

    /// <summary>
    /// NumberBox draws its well through InputBox, a TextBox WinUI points at a style of its own. Pointed at the field
    /// instead, it is the field - and nothing else in the template may point it back.
    /// </summary>
    [Fact]
    public void NumberBoxTypesIntoTheField()
    {
        Named("NumberBox", "InputBox").Attribute("Style")?.Value.Should().Be($"{{StaticResource {FieldStyle}}}",
            "the number box's text box is the field; WinUI's NumberBoxTextBoxStyle is not");

        Template("NumberBox").Descendants(Presentation + "Setter")
            .Where(setter => setter.Attribute("Target")?.Value == "InputBox.Style")
            .Should().BeEmpty("a state that re-sets InputBox's style would swap the field out mid-use");
    }

    /// <summary>
    /// A locked NumberBox locks its InputBox, which dims itself as every field does. So the number box dims only
    /// what InputBox does not cover; dimming its own root as well would draw the field at .42 of .42.
    /// </summary>
    [Fact]
    public void ALockedNumberBoxDimsTheFieldOnce()
    {
        var targets = Setters(State("NumberBox", "Disabled"))
            .Where(setter => setter.Target.EndsWith(".Opacity", StringComparison.Ordinal))
            .Select(setter => setter.Target[..setter.Target.IndexOf('.')])
            .ToList();

        targets.Should().NotBeEmpty("the label and the spinners still have to read as locked");
        targets.Should().BeSubsetOf(["HeaderContentPresenter", "UpSpinButton", "DownSpinButton"],
            "InputBox dims itself; anything that contains it would dim it twice");
    }

    /// <summary>
    /// Text, the caret and a button's glyph follow the element's theme rather than its Foreground, so on Day Shift
    /// they would come out dark on a well that stays dark. The parts that draw them ask for the dark theme, as a
    /// Well or a Display does.
    /// </summary>
    [Theory]
    [MemberData(nameof(WellParts))]
    public void ThePartsInsideTheWellAskForTheDarkTheme(string control, string part)
    {
        Named(control, part).Attribute("RequestedTheme")?.Value.Should().Be("Dark",
            $"{control}'s {part} sits inside a well that is dark in both shifts");
    }

    [Fact]
    public void EveryFieldTokenIsInThePaletteInEveryShift()
    {
        var themes = XDocument.Load(PathOf(Tokens))
            .Descendants(Presentation + "ResourceDictionary.ThemeDictionaries").Single()
            .Elements()
            .ToDictionary(
                theme => theme.Attribute(Xaml + "Key")!.Value,
                theme => theme.Elements().Select(brush => brush.Attribute(Xaml + "Key")?.Value).OfType<string>()
                    .ToHashSet(StringComparer.Ordinal));

        themes.Keys.Should().BeEquivalentTo(["Default", "Light", "HighContrast"]);
        foreach (var (shift, written) in themes)
        {
            FieldTokens.Where(token => !written.Contains(token)).Should().BeEmpty(
                $"a token the {shift} palette does not write throws when the dictionary loads, at startup");
        }
    }

    /// <summary>
    /// A field on a page that names a style or paints its own face is a field the console does not reach - which
    /// is what the legacy SearchTextBoxStyle and a stray <c>Style="{StaticResource DefaultTextBoxStyle}"</c> did
    /// until this phase. Width, alignment and the rest of layout are the page's; the well is not.
    /// </summary>
    [Fact]
    public void NoPageRestylesAField()
    {
        var restyling = new[] { "Style", "Background", "BorderBrush", "BorderThickness", "Foreground", "CornerRadius", "Padding", "FontFamily", "FontSize" };

        var wrong = RepoSource.FilesUnder(Views, "*.xaml")
            .SelectMany(file => XDocument.Load(file, LoadOptions.SetLineInfo).Descendants()
                .Where(element => FieldControls.Contains(element.Name.LocalName) && element.Name.Namespace == Presentation)
                .SelectMany(element => restyling
                    .Where(property => element.Attribute(property) is not null ||
                                       element.Element(Presentation + $"{element.Name.LocalName}.{property}") is not null)
                    .Select(property => $"{RepoSource.Relative(file)}:{((System.Xml.IXmlLineInfo)element).LineNumber} " +
                                        $"{element.Name.LocalName} sets {property}")))
            .ToList();

        wrong.Should().BeEmpty("a field takes the console's well; a page that restyles one draws half a form in another idiom");
    }

    // ---------------------------------------------------------------- reading the dictionary

    private static List<XElement> Styles(string control) =>
        XDocument.Load(PathOf(Controls)).Root!.Elements(Presentation + "Style")
            .Where(style => style.Attribute("TargetType")?.Value == control)
            .ToList();

    /// <summary>The style that carries a control's field: the keyed one for TextBox, the implicit one otherwise.</summary>
    private static XElement FieldStyleFor(string control) =>
        control == "TextBox"
            ? Styles(control).Single(style => style.Attribute(Xaml + "Key")?.Value == FieldStyle)
            : Styles(control).Single(style => style.Attribute(Xaml + "Key") is null);

    /// <summary>The control's own template, not one of the inner buttons' templates inside it.</summary>
    private static XElement Template(string control) =>
        FieldStyleFor(control).Descendants(Presentation + "ControlTemplate")
            .First(template => template.Attribute("TargetType")?.Value == control);

    private static XElement Named(string control, string name)
    {
        var found = Template(control).Descendants()
            .Where(element => element.Attribute(Xaml + "Name")?.Value == name)
            .ToList();

        found.Should().HaveCount(1, $"{control}'s template has no single element named {name}");
        return found[0];
    }

    /// <summary>A state of the control's own, not one of its inner buttons' states of the same name.</summary>
    private static XElement State(string control, string name) =>
        Template(control).Element(Presentation + "Grid")!
            .Elements(Presentation + "VisualStateManager.VisualStateGroups")
            .Descendants(Presentation + "VisualState")
            .Single(state => state.Attribute(Xaml + "Name")?.Value == name);

    private static List<(string Target, string Value)> Setters(XElement state) =>
        state.Descendants(Presentation + "Setter")
            .Select(setter => (setter.Attribute("Target")?.Value ?? string.Empty, setter.Attribute("Value")?.Value ?? string.Empty))
            .ToList();

    private static string? Setter(XElement style, string property) =>
        style.Elements(Presentation + "Setter")
            .FirstOrDefault(setter => setter.Attribute("Property")?.Value == property)
            ?.Attribute("Value")?.Value;

    private static string PathOf(string relative) =>
        Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
