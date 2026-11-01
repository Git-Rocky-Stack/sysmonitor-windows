using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The console is drawn as hardware: machined, solid-state equipment, not flat shapes. These rules hold the
/// realism in the shared primitives, so every page that uses a faceplate, a plate, a lamp, an LCD or a meter gets
/// it without asking, and a later edit cannot quietly flatten one of them again.
/// <list type="bullet">
/// <item>Panels are brushed aluminium - a faint directional texture laid over the face, never a colour - and are
/// screwed to the rack by a stainless socket-cap screw in each corner, small panels included.</item>
/// <item>Every readout sits behind glass: a glare across the top, a lit edge, and a vignette, over the reading.</item>
/// <item>An LED is a lens in a bezel: a domed body, a hot specular, and the light's own glow round it.</item>
/// <item>Caps and the lever deflect: a contact shadow under them that goes when they are pressed home.</item>
/// <item>A meter is a bar of discrete LEDs behind a lens; a process meter carries an ACT light that strikes when
/// the work begins.</item>
/// </list>
/// All of it is decoration, so High Contrast drops it, as it drops every other key light and bolt.
/// </summary>
public class HardwareRealismTests
{
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";
    private const string Instruments = "src/SysMonitor.App/Styles/Console/Instruments.xaml";
    private const string Controls = "src/SysMonitor.App/Styles/Console/Controls.xaml";
    private const string Legacy = "src/SysMonitor.App/Styles/Styles.xaml";
    private const string Views = "src/SysMonitor.App/Views";

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The decoration the realism adds to the palette: in both working shifts, and transparent in High Contrast.</summary>
    public static readonly TheoryData<string> RealismBrushes = new()
    {
        "BrushedGrainBrush", "BrushedGrainFineBrush", "BrushedSheenBrush",
        "StainlessHeadBrush", "StainlessChamferBrush", "StainlessSocketBrush", "StainlessSocketWallBrush",
        "StainlessSpecularBrush", "BoltCounterboreBrush",
        "GlassGlareBrush", "GlassEdgeBrush", "GlassVignetteBrush", "LcdPixelGridBrush",
        "LedBezelBrush", "LedDomeBrush", "LedSpecularBrush", "LedLensBrush", "LedSegmentGapBrush",
        "CapContactShadowBrush", "CapUnderBrush", "CapPressedShadeBrush", "SwitchThumbGripBrush",
    };

    // ------------------------------------------------------------------ the palette

