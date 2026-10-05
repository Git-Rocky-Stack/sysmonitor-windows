using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The console is transcribed from System-X's stylesheet, and every transcription says where it read from:
/// <c>.btn, :2062</c> beside a cap, <c>(:545-552, Day :726-732)</c> above a group of the palette. A line number
/// is the one kind of citation that goes wrong while nobody touches it - the source file moves a rule and every
/// anchor past it now points somewhere else, still reading as though someone had checked.
/// <para>
/// So they are checked, against the revision the console names, vendored beside these tests
/// (<see cref="PinnedStylesheet"/>). Four things are checkable without a human reading CSS:
/// a selector named beside an anchor opens at that line; a range covers whole blocks rather than starting or
/// ending inside one; a point anchor is not a fragment of a sentence in a comment; and every colour a palette
/// group claims to have read out of its lines is in them.
/// </para>
/// <para>
/// What this cannot check is whether the prose beside an anchor describes what is there. That is a reading, and
/// it was done by hand once, at the revision named here.
/// </para>
/// </summary>
public class ConsolePinAnchorTests
{
    /// <summary>The console's own sources, which are the files allowed to cite the stylesheet.</summary>
    private static readonly string[] SourceFolders =
    [
        "src/SysMonitor.App/Styles/Console",
        "src/SysMonitor.App/Controls/Instruments",
    ];

    private static readonly string TokensFile =
        Path.Combine("src", "SysMonitor.App", "Styles", "Console", "Tokens.xaml");

    /// <summary>A <c>:1234</c> or <c>:1234-1250</c> anchor. Three digits up, so a time or a version is not one.</summary>
    private static readonly Regex Anchor = new(@":(?<first>\d{3,4})(?:-(?<last>\d{3,4}))?", RegexOptions.Compiled);

    /// <summary>A CSS class named immediately before its anchor, as the cap and switch styles write them.</summary>
    private static readonly Regex SelectorAnchor =
        new(@"(?<selector>\.[a-z][a-z0-9-]*), :(?<line>\d{3,4})", RegexOptions.Compiled);

    /// <summary>A palette group's colour, as the generator writes it into Tokens.xaml.</summary>
    private static readonly Regex Colour =
        new(@"<Color x:Key=""(?<name>\w+?)Color"">(?<value>#[0-9A-Fa-f]{6,8})</Color>", RegexOptions.Compiled);

