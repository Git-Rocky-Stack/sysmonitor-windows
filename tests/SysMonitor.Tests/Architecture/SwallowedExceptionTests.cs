using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A cleaner, an uninstall or a backup that fails leaves the user with nothing to look at unless the failure
/// reaches the log in %LocalAppData%\SysMonitor\Logs. An empty catch is how that happens: the exception is
/// caught, discarded, and the operation carries on as if it had worked.
/// <para>
/// Some are deliberate - deleting a temporary file that may already be gone - and those are allowed, on the
/// condition that they say so. A reader can then tell a decision from an oversight, which is the whole point.
/// </para>
/// </summary>
public class SwallowedExceptionTests
{
    private static readonly string[] Folders = ["src/SysMonitor.Core", "src/SysMonitor.App"];

    /// <summary>`catch { }`, `catch (IOException) { }` - a catch whose body is empty.</summary>
    private static readonly Regex EmptyCatch = new(@"catch\s*(\([^)]*\))?\s*\{\s*\}");

    /// <summary>The line before it, or the line itself, saying why nothing is done.</summary>
    private static readonly Regex Excused = new(@"//\s*Best effort:", RegexOptions.IgnoreCase);

    [Fact]
    public void NoFailureIsDiscardedWithoutSayingWhy()
    {
        var unexplained = new List<string>();
        var excused = 0;

        foreach (var file in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(file);

            foreach (Match match in EmptyCatch.Matches(source))
            {
                var line = source.Take(match.Index).Count(c => c == '\n') + 1;

                // The two lines above the catch are where the reason belongs.
                var lines = source.Split('\n');
                var context = string.Join("\n", lines.Skip(Math.Max(0, line - 3)).Take(3));

                if (Excused.IsMatch(context))
                    excused++;
                else
                    unexplained.Add($"{RepoSource.Relative(file)}:{line}");
            }
        }

        unexplained.Should().BeEmpty(
            "a caught exception that is neither logged nor explained is a failure the user never hears about");
        excused.Should().BeGreaterThan(0, "this test is worthless if it cannot find the catches it is meant to judge");
    }
}
