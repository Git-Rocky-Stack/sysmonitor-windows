using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A cleaner, an uninstall or a backup that fails leaves the user with nothing to look at unless the failure
/// reaches the log in %LocalAppData%\SysMonitor\Logs. A catch that does nothing is how that happens: the
/// exception is caught, discarded, and the operation carries on as if it had worked.
/// <para>
/// Some are deliberate — deleting a temporary file that may already be gone — and those are allowed, on the
/// condition that they say so with <c>// Best effort:</c> and a reason. A reader can then tell a decision
/// from an oversight, which is the whole point.
/// </para>
/// <para>
/// This rule used to look for <c>catch (…) { }</c> with a literally empty body. It found 6. A body holding
/// nothing but <c>// Log in production</c> discards the exception exactly as completely, and there were 83
/// more of those — every one invisible to the rule that was supposed to be guarding against them.
/// </para>
/// </summary>
public class SwallowedExceptionTests
{
    private static readonly string[] Folders = ["src/SysMonitor.Core", "src/SysMonitor.App"];

    /// <summary>
    /// The rule judged against code whose verdict is known, before it is trusted to say the repository is
    /// clean. The third case is the one the old rule could not see.
    /// </summary>
    [Fact]
    public void TheRuleSeesACommentOnlyCatchAsClearlyAsAnEmptyOne()
    {
        SwallowedExceptionRule.Unexplained(Catching("_logger.LogDebug(ex, \"it failed\");"))
            .Should().BeEmpty("the failure reaches the log");

        SwallowedExceptionRule.Unexplained(Catching("return false;"))
            .Should().BeEmpty("the failure is turned into an answer the caller gets");

        SwallowedExceptionRule.Unexplained(Catching("// Log in production"))
            .Should().ContainSingle("a note about logging is not logging");

        SwallowedExceptionRule.Unexplained(Catching(""))
            .Should().ContainSingle("nothing at all");

        SwallowedExceptionRule.Unexplained(Catching("// Best effort: the file may already be gone."))
            .Should().BeEmpty("it says why, so a reader can check the decision");

        SwallowedExceptionRule.Excusals(Catching("// Best effort: the file may already be gone."))
            .Should().Be(1);

        // The one-line form has no room inside the body, so the marker goes directly above it.
        const string OneLiner = """
            namespace Example;

            public class Sample
            {
                public void Tidy(string path)
                {
                    // Best effort: the file may already be gone.
                    try { File.Delete(path); } catch (IOException) { }
                }
            }
            """;
        SwallowedExceptionRule.Unexplained(OneLiner).Should().BeEmpty();
        SwallowedExceptionRule.Excusals(OneLiner).Should().Be(1);

        SwallowedExceptionRule.Unexplained(OneLiner.Replace("// Best effort: the file may already be gone.", ""))
            .Should().ContainSingle("with the marker gone there is nothing saying why");
    }

    [Fact]
    public void NoFailureIsDiscardedWithoutSayingWhy()
    {
        var unexplained = new List<string>();
        var excused = 0;
        var judged = 0;

        foreach (var file in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(file);
            excused += SwallowedExceptionRule.Excusals(source);
            judged += SwallowedExceptionRule.CatchesJudged(source);

            foreach (var finding in SwallowedExceptionRule.Unexplained(source))
                unexplained.Add($"{RepoSource.Relative(file)}:{finding.Line}  {finding.Body}");
        }

        judged.Should().BeGreaterThan(100,
            "this test is worthless if it cannot find the catches it is meant to judge");
        excused.Should().BeGreaterThan(0,
            "some silent catches are deliberate, and this test only means anything if it recognises them");
        unexplained.Should().BeEmpty(
            "a caught exception that is neither acted on nor explained is a failure the user never hears about");
    }

    private static string Catching(string body) => $$"""
        namespace Example;

        public class Sample
        {
            public bool Work()
            {
                try
                {
                    return Risky();
                }
                catch (Exception ex)
                {
                    {{body}}
                }

                return true;
            }
        }
        """;
}
