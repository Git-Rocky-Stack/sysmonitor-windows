using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Core.Models;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A status on the console carries a word and a <see cref="LampState"/>, and the page colours it with
/// <c>StateBrush</c>, which asks the palette for the state's colour in the shift the element is shown in. So the
/// table that turns a state into a palette key is the whole of the console's status colouring, and it is checked
/// here: every state has a key for each use, and every key it names is written for all three shifts.
/// <para>
/// A key the palette does not write would not throw - <c>ConsolePalette</c> answers null - so the element would
/// simply lose its colour, in one shift, with nothing to say so. That the brushes then reach a live element, and
/// follow a change of shift, is measured in the UI smoke run (UiSmokeRun.CheckStateBrushAsync).
/// </para>
/// </summary>
public class ConsoleStateBrushTests
{
    private const string StateBrush = "src/SysMonitor.App/Controls/Instruments/StateBrush.cs";
    private const string Tokens = "src/SysMonitor.App/Styles/Console/Tokens.xaml";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string> Uses => new(["WordKey", "FillKey", "WashKey"]);

    /// <summary>
    /// Each use names a key for every state but Off, which may fall to the default arm - a word that is off is
    /// silver mute, a fill anthracite, a wash nothing. A state added to <see cref="LampState"/> and forgotten here
    /// would fall to that default too, and read as Off.
    /// </summary>
    [Theory]
    [MemberData(nameof(Uses))]
    public void EveryLitStateHasAKeyOfItsOwnForEachUse(string use)
    {
        var arms = Arms(use);

        var lit = Enum.GetNames<LampState>().Where(state => state != nameof(LampState.Off));
        lit.Where(state => !arms.ContainsKey(state)).Should().BeEmpty(
            $"{use} has to say what every lit state looks like; one left to the default arm draws as Off");
    }

    [Fact]
    public void EveryKeyTheStatesNameIsInThePaletteInEveryShift()
    {
        // The keys in the three tables, and the default arms that hold Off's.
        var keys = new[] { "WordKey", "FillKey", "WashKey" }
            .SelectMany(use => Arms(use).Values.Concat(DefaultArm(use)))
            .Distinct()
            .ToList();
        keys.Should().HaveCountGreaterThan(15, "this test is worthless if it cannot find the keys it judges");

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
            keys.Where(key => !written.Contains(key)).Should().BeEmpty(
                $"a state colour the {shift} palette does not write leaves the element uncoloured in that shift");
        }
    }

    /// <summary>
    /// A word is the state colour and a fill the rail colour: System-X darkens both for Day Shift, and the LED
    /// colours it does not. A word drawn in an LED colour is bright green on silver by day.
    /// </summary>
    [Fact]
    public void WordsAndFillsTakeTheColoursDayShiftDarkens()
    {
        Arms("WordKey").Where(arm => arm.Key != nameof(LampState.Armed))
            .Should().OnlyContain(arm => arm.Value.StartsWith("State", StringComparison.Ordinal),
                "a status word is --state-*, which Day Shift darkens so it reads on silver");
        Arms("FillKey").Where(arm => arm.Key != nameof(LampState.Armed))
            .Should().OnlyContain(arm => arm.Value.StartsWith("Rail", StringComparison.Ordinal),
                "a status fill is the rail colour, which Day Shift darkens the same way");
    }

    /// <summary>The arms of one of StateBrush's key tables: state name to palette key.</summary>
    private static Dictionary<string, string> Arms(string method)
    {
        var body = Regex.Match(Source(), method + @"\(LampState state\) => state switch\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline);
        body.Success.Should().BeTrue($"StateBrush has no {method} table to read");

        return Regex.Matches(body.Groups["body"].Value, @"LampState\.(?<state>\w+)\s*=>\s*""(?<key>\w+)""")
            .ToDictionary(match => match.Groups["state"].Value, match => match.Groups["key"].Value);
    }

    /// <summary>The key a table's default arm gives, which is Off's; none when Off takes no brush.</summary>
    private static IEnumerable<string> DefaultArm(string method) =>
        Regex.Matches(Body(method), @"_\s*=>\s*""(?<key>\w+)""").Select(match => match.Groups["key"].Value);

    private static string Body(string method) =>
        Regex.Match(Source(), method + @"\(LampState state\) => state switch\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline).Groups["body"].Value;

    private static string Source() => File.ReadAllText(PathOf(StateBrush));

    private static string PathOf(string relative) =>
        Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
