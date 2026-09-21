using System.Text.RegularExpressions;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The rule that says a caught exception is either acted on or explicitly excused.
/// <para>
/// It lives here rather than inside the test so the test can feed it snippets whose verdict is known. The
/// rule it replaces looked for <c>catch (…) { }</c> with a literally empty body and found 6 of them; a body
/// holding nothing but a comment discards the exception just as completely and there were 83 more of those,
/// none of which the old rule could see.
/// </para>
/// </summary>
internal static class SwallowedExceptionRule
{
    public readonly record struct Finding(int Line, string Body);

    /// <summary>The head of a catch clause, with its optional type, variable and filter.</summary>
    private static readonly Regex CatchHead = new(
        @"catch\s*(?:\(\s*(?<type>[\w\.<>]+)\s*(?<var>\w+)?\s*\))?\s*(?:when\s*\([^)]*\)\s*)?\{",
        RegexOptions.Compiled);

    /// <summary>The marker that turns a silent catch from an oversight into a decision a reader can check.</summary>
    private static readonly Regex Excused = new(@"//\s*Best effort:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Every catch in <paramref name="source"/> that does nothing and does not say why.</summary>
    public static IReadOnlyList<Finding> Unexplained(string source) => Examine(source).Unexplained;

    /// <summary>Every catch that does nothing but carries the marker.</summary>
    public static int Excusals(string source) => Examine(source).Excused;

    /// <summary>How many catch clauses the rule judged, so a caller can refuse a vacuous pass.</summary>
    public static int CatchesJudged(string source) => Examine(source).Judged;

    private static (List<Finding> Unexplained, int Excused, int Judged) Examine(string source)
    {
        var unexplained = new List<Finding>();
        var excused = 0;
        var judged = 0;
        var lastEnd = -1;

        foreach (Match head in CatchHead.Matches(source))
        {
            // Matches() can hand back overlapping heads around nested braces; count each clause once.
            if (head.Index <= lastEnd)
                continue;

            var open = head.Index + head.Length - 1;
            var body = BlockBody(source, open);
            if (body is null)
                continue;

            lastEnd = open;
            judged++;

            // A statement means the failure was acted on: logged, rethrown, turned into a result, or used
            // to steer what happens next. Only a body with none at all discards it outright.
            var statements = body
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                .ToList();

            if (statements.Count > 0)
                continue;

            // The marker may sit inside the body, or on the lines just above — which is where it goes for
            // the one-line `try { … } catch { }` form, whose body has no room for it.
            if (Excused.IsMatch(body) || Excused.IsMatch(LinesEndingAt(source, head.Index, 3)))
                excused++;
            else
                unexplained.Add(new Finding(LineOf(source, head.Index), Collapse(body)));
        }

        return (unexplained, excused, judged);
    }

    /// <summary>The text between the brace at <paramref name="open"/> and the one that closes it.</summary>
    private static string? BlockBody(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[(open + 1)..i];
        }

        return null;
    }

    /// <summary>The <paramref name="count"/> lines ending with the one <paramref name="at"/> falls on.</summary>
    private static string LinesEndingAt(string source, int at, int count)
    {
        var end = source.IndexOf('\n', at);
        if (end < 0) end = source.Length;

        var start = end;
        for (var taken = 0; taken < count && start > 0; taken++)
        {
            var previous = source.LastIndexOf('\n', start - 1);
            if (previous < 0) { start = 0; break; }
            start = previous;
        }

        return source[start..end];
    }

    private static string Collapse(string body) =>
        string.Join(" ", body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    private static int LineOf(string source, int at) => source.Take(at).Count(c => c == '\n') + 1;
}
