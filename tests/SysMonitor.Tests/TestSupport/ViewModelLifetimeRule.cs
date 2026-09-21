using System.Text.RegularExpressions;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The rules about what a view model hands to a singleton and what it takes back.
/// <para>
/// They live here rather than inside the test so the test can feed them snippets whose verdict is known.
/// Both were previously written in a way that could not fail: one searched the whole file for the matching
/// <c>-=</c>, so moving the unsubscribes into a method nobody calls satisfied it; the other found a
/// <c>Dispose()</c> body only when its closing brace sat at exactly four spaces of indentation.
/// </para>
/// </summary>
internal static class ViewModelLifetimeRule
{
    public readonly record struct Finding(string What, string Reason);

    /// <summary>`_someService.SomeEvent += OnSomething;` — handing a handler to something the view model does not own.</summary>
    private static readonly Regex NamedSubscription =
        new(@"^[ \t]*(?<target>_\w+)\.(?<event>\w+)\s*\+=\s*(?<handler>\w+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>`_someService.SomeEvent += (s, e) => …;` — a handler with no name, so nothing can ever take it back.</summary>
    private static readonly Regex LambdaSubscription =
        new(@"^[ \t]*(?<target>_\w+)\.(?<event>\w+)\s*\+=\s*(?:async\s+)?(?:\(|\w+\s*=>)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The methods a page is guaranteed to reach when the user leaves it.</summary>
    private static readonly string[] TeardownMethods = ["Dispose", "Cleanup", "OnNavigatedFrom", "Unloaded"];

    /// <summary>Every subscription in <paramref name="source"/> that nothing reachable undoes.</summary>
    public static IReadOnlyList<Finding> LeakedSubscriptions(string source)
    {
        var leaks = new List<Finding>();

        foreach (Match match in LambdaSubscription.Matches(source))
        {
            var target = match.Groups["target"].Value;
            var name = match.Groups["event"].Value;
            leaks.Add(new Finding($"{target}.{name}",
                "is subscribed with a lambda, which has no name and so can never be unsubscribed"));
        }

        foreach (Match match in NamedSubscription.Matches(source))
        {
            var target = match.Groups["target"].Value;
            var name = match.Groups["event"].Value;
            var handler = match.Groups["handler"].Value;
            var what = $"{target}.{name} += {handler}";

            var unsubscribe = new Regex(
                $@"{Regex.Escape(target)}\.{Regex.Escape(name)}\s*-=\s*{Regex.Escape(handler)}\s*;");

            var match2 = unsubscribe.Match(source);
            if (!match2.Success)
            {
                leaks.Add(new Finding(what, "is never undone"));
                continue;
            }

            // Undoing it in a method nothing calls is the same as not undoing it.
            var method = EnclosingMethodName(source, match2.Index);
            if (method is null)
                leaks.Add(new Finding(what, "is undone outside any method"));
            else if (!TeardownMethods.Contains(method) && !IsCalledFromTeardown(source, method))
                leaks.Add(new Finding(what, $"is only undone in {method}(), which nothing reachable from teardown calls"));
        }

        return leaks;
    }

    /// <summary>How many subscriptions the rule judged, so a caller can refuse a vacuous pass.</summary>
    public static int SubscriptionsJudged(string source) =>
        NamedSubscription.Matches(source).Count + LambdaSubscription.Matches(source).Count;

    /// <summary>Every <c>Dispose()</c> in <paramref name="source"/> whose body releases nothing.</summary>
    public static IReadOnlyList<Finding> EmptyDisposeBodies(string source)
    {
        var empty = new List<Finding>();

        foreach (var (signature, body) in DisposeBodies(source))
        {
            var statements = body
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0
                            && !line.StartsWith("//", StringComparison.Ordinal)
                            && line != "{" && line != "}"
                            && line != "GC.SuppressFinalize(this);")
                .ToList();

            if (statements.Count == 0)
                empty.Add(new Finding(signature, "implements Dispose() but releases nothing"));
        }

        return empty;
    }

    /// <summary>How many Dispose() bodies the rule found, so a caller can refuse a vacuous pass.</summary>
    public static int DisposeBodiesFound(string source) => DisposeBodies(source).Count;

    /// <summary>
    /// Each <c>public void Dispose()</c> and its body, found by matching braces rather than by expecting the
    /// closing one at a particular indentation.
    /// </summary>
    private static List<(string Signature, string Body)> DisposeBodies(string source)
    {
        var found = new List<(string, string)>();

        foreach (Match match in Regex.Matches(source, @"public\s+(?:virtual\s+|override\s+)?void\s+Dispose\s*\(\s*\)\s*\{"))
        {
            var open = match.Index + match.Length - 1;
            var body = BlockBody(source, open);
            if (body is not null)
                found.Add((match.Value.TrimEnd('{', ' ', '\r', '\n'), body));
        }

        return found;
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

    /// <summary>True when <paramref name="method"/> is called from somewhere teardown reaches.</summary>
    private static bool IsCalledFromTeardown(string source, string method)
    {
        foreach (Match call in Regex.Matches(source, $@"\b{Regex.Escape(method)}\s*\("))
        {
            // Skip the declaration itself.
            if (Regex.IsMatch(source[LineStart(source, call.Index)..call.Index], @"\b(void|Task|async)\b"))
                continue;

            var caller = EnclosingMethodName(source, call.Index);
            if (caller is not null && caller != method &&
                (TeardownMethods.Contains(caller) || IsCalledFromTeardown(source, caller, method)))
                return true;
        }

        return false;
    }

    /// <summary>One more hop, with the starting method excluded so a cycle cannot recurse forever.</summary>
    private static bool IsCalledFromTeardown(string source, string method, string alreadySeen)
    {
        foreach (Match call in Regex.Matches(source, $@"\b{Regex.Escape(method)}\s*\("))
        {
            if (Regex.IsMatch(source[LineStart(source, call.Index)..call.Index], @"\b(void|Task|async)\b"))
                continue;

            var caller = EnclosingMethodName(source, call.Index);
            if (caller is not null && caller != method && caller != alreadySeen && TeardownMethods.Contains(caller))
                return true;
        }

        return false;
    }

    /// <summary>The name of the method containing <paramref name="at"/>, or null when it cannot be found.</summary>
    private static string? EnclosingMethodName(string source, int at)
    {
        var depth = 0;
        for (var i = at; i >= 0; i--)
        {
            if (source[i] == '}') depth++;
            else if (source[i] == '{')
            {
                if (depth == 0)
                {
                    var declaration = DeclarationBefore(source, i);
                    if (Regex.IsMatch(declaration, @"\b(class|record|struct|interface|namespace|enum)\b"))
                        return null;

                    var name = Regex.Match(declaration, @"(?<name>\w+)\s*\([^()]*\)\s*$");
                    if (name.Success)
                        return name.Groups["name"].Value;
                }
                else depth--;
            }
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

    private static int LineStart(string source, int at) => source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
}