    private static readonly Regex CssVariable =
        new(@"--(?<name>[a-z0-9-]+)\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled);

    /// <summary>
    /// The fixture is the stylesheet, not a copy someone tidied. Every other test here reads lines out of it, so
    /// an edited fixture would make all of them agree with whatever it was edited to say.
    /// </summary>
    [Fact]
    public void TheVendoredStylesheetIsTheRevisionTheConsoleNames()
    {
        PinnedStylesheet.ActualBodySha256().Should().Be(PinnedStylesheet.BodySha256,
            "the fixture is System-X's file at the pin; a changed one silently re-points every anchor below");

        PinnedStylesheet.Header().Should().Contain(PinnedStylesheet.Revision,
            "the fixture has to say which revision it is, or nothing says what these anchors were checked against");
    }

    /// <summary>
    /// The console names one revision. Two would mean half the anchors were checked against a file the other half
    /// was not written from.
    /// </summary>
    [Fact]
    public void EverySourceThatNamesARevisionNamesThePinnedOne()
    {
        var named = new Regex(@"system-x-app@(?<revision>[0-9a-f]{7,40})");
        var wrong = new List<string>();

        foreach (var file in CitingFiles().Concat([Path.Combine(RepoSource.Root, "scripts", "generate-tokens.py")]))
        {
            foreach (Match match in named.Matches(File.ReadAllText(file)))
            {
                if (!match.Groups["revision"].Value.StartsWith(PinnedStylesheet.Revision, StringComparison.Ordinal))
                    wrong.Add($"{RepoSource.Relative(file)} names {match.Groups["revision"].Value}");
            }
        }

        wrong.Should().BeEmpty($"the console is transcribed from {PinnedStylesheet.Revision} and its anchors are that file's lines");
    }

    /// <summary>
    /// <c>.btn, :2062</c> is a claim that line 2062 is where <c>.btn</c> starts. It either is or it is not.
    /// </summary>
    [Fact]
    public void EverySelectorNamedBesideAnAnchorOpensAtThatLine()
    {
        var wrong = new List<string>();

        foreach (var (file, line, text) in CitingLines())
        {
            foreach (Match match in SelectorAnchor.Matches(text))
            {
                var selector = match.Groups["selector"].Value;
                var number = int.Parse(match.Groups["line"].Value);
                var found = PinnedStylesheet.Line(number).Trim();

                // A rule either stands alone (`.btn {`) or heads a list (`.btn,`).
                if (!found.StartsWith($"{selector} {{", StringComparison.Ordinal) &&
                    !found.StartsWith($"{selector},", StringComparison.Ordinal))
                {
                    wrong.Add($"{RepoSource.Relative(file)}:{line} says {selector} is at :{number}, which is `{found}`");
                }
            }
        }

        wrong.Should().BeEmpty("a selector anchor names the line its rule opens on, and the stylesheet says otherwise");
    }

    /// <summary>
    /// An anchor is followed by a reader, who lands on the line it names. So the line it names has to be one you
    /// could have meant: a rule, an at-rule, a declaration, a whole comment. The two ways to name a line nobody
    /// meant are a fragment of a sentence inside a comment, and the second half of a selector list - and both
    /// read as deliberate, because the number is right there and looks measured.
    /// <para>
    /// <c>:458-467</c> was the second of those. It was cited as the 1 Hz blink, whose keyframes are
    /// <c>:456-465</c>; starting two lines in means starting at <c>49.9% {</c>, and the range then runs past the
    /// block's end into the one after it.
    /// </para>
    /// <para>
    /// Where a range *ends* is not checked. Some stop on the closing brace and some on the last declaration
    /// before it, and both land a reader in the right rule, which is the whole job of the number. Only the start
    /// decides what is found.
    /// </para>
    /// </summary>
    [Fact]
    public void NoAnchorStartsInTheMiddleOfSomething()
    {
        var wrong = new List<string>();

        foreach (var (file, line, text) in CitingLines())
        {
            foreach (Match match in Anchor.Matches(text))
            {
                if (SkipsThePin(text))
                    continue;

                var first = int.Parse(match.Groups["first"].Value);
                var cited = match.Groups["last"].Success ? $":{first}-{match.Groups["last"].Value}" : $":{first}";

                if (first > PinnedStylesheet.Lines.Count)
                {
                    wrong.Add($"{RepoSource.Relative(file)}:{line} cites {cited}, past the end of the stylesheet");
                    continue;
                }

                if (match.Groups["last"].Success && int.Parse(match.Groups["last"].Value) < first)
                {
                    wrong.Add($"{RepoSource.Relative(file)}:{line} cites {cited}, which runs backwards");
                    continue;
                }

                var found = PinnedStylesheet.Line(first).Trim();

                if (InsideAComment(first))
                    wrong.Add($"{RepoSource.Relative(file)}:{line} cites {cited}, which starts mid-comment: `{found}`");
                else if (ContinuesASelector(first))
                    wrong.Add($"{RepoSource.Relative(file)}:{line} cites {cited}, which starts part-way down a selector list: `{found}`");
            }
        }

        wrong.Should().BeEmpty("an anchor names the line a reader lands on, and these land part-way into something");
    }

    /// <summary>
    /// The palette says where each group was read from. Every colour in the group has to be in those lines - as
    /// the hex the stylesheet spells, as an <c>rgba()</c>, or one hop through the <c>var()</c> a token aliases.
    /// A colour that is nowhere in them was read somewhere else, and the anchor is the part that is wrong.
    /// <para>
    /// A Day value equal to its Night one is skipped: Day Shift only re-declares what it changes, so the Day
    /// anchor has nothing to say about a token it leaves alone.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Default", "Light", false)]
    [InlineData("Light", "HighContrast", true)]
    public void EveryPaletteColourIsInTheLinesItsGroupAnchors(string shift, string next, bool day)
    {
        var tokens = File.ReadAllText(Path.Combine(RepoSource.Root, TokensFile));
        var night = Section(tokens, "Default", "Light");
        var nightValues = Colour.Matches(night)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value, StringComparer.Ordinal);

        var checkedCount = 0;
        var wrong = new List<string>();

        foreach (var (comment, body) in Groups(Section(tokens, shift, next)))
        {
            var ranges = RangesIn(comment, day);
            if (ranges.Count == 0)
                continue;

            var slab = Resolved(ranges);

            foreach (Match match in Colour.Matches(body))
            {
                var name = match.Groups["name"].Value;
                var value = match.Groups["value"].Value;

                if (day && nightValues.TryGetValue(name, out var unchanged) &&
                    string.Equals(unchanged, value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                checkedCount++;
                if (!Spellings(value).Any(spelling => slab.Contains(spelling, StringComparison.Ordinal)))
                {
                    var cited = string.Join(", ", ranges.Select(r => r.First == r.Last ? $":{r.First}" : $":{r.First}-{r.Last}"));
                    wrong.Add($"{name} is {value} in {shift}, which is not in {cited}");
                }
            }
        }

        checkedCount.Should().BeGreaterThan(20, "a rule that checks almost nothing passes for the wrong reason");
        wrong.Should().BeEmpty($"every {shift} colour is transcribed from the lines its group cites");
    }

    /// <summary>
    /// Controls.xaml deliberately cites one line of System-X's *working tree* - where <c>.btn</c> has moved to
    /// since the pin - to say why selectors are written beside their numbers. It is the one anchor here that is
    /// not a claim about the pinned file, and it says so on the same line.
    /// </summary>
    private static bool SkipsThePin(string text) =>
        text.Contains("working tree", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a line is prose inside a comment rather than the line the comment opens on.</summary>
    private static bool InsideAComment(int number)
    {
        var line = PinnedStylesheet.Line(number);
        if (line.Contains("/*", StringComparison.Ordinal))
            return false;

        // Walk back: an unclosed /* above means this line is inside it.
        for (var i = number - 1; i >= 1; i--)
        {
            var above = PinnedStylesheet.Line(i);
            var opens = above.LastIndexOf("/*", StringComparison.Ordinal);
            var closes = above.LastIndexOf("*/", StringComparison.Ordinal);
            if (opens >= 0 && opens > closes)
                return true;
            if (closes >= 0)
                return false;
        }

        return false;
    }

    /// <summary>
    /// Whether a line that opens a block is the second or later line of a selector list - <c>49.9% {</c> sitting
    /// under <c>0%,</c>. The rule starts above it, so this is not the line it starts on.
    /// </summary>
    private static bool ContinuesASelector(int number)
    {
        if (!PinnedStylesheet.Line(number).Contains('{'))
            return false;

        // Only the line directly above decides it: a selector list is unbroken, and a blank line ends one.
        if (number <= 1)
            return false;

        var above = PinnedStylesheet.Line(number - 1).Trim();
        return above.Length > 0 && above.EndsWith(',');
    }

    /// <summary>Everything the stylesheet writes for a custom property, for the one hop a token may alias through.</summary>
    private static readonly Lazy<ILookup<string, string>> Variables = new(() =>
        CssVariable.Matches(string.Join('\n', PinnedStylesheet.Lines))
            .ToLookup(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim(), StringComparer.Ordinal));

    private static string Resolved(IReadOnlyList<(int First, int Last)> ranges)
    {
        var slab = string.Join('\n', ranges.Select(r => PinnedStylesheet.Slab(r.First, r.Last)));
        var aliased = Regex.Matches(slab, @"var\(--(?<name>[a-z0-9-]+)")
            .SelectMany(m => Variables.Value[m.Groups["name"].Value]);

        return (slab + "\n" + string.Join('\n', aliased)).ToLowerInvariant();
    }

    /// <summary>Every way the stylesheet might spell a colour the palette holds as #RRGGBB or #AARRGGBB.</summary>
    private static IEnumerable<string> Spellings(string value)
    {
        var hex = value.ToLowerInvariant();
        if (hex.Length == 9)
        {
            var (r, g, b) = (Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16), Convert.ToInt32(hex[7..9], 16));
            yield return $"rgba({r}, {g}, {b}";
        }
        else
        {
            var (r, g, b) = (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));
            yield return hex;
            yield return $"rgb({r}, {g}, {b}";
            yield return $"rgba({r}, {g}, {b}";
        }
    }

    /// <summary>A theme dictionary's text, between its key and the next one's.</summary>
    private static string Section(string tokens, string key, string next)
    {
        var start = tokens.IndexOf($@"<ResourceDictionary x:Key=""{key}"">", StringComparison.Ordinal);
        var end = tokens.IndexOf($@"<ResourceDictionary x:Key=""{next}"">", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the palette has no {key} shift, so this rule would read nothing");
        end.Should().BeGreaterThan(start, $"the palette has no {next} shift after {key}");
        return tokens[start..end];
    }

    /// <summary>Each comment in a theme dictionary and the colours written under it, which is one group.</summary>
    private static IEnumerable<(string Comment, string Body)> Groups(string section)
    {
        var parts = Regex.Split(section, @"<!--(.*?)-->", RegexOptions.Singleline);
        for (var i = 1; i < parts.Length - 1; i += 2)
            yield return (parts[i], parts[i + 1]);
    }

    /// <summary>
    /// The lines a group's comment cites for one shift. Night is what stands before the word Day inside the
    /// brackets, Day what stands after it; a group with no Day anchor is the same in both shifts, and the one
    /// range serves for both. A bare <c>:N</c> means the block opening there.
    /// </summary>
    private static List<(int First, int Last)> RangesIn(string comment, bool day)
    {
        var found = new List<(int, int)>();

        foreach (Match bracket in Regex.Matches(comment, @"\(([^)]*)\)", RegexOptions.Singleline))
        {
            var segments = Regex.Split(bracket.Groups[1].Value, @"\bDay\b");
            var chosen = day
                ? (segments.Length > 1 ? segments[1..] : segments[..1])
                : segments[..1];

            foreach (var segment in chosen)
            {
                foreach (Match match in Anchor.Matches(segment))
                {
                    var first = int.Parse(match.Groups["first"].Value);
                    if (first > PinnedStylesheet.Lines.Count)
                        continue;

                    var last = match.Groups["last"].Success ? int.Parse(match.Groups["last"].Value) : BlockEnd(first);
                    found.Add((first, Math.Min(last, PinnedStylesheet.Lines.Count)));
                }
            }
        }

        return found;
    }

    /// <summary>Where the block opening at or just after a line closes.</summary>
    private static int BlockEnd(int first)
    {
        var depth = 0;
        var opened = false;

        for (var i = first; i <= PinnedStylesheet.Lines.Count; i++)
        {
            foreach (var character in PinnedStylesheet.Line(i))
            {
                if (character == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (character == '}')
                {
                    depth--;
                }
            }

            if (opened && depth <= 0)
                return i;
        }

        return Math.Min(first + 30, PinnedStylesheet.Lines.Count);
    }

    private static IEnumerable<string> CitingFiles() =>
        SourceFolders
            .SelectMany(folder => RepoSource.FilesUnder(folder, "*.xaml").Concat(RepoSource.FilesUnder(folder, "*.cs")))
            .Distinct();

    /// <summary>Every line of the console's sources that could carry an anchor, with where it is.</summary>
    private static IEnumerable<(string File, int Line, string Text)> CitingLines()
    {
        foreach (var file in CitingFiles())
        {
            // Tokens.xaml is generated; its anchors are the generator's and are checked as palette groups.
            if (file.EndsWith(TokensFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                yield return (file, i + 1, lines[i]);
        }
    }
}
