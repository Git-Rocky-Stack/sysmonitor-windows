using System.Text.RegularExpressions;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The rule that says a <c>Process</c> the code opens is a kernel handle the same method has to close.
/// <para>
/// It lives here rather than inside the test so the test can feed it snippets whose verdict is known, and
/// prove the rule finds a leak at all before trusting it to say the repository has none.
/// </para>
/// </summary>
internal static class ProcessHandleRule
{
    /// <summary>The Process factories that hand back something owning a kernel handle.</summary>
    private static readonly Regex Factory = new(
        @"(?:[A-Za-z_][\w\.]*\.)?Process\.(?:GetProcesses|GetProcessesByName|GetProcessById|GetCurrentProcess)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// What binds the call's result: <c>using var p =</c>, <c>using (var p =</c>, <c>var p =</c>,
    /// <c>Process[] p =</c>, or a plain <c>p =</c> onto a variable declared earlier.
    /// </summary>
    private static readonly Regex Binding = new(
        @"(?<using>using\s*\(?\s*)?(?:var\s+|[A-Za-z_][\w<>\[\],\.]*\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*$",
        RegexOptions.Compiled);

    /// <summary><c>foreach (var item in source)</c>, for finding what a loop over an array of processes calls each one.</summary>
    private static readonly Regex ForEachOver = new(
        @"foreach\s*\(\s*(?:var|[A-Za-z_][\w<>\[\],\.]*)\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<source>[A-Za-z_]\w*)\s*\)",
        RegexOptions.Compiled);

    /// <summary>A brace that opens the body of a type rather than of a method.</summary>
    private static readonly Regex TypeHeader = new(
        @"\b(class|record|struct|interface|namespace|enum)\b", RegexOptions.Compiled);

    public readonly record struct Finding(int Line, string Reason);

    /// <summary>Every place in <paramref name="source"/> that opens a process and does not close it.</summary>
    public static IReadOnlyList<Finding> Leaks(string source) => Examine(source).Leaks;

    /// <summary>How many process-opening calls the rule judged, so a caller can refuse a vacuous pass.</summary>
    public static int CallsJudged(string source) => Examine(source).Judged;

    private static (List<Finding> Leaks, int Judged) Examine(string source)
    {
        var leaks = new List<Finding>();
        var judged = 0;

        foreach (Match call in Factory.Matches(source))
        {
            var lineStart = LineStart(source, call.Index);
            var before = source[lineStart..call.Index];

            // A rule that flags the comment explaining the rule teaches people to ignore it.
            if (before.Contains("//", StringComparison.Ordinal))
                continue;

            judged++;
            var line = LineOf(source, call.Index);

            var binding = Binding.Match(before.TrimStart());
            if (!binding.Success)
            {
                leaks.Add(new Finding(line, "not bound to a local, so nothing can dispose it"));
                continue;
            }

            // `using var p = ...` and `using (var p = ...)` close it when the scope ends.
            if (binding.Groups["using"].Success)
                continue;

            var method = EnclosingMethodBody(source, call.Index);
            if (method is null)
            {
                leaks.Add(new Finding(line, "could not find the method it sits in"));
                continue;
            }

            var name = binding.Groups["name"].Value;
            if (!DisposedIn(method, name))
                leaks.Add(new Finding(line, $"'{name}' is never disposed in this method"));
        }

        return (leaks, judged);
    }

    /// <summary>
    /// True when <paramref name="name"/> is disposed in this method: directly, or by a loop over it that
    /// disposes each item.
    /// </summary>
    private static bool DisposedIn(string method, string name)
    {
        if (Regex.IsMatch(method, $@"\b{Regex.Escape(name)}\s*\??\.Dispose\s*\("))
            return true;

        foreach (Match loop in ForEachOver.Matches(method))
        {
            if (loop.Groups["source"].Value != name)
                continue;

            var item = loop.Groups["item"].Value;
            if (Regex.IsMatch(method[loop.Index..], $@"\b{Regex.Escape(item)}\s*\??\.Dispose\s*\("))
                return true;
        }

        return false;
    }

    private static int LineStart(string source, int at) => source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;

    private static int LineOf(string source, int at) => source.Take(at).Count(c => c == '\n') + 1;

    /// <summary>The body of the method containing <paramref name="at"/>, or null when it cannot be found.</summary>
    private static string? EnclosingMethodBody(string source, int at)
    {
        // Walk outwards through enclosing blocks and stop at the first whose header declares a type. The
        // block below that one is the method. The header is the text back to the previous statement end,
        // not the text on the brace's own line - `class Foo` and `{` are usually on separate lines, and
        // reading only the brace's line made every member of the type look like one huge method.
        var open = -1;
        var depth = 0;
        for (var i = at; i >= 0; i--)
        {
            if (source[i] == '}') depth++;
            else if (source[i] == '{')
            {
                if (depth == 0)
                {
                    if (TypeHeader.IsMatch(DeclarationBefore(source, i)))
                        break;
                    open = i;
                }
                else depth--;
            }
        }

        if (open < 0) return null;

        var level = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') level++;
            else if (source[i] == '}' && --level == 0) return source[open..i];
        }

        return null;
    }

    /// <summary>The declaration a brace belongs to: everything back to the previous <c>;</c>, <c>{</c> or <c>}</c>.</summary>
    private static string DeclarationBefore(string source, int brace)
    {
        for (var i = brace - 1; i >= 0; i--)
        {
            if (source[i] is ';' or '{' or '}')
                return source[(i + 1)..brace];
        }

        return source[..brace];
    }
}