    [Theory]
    [MemberData(nameof(RealismBrushes))]
    public void EveryShiftDrawsTheRealismAndHighContrastDropsIt(string key)
    {
        foreach (var shift in new[] { "Default", "Light" })
        {
            var brush = Shift(shift).Elements().SingleOrDefault(element => KeyOf(element) == key);
            brush.Should().NotBeNull($"{key} is drawn in the {shift} shift");
            brush!.Name.LocalName.Should().EndWith("GradientBrush",
                $"{key} is light falling on a surface, which is a gradient, never a flat colour");
        }

        var contrast = Shift("HighContrast").Elements().SingleOrDefault(element => KeyOf(element) == key);
        contrast.Should().NotBeNull($"every shift defines {key}");
        contrast!.Attribute("Color")?.Value.Should().Be("Transparent",
            $"{key} is decoration, and High Contrast draws the user's colours and nothing else");
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void BrushedAluminiumIsATextureNotAColour(string shift)
    {
        var periods = new List<double>();
        foreach (var key in new[] { "BrushedGrainBrush", "BrushedGrainFineBrush" })
        {
            var grain = Shift(shift).Elements().Single(element => KeyOf(element) == key);
            grain.Attribute("MappingMode")?.Value.Should().Be("Absolute", $"{key} is cut in pixels, like a machine's brushing");
            grain.Attribute("SpreadMethod")?.Value.Should().Be("Repeat", $"{key} repeats down the whole face");

            var (startX, startY) = Point(grain.Attribute("StartPoint")!.Value);
            var (endX, endY) = Point(grain.Attribute("EndPoint")!.Value);
            endX.Should().Be(startX, $"{key} runs down the face, so its lines lie across it: the brushing is horizontal");
            periods.Add(endY - startY);

            foreach (var stop in grain.Elements().Where(element => element.Name.LocalName == "GradientStop"))
            {
                Alpha(stop.Attribute("Color")!.Value).Should().BeLessThanOrEqualTo(0x1A,
                    $"{key} is a faint texture over the metal; a stop this strong would be a stripe, or a new colour");
            }
        }

        periods.Should().OnlyContain(period => period >= 2);
        Gcd((int)periods[0], (int)periods[1]).Should().Be(1,
            "two grains whose periods share no factor never line up, so the brushing reads as metal and not as ruled lines");
    }

    // ------------------------------------------------------------------ screws and panels

    [Fact]
    public void TheBoltIsAStainlessSocketCapScrew()
    {
        var template = Template(Instruments, "instruments:HexBolt");
        var parts = template.Descendants().ToList();

        parts.Should().Contain(element => element.Name.LocalName == "Viewbox",
            "one screw is drawn once and scaled, so a small panel's screws are the same screw, smaller");
        parts.Should().Contain(element => element.Name.LocalName == "Ellipse" && Uses(element, "StainlessHeadBrush"),
            "a socket-cap screw has a round head");
        parts.Should().Contain(element => element.Name.LocalName == "Ellipse" && Uses(element, "BoltCounterboreBrush"),
            "the head sits in a counterbore cut into the panel");
        parts.Should().Contain(element => Uses(element, "StainlessChamferBrush"), "the head's rim is chamfered");
        parts.Should().Contain(element => Uses(element, "StainlessSpecularBrush"), "polished stainless catches the key light");

        var socket = parts.SingleOrDefault(element => element.Name.LocalName == "Path" && Uses(element, "StainlessSocketBrush"));
        socket.Should().NotBeNull("the head is driven by a hex key, through a hexagonal socket");
        Vertices(socket!.Attribute("Data")!.Value).Should().Be(6, "a hex socket has six sides");
        parts.Should().Contain(element => element.Name.LocalName == "Path" && Uses(element, "StainlessSocketWallBrush"),
            "the socket has a wall, lit on the far side, or it reads as a printed hexagon rather than a hole");
    }

    [Theory]
    [InlineData("instruments:Faceplate")]
    [InlineData("instruments:Plate")]
    [InlineData("instruments:Banner")]
    public void EveryPanelIsScrewedDownAtItsFourCorners(string panel)
    {
        var corners = Template(Instruments, panel).Descendants()
            .Where(element => element.Name.LocalName == "HexBolt")
            .Select(bolt => $"{bolt.Attribute("HorizontalAlignment")?.Value}/{bolt.Attribute("VerticalAlignment")?.Value}")
            .ToList();

        corners.Should().BeEquivalentTo(["Left/Top", "Right/Top", "Left/Bottom", "Right/Bottom"],
            $"{panel} is a panel, and a panel is held by a screw in each corner, however small it is");
    }

    [Theory]
    [InlineData("instruments:Faceplate")]
    [InlineData("instruments:Plate")]
    public void EveryPanelIsBrushedAluminium(string panel)
    {
        var parts = Template(Instruments, panel).Descendants().ToList();
        var content = parts.FindIndex(element => element.Name.LocalName == "ContentPresenter");

        foreach (var key in new[] { "BrushedGrainBrush", "BrushedGrainFineBrush", "BrushedSheenBrush" })
        {
            var layer = parts.FindIndex(element => Uses(element, key));
            layer.Should().BeGreaterThan(-1, $"{panel}'s face is brushed: it carries {key}");
            layer.Should().BeLessThan(content, $"{key} is the metal under the panel's content, not a film over it");
            parts[layer].Attribute("IsHitTestVisible")?.Value.Should().Be("False", "a texture takes no pointer");
        }
    }

    // ------------------------------------------------------------------ glass

    [Theory]
    [InlineData("instruments:Lcd", "ValueText")]
    [InlineData("instruments:Well", null)]
    [InlineData("instruments:Display", null)]
    [InlineData("instruments:VuMeter", "PART_Segments")]
    public void EveryReadoutSitsBehindGlass(string readout, string? reading)
    {
        var parts = Template(Instruments, readout).Descendants().ToList();
        var under = reading is null
            ? parts.FindIndex(element => element.Name.LocalName == "ContentPresenter")
            : parts.FindIndex(element => element.Attribute(X + "Name")?.Value == reading);
        under.Should().BeGreaterThan(-1, $"{readout} has a reading to put glass over");

        foreach (var key in new[] { "GlassGlareBrush", "GlassEdgeBrush" })
        {
            var glass = parts.FindIndex(element => Uses(element, key));
            glass.Should().BeGreaterThan(under, $"{readout}'s {key} lies over the reading, as a lens does");
            parts[glass].Attribute("IsHitTestVisible")?.Value.Should().Be("False", "glass takes no pointer");
        }
    }

    [Fact]
    public void AnLcdIsAPanelOfPixelsBehindALens()
    {
        var parts = Template(Instruments, "instruments:Lcd").Descendants().ToList();
        var reading = parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "ValueText");

        parts.FindIndex(element => Uses(element, "LcdPixelGridBrush")).Should().BeGreaterThan(reading,
            "the reading is drawn on a pixel matrix, whose grid shows through it");
        parts.FindIndex(element => Uses(element, "GlassVignetteBrush")).Should().BeGreaterThan(reading,
            "the lens darkens towards its bezel");
    }

