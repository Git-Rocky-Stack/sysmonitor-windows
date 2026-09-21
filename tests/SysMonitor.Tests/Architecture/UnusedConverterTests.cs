using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Every converter is declared once in App.xaml, as an application resource with an <c>x:Key</c>, and is
/// reached from markup as <c>{StaticResource ThatKey}</c>. A converter with a key that no markup binds is
/// a class that ships in every build and converts nothing.
/// <para>
/// StringToColorConverter was exactly this. It was added in "fix: Add StringToColorConverter to fix
/// InstalledProgramsPage crash"; the page later moved to StringToBrushConverter and the original stayed
/// behind, still registered, no longer bound by anything. The whole-file orphan sweep cannot see this —
/// Converters.cs is emphatically reachable, so the file passes while two of the thirty-nine types inside
/// it are dead.
/// </para>
/// <para>
/// Keys are read from App.xaml rather than from the class declarations on purpose: an unregistered
/// converter is a different defect (a binding that throws at runtime), and this rule is about the ones
/// that are wired up and unused.
/// </para>
/// </summary>
public class UnusedConverterTests
{
    private const string Composition = "src/SysMonitor.App/App.xaml";

    /// <summary>`&lt;converters:FooConverter x:Key="FooConverter"/&gt;` — a registered application resource.</summary>
    private static readonly Regex RegisteredConverter =
        new(@"<converters:(?<type>\w+)\s+x:Key=""(?<key>\w+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryRegisteredConverterIsBoundBySomeMarkup()
    {
        // Read every file once. Re-reading them per converter is 37 x 40 reads for the same bytes.
        var markup = Markup().Select(File.ReadAllText).ToList();
        markup.Should().HaveCountGreaterThan(20,
            "this test is worthless if it cannot find the markup it is meant to search");

        var registrations = RegisteredConverter.Matches(File.ReadAllText(Absolute(Composition))).ToList();
        registrations.Should().HaveCountGreaterThan(20,
            "this test is worthless if it cannot find the converters it is meant to judge");

        var unused = new List<string>();

        foreach (Match registration in registrations)
        {
            var key = registration.Groups["key"].Value;

            // The registration itself lives in App.xaml, so that file's own declaration line must not
            // count as a use. Any {StaticResource key} anywhere — including elsewhere in App.xaml, where
            // a style may legitimately bind one — does count.
            var bound = markup.Any(text =>
                Regex.IsMatch(text, $@"StaticResource\s+{Regex.Escape(key)}\s*\}}"));

            if (!bound)
                unused.Add($"{registration.Groups["type"].Value} — registered in App.xaml, bound by no markup");
        }

        unused.Should().BeEmpty(
            "a converter nothing binds is dead code that still loads with the application");
    }

    /// <summary>Every XAML file in the app: pages, windows, styles and App.xaml itself.</summary>
    private static IEnumerable<string> Markup() =>
        Directory.EnumerateFiles(Absolute("src/SysMonitor.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static string Absolute(string relative) =>
        Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
