using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A <c>System.Diagnostics.Process</c> is not a plain data object. Reading <c>.Handle</c>, <c>.Id</c> or
/// <c>.WorkingSet64</c> makes the runtime call <c>OpenProcess</c>, and the kernel handle that comes back is
/// held until the object is disposed or a finaliser happens to run.
/// <para>
/// This app enumerates every process on the machine on a timer. Dropping a few hundred <c>Process</c> objects
/// on the floor every few seconds is a handle leak that grows for as long as the app is open, and nothing in
/// the UI ever says so — the symptom is a machine that gets slower over a day.
/// </para>
/// <para>
/// The rule: bind what the factory returns to a local, and dispose it in the same method — <c>using</c> for a
/// single process, or a loop that disposes each element of an array. Calling a factory inline, in a
/// <c>foreach</c> header or in the middle of an expression, leaves nothing to dispose.
/// </para>
/// </summary>
public class ProcessHandleTests
{
    private static readonly string[] Folders = ["src/SysMonitor.Core", "src/SysMonitor.App"];

    /// <summary>
    /// The rule judged against code whose verdict is known, before it is trusted to say the repository is
    /// clean. A source-scanning rule that cannot fail is worth nothing, and this one has already been wrong
    /// once: reading only the brace's own line for the type header made every member of a class look like a
    /// single method, so a leak in one method was excused by a <c>Dispose</c> in another.
    /// </summary>
    [Fact]
    public void TheRuleItselfTellsALeakFromADisposal()
    {
        ProcessHandleRule.Leaks(Snippet("using var proc = Process.GetProcessById(id);\n            proc.Refresh();"))
            .Should().BeEmpty("`using` closes it at the end of the scope");

        ProcessHandleRule.Leaks(Snippet("var proc = Process.GetProcessById(id);\n            proc.Refresh();\n            proc.Dispose();"))
            .Should().BeEmpty("it is disposed by hand in the same method");

        ProcessHandleRule.Leaks(Snippet("var all = Process.GetProcesses();\n            foreach (var p in all) p.Dispose();"))
            .Should().BeEmpty("a loop over the array disposes each one");

        ProcessHandleRule.Leaks(Snippet("var proc = Process.GetProcessById(id);\n            proc.Refresh();"))
            .Should().ContainSingle("nothing closes it").Which.Reason.Should().Contain("never disposed");

        ProcessHandleRule.Leaks(Snippet("if (Process.GetProcessesByName(name).Length > 0) return;"))
            .Should().ContainSingle("the array is discarded where it stands").Which.Reason.Should().Contain("not bound");

        ProcessHandleRule.Leaks(Snippet("var any = Process.GetProcessesByName(name).Length > 0;"))
            .Should().ContainSingle("binding the count, not the processes, still leaves every process open");

        // The bug that made this rule silent: a Dispose in a *different* method of the same class.
        ProcessHandleRule.Leaks(TwoMethods)
            .Should().ContainSingle("the disposal is in the other method")
            .Which.Reason.Should().Contain("never disposed");
    }

    [Fact]
    public void EveryProcessTheAppOpens_IsClosedByTheMethodThatOpenedIt()
    {
        var leaks = new List<string>();
        var checkedCalls = 0;

        foreach (var file in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(file);
            checkedCalls += ProcessHandleRule.CallsJudged(source);

            foreach (var leak in ProcessHandleRule.Leaks(source))
                leaks.Add($"{RepoSource.Relative(file)}:{leak.Line} - {leak.Reason}");
        }

        checkedCalls.Should().BeGreaterThan(8,
            "this test is worthless if it cannot find the Process factories it is meant to judge");
        leaks.Should().BeEmpty(
            "each of these holds a kernel handle for as long as the app runs");
    }

    private static string Snippet(string body) => $$"""
        namespace Example;

        public class Holder
        {
            public void Work(int id, string name)
            {
                {{body}}
            }
        }
        """;

    private const string TwoMethods = """
        namespace Example;

        public class Holder
        {
            public void Tidy()
            {
                var all = System.Diagnostics.Process.GetProcesses();
                foreach (var proc in all)
                    proc.Dispose();
            }

            public void Leaky(int id)
            {
                var proc = System.Diagnostics.Process.GetProcessById(id);
                proc.Refresh();
            }
        }
        """;
}
