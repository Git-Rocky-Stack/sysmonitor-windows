using System.Text.RegularExpressions;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The rule that says a singleton holding something that has to be shut down must say it is disposable.
/// <para>
/// The host disposes the singletons it built, and only the ones that implement <c>IDisposable</c>. A service
/// that owns a window or a cancellation token source and does not implement it is never told the app is
/// closing: its loop keeps running and its window stays open, which for WinUI means the process does not
/// exit at all.
/// </para>
/// </summary>
internal static class SingletonDisposalRule
{
    public readonly record struct Finding(string Type, string Reason);

    /// <summary><c>services.AddSingleton&lt;Foo&gt;()</c> and <c>services.AddSingleton&lt;IFoo, Foo&gt;()</c>.</summary>
    private static readonly Regex Registration = new(
        @"AddSingleton<\s*(?:(?<iface>[\w\.]+)\s*,\s*)?(?<impl>[\w\.]+)\s*>\s*\(\s*\)",
        RegexOptions.Compiled);

    /// <summary>Fields whose type is something the owner has to shut down.</summary>
    private static readonly Regex OwnedResource = new(
        @"^[ \t]*(?:private|protected|internal|public)[^;=\r\n]*\b(?<kind>CancellationTokenSource|[A-Za-z]*Window|Timer|PeriodicTimer|Computer)\??\s+(?<field>_\w+)\s*[;=]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Every registered singleton that owns such a resource and is not disposable.</summary>
    public static IReadOnlyList<Finding> UndisposableOwners(
        string registrationSource,
        IReadOnlyDictionary<string, string> typeSources)
    {
        var findings = new List<Finding>();

        foreach (var type in RegisteredTypes(registrationSource))
        {
            if (!typeSources.TryGetValue(type, out var source))
                continue;

            var owned = OwnedResource.Matches(source)
                .Select(m => $"{m.Groups["kind"].Value} {m.Groups["field"].Value}")
                .Distinct()
                .ToList();

            if (owned.Count == 0)
                continue;

            if (DeclaresDisposable(source, type))
                continue;

            findings.Add(new Finding(type,
                $"holds {string.Join(", ", owned)} but is not IDisposable, so the host cannot shut it down"));
        }

        return findings;
    }

    /// <summary>The implementation types registered as singletons, so a caller can refuse a vacuous pass.</summary>
    public static IReadOnlyList<string> RegisteredTypes(string registrationSource) =>
        Registration.Matches(registrationSource)
            .Select(m => Simple(m.Groups["impl"].Value))
            .Distinct()
            .ToList();

    /// <summary>True when <paramref name="type"/>, or an interface it names in its base list, is disposable.</summary>
    private static bool DeclaresDisposable(string source, string type)
    {
        var declaration = Regex.Match(source, $@"\bclass\s+{Regex.Escape(type)}\b[^{{\r\n]*");
        if (!declaration.Success)
            return false;

        if (Regex.IsMatch(declaration.Value, @"\bIAsync?Disposable\b|\bIDisposable\b|\bIAsyncDisposable\b"))
            return true;

        // It may inherit disposability from an interface declared elsewhere; a Dispose method is the
        // observable promise either way.
        return Regex.IsMatch(source, @"public\s+(?:virtual\s+|override\s+)?void\s+Dispose\s*\(\s*\)")
            || Regex.IsMatch(source, @"public\s+ValueTask\s+DisposeAsync\s*\(\s*\)");
    }

    private static string Simple(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }
}