    // ------------------------------------------------------------------ LEDs

    [Fact]
    public void AnLedIsALensInABezel()
    {
        var parts = Template(Instruments, "instruments:LedDot").Descendants().ToList();
        var light = parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "Light");

        parts.FindIndex(element => element.Name.LocalName == "Ellipse" && Uses(element, "LedBezelBrush"))
            .Should().BeInRange(0, light - 1, "the bezel is behind the lens it holds");
        parts.FindIndex(element => element.Name.LocalName == "Ellipse" && Uses(element, "LedDomeBrush"))
            .Should().BeGreaterThan(light, "the dome shades the lens over its colour");
        parts.FindIndex(element => element.Name.LocalName == "Ellipse" && Uses(element, "LedSpecularBrush"))
            .Should().BeGreaterThan(light, "the lens catches the key light");
    }

    [Theory]
    [InlineData("instruments:Lamp", "Face")]
    [InlineData("instruments:ActivityLed", "Light")]
    public void EveryLightIsSeenThroughALens(string light, string face)
    {
        var parts = Template(Instruments, light).Descendants().ToList();
        var under = parts.FindIndex(element => element.Attribute(X + "Name")?.Value == face);

        under.Should().BeGreaterThan(-1);
        parts.FindIndex(element => Uses(element, "LedLensBrush") || Uses(element, "LedSpecularBrush"))
            .Should().BeGreaterThan(under, $"{light} is a light behind a lens, and the lens catches the key light");
        parts.Select(element => element.Attribute(X + "Name")?.Value)
            .Should().Contain(name => name == "PART_GlowHost" || name == "PART_OuterGlow",
                $"{light} glows round itself when it is lit");
    }

    // ------------------------------------------------------------------ caps and the lever

    [Theory]
    [InlineData("ConsoleCapButtonStyle")]
    [InlineData("ConsoleRepeatCapStyle")]
    public void ACapDeflectsUnderTheFinger(string style)
    {
        var template = KeyedTemplate(Controls, style);
        var parts = template.Descendants().ToList();
        var face = parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "Face");

        parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "ContactShadow" && Uses(element, "CapContactShadowBrush"))
            .Should().BeInRange(0, face - 1, "a raised cap casts a contact shadow on the panel below it");
        parts.FindIndex(element => Uses(element, "CapUnderBrush")).Should().BeGreaterThan(face,
            "a cap's lower bevel turns away from the light");
        parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "PressedShade" && Uses(element, "CapPressedShadeBrush"))
            .Should().BeGreaterThan(face, "a cap pressed home is shaded by the rim of its own well");

        var pressed = Setters(template, "Pressed");
        pressed.Should().Contain("ContactShadow.Opacity=0", "a cap pressed home sits on the panel and casts nothing");
        pressed.Should().Contain("PressedShade.Opacity=1");
        Setters(template, "Normal").Should().NotContain(setter => setter.StartsWith("PressedShade", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLeverThumbIsKnurledForTheThumb()
    {
        var template = Template(Controls, "ToggleSwitch");
        var thumbs = template.Descendants()
            .Where(element => element.Attribute(X + "Name")?.Value is "SwitchKnobOff" or "SwitchKnobOn")
            .ToList();

        thumbs.Should().HaveCount(2);
        thumbs.Should().OnlyContain(thumb => thumb.Descendants().Any(element => Uses(element, "SwitchThumbGripBrush")),
            "each thumb, lit or not, has a grip cut across it");
    }

    // ------------------------------------------------------------------ meters

    /// <summary>The parts ProgressBar's code finds by name, and the states it goes to (ProgressBar.cpp).</summary>
    private static readonly string[] ProgressBarParts =
        ["LayoutRoot", "DeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator2"];

    private static readonly string[] ProgressBarStates =
        ["Determinate", "Updating", "Indeterminate", "Error", "Paused", "IndeterminateError", "IndeterminatePaused"];

    [Fact]
    public void AMeterIsABarOfLedsBehindALens()
    {
        var template = KeyedTemplate(Controls, "ConsoleMeterProgressBarStyle");
        var parts = template.Descendants().ToList();
        var named = parts.Select(element => element.Attribute(X + "Name")?.Value).OfType<string>().ToHashSet();

        named.Should().Contain(ProgressBarParts, "ProgressBar's code sizes and animates these parts by name");
        parts.Where(element => ProgressBarParts.Skip(1).Contains(element.Attribute(X + "Name")?.Value))
            .Should().OnlyContain(element => element.Name.LocalName == "Rectangle", "ProgressBar's code looks them up as Rectangles");
        named.Should().Contain(ProgressBarStates, "ProgressBar's code goes to each of these states");

        var lit = parts.FindIndex(element => element.Attribute(X + "Name")?.Value == "DeterminateProgressBarIndicator");
        parts.FindIndex(element => Uses(element, "LedSegmentGapBrush")).Should().BeGreaterThan(lit,
            "the bar is cut into discrete LEDs over the lit length and the dark one alike");
        parts.FindIndex(element => Uses(element, "LedLensBrush")).Should().BeGreaterThan(lit, "the LEDs sit behind a lens");
        parts.FindIndex(element => Uses(element, "GlassGlareBrush")).Should().BeGreaterThan(lit, "the meter is a readout behind glass");
    }

    [Fact]
    public void EveryProgressBarIsAnLedMeter()
    {
        var implicitStyle = Styles(Controls).SingleOrDefault(style =>
            style.Attribute("TargetType")?.Value == "ProgressBar" && style.Attribute(X + "Key") is null);
        implicitStyle.Should().NotBeNull("a progress bar nobody styled is still an LED meter");
        implicitStyle!.Attribute("BasedOn")?.Value.Should().Be("{StaticResource ConsoleMeterProgressBarStyle}");

        var known = Styles(Controls).Concat(Styles(Legacy))
            .Where(style => style.Attribute(X + "Key") is not null)
            .ToDictionary(style => KeyOf(style)!, style => style);
        foreach (var style in Styles(Legacy).Where(style => style.Attribute("TargetType")?.Value == "ProgressBar"))
        {
            BasesOf(style, known).Should().Contain("ConsoleMeterProgressBarStyle",
                $"{KeyOf(style)} is a meter's colour, and the meter is the console's LED bar");
        }
    }

    [Fact]
    public void AProcessMeterCarriesAnActLightTiedToIt()
    {
        var style = Styles(Controls).Single(element => KeyOf(element) == "ConsoleProcessProgressBarStyle");
        style.Attribute("BasedOn")?.Value.Should().Be("{StaticResource ConsoleMeterProgressBarStyle}");

        var act = style.Descendants().SingleOrDefault(element => element.Name.LocalName == "ActivityLed");
        act.Should().NotBeNull("work under way shows on an ACT light beside its meter");
        act!.Attribute("Meter")?.Value.Should().Be("{Binding RelativeSource={RelativeSource TemplatedParent}}",
            "the light reads the meter it sits on, so it strikes when that meter's work begins");
    }

    /// <summary>A bar that reports work under way, as opposed to one that reports a level.</summary>
    private static readonly Regex ProcessBar = new(
        @"IsIndeterminate=""True""|Value=""\{x:Bind\s+ViewModel\.(?:\w*Progress\w*)", RegexOptions.Compiled);

    [Fact]
    public void EveryProcessOnAPageShowsItsActLight()
    {
        var offenders = new List<string>();
        var meters = 0;
        foreach (var file in RepoSource.FilesUnder(Views, "*.xaml"))
        {
            foreach (var bar in XDocument.Load(file).Descendants().Where(element => element.Name.LocalName == "ProgressBar"))
            {
                var text = bar.ToString();
                var styled = bar.Attribute("Style")?.Value == "{StaticResource ConsoleProcessProgressBarStyle}";
                if (!ProcessBar.IsMatch(text))
                {
                    meters++;
                    styled.Should().BeFalse($"{RepoSource.Relative(file)}: a level is a meter, not a process, and has no ACT light");
                    continue;
                }

                if (!styled)
                    offenders.Add($"{RepoSource.Relative(file)}: {text.Split('\n')[0].Trim()}");
                if (bar.Attribute("Background") is not null || bar.Attribute("CornerRadius") is not null)
                    offenders.Add($"{RepoSource.Relative(file)}: a process meter repaints its own track");
            }
        }

        meters.Should().BeGreaterThan(0, "the rule tells the two kinds apart, so both must exist");
        offenders.Should().BeEmpty("every process meter carries the ACT light, on the console's own LED track");

        Template(Instruments, "instruments:BusyPanel").Descendants()
            .Single(element => element.Attribute(X + "Name")?.Value == "PART_Bar")
            .Attribute("Style")?.Value.Should().Be("{StaticResource ConsoleProcessProgressBarStyle}",
                "the busy panel's bar is the work under way");
    }

    // ------------------------------------------------------------------ helpers

    private static XElement Shift(string key) =>
        XDocument.Load(Path.Combine(RepoSource.Root, Tokens)).Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary" && KeyOf(element) == key);

    private static string? KeyOf(XElement element) => element.Attribute(X + "Key")?.Value;

    private static IEnumerable<XElement> Styles(string file) =>
        XDocument.Load(Path.Combine(RepoSource.Root, file)).Descendants().Where(element => element.Name.LocalName == "Style");

    private static XElement Template(string file, string targetType) =>
        Styles(file).Where(style => style.Attribute("TargetType")?.Value == targetType && style.Attribute(X + "Key") is null)
            .SelectMany(style => style.Descendants().Where(element => element.Name.LocalName == "ControlTemplate"))
            .First();

    private static XElement KeyedTemplate(string file, string key) =>
        Styles(file).Single(style => KeyOf(style) == key)
            .Descendants().First(element => element.Name.LocalName == "ControlTemplate");

    private static bool Uses(XElement element, string key) =>
        element.Attributes().Any(attribute => attribute.Value == $"{{ThemeResource {key}}}");

    /// <summary>"Target=Value" for each setter in the named visual state.</summary>
    private static List<string> Setters(XElement template, string state) =>
        template.Descendants()
            .Where(element => element.Name.LocalName == "VisualState" && element.Attribute(X + "Name")?.Value == state)
            .SelectMany(element => element.Descendants().Where(setter => setter.Name.LocalName == "Setter"))
            .Select(setter => $"{setter.Attribute("Target")?.Value}={setter.Attribute("Value")?.Value}")
            .ToList();

    private static IEnumerable<string> BasesOf(XElement style, IReadOnlyDictionary<string, XElement> known)
    {
        for (var basedOn = style.Attribute("BasedOn")?.Value; basedOn is not null;)
        {
            var key = Regex.Match(basedOn, @"\{StaticResource\s+(\w+)\}").Groups[1].Value;
            yield return key;
            basedOn = known.TryGetValue(key, out var next) ? next.Attribute("BasedOn")?.Value : null;
        }
    }

    private static (double X, double Y) Point(string text)
    {
        var parts = text.Split(',');
        return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>The alpha of a colour written #AARRGGBB, or 255 for #RRGGBB.</summary>
    private static int Alpha(string colour) =>
        colour.Length == 9 ? int.Parse(colour.Substring(1, 2), NumberStyles.HexNumber) : 255;

    private static int Vertices(string data) => Regex.Matches(data, @"-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?").Count;

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
