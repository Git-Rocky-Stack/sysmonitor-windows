using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A background loop paced by a <see cref="PeriodicTimer"/> is the app's way of saying "do this every so
/// often". The pacing only holds while the wait actually runs.
/// <para>
/// Put the wait in the same <c>try</c> as the work and a single throw skips it: the <c>catch</c> swallows,
/// the <c>while</c> comes straight back round, and the loop runs flat out for the rest of the session. The
/// symptom is not an error - it is a core at 100% and a machine the user thinks is broken.
/// </para>
/// <para>
/// Two shapes are safe: the wait as the loop condition, <c>while (await timer.WaitForNextTickAsync(ct))</c>,
/// where no catch can skip it; or the wait alone in its own <c>try</c>, after the work's.
/// </para>
/// </summary>
public class TimerLoopTests
{
    private static readonly string[] Folders = ["src/SysMonitor.Core", "src/SysMonitor.App"];

    private const string Wait = "WaitForNextTickAsync";

    [Fact]
    public void NoTimerWaitCanBeSkippedByTheCatchThatFollowsIt()
    {
        var skippable = new List<string>();
        var checkedWaits = 0;

        foreach (var file in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(file);

            for (var at = source.IndexOf(Wait, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(Wait, at + Wait.Length, StringComparison.Ordinal))
            {
                checkedWaits++;

                // `while (await timer.WaitForNextTickAsync(ct))` - the wait is the condition, so it always runs.
                if (IsLoopCondition(source, at))
                    continue;

                // Otherwise it sits in a block. If the work awaited before it in that same block, a throw
                // from the work jumps past this wait and the loop spins. The wait's own `await` is part of
                // the wait, not work done before it, so the search stops short of it.
                var blockStart = EnclosingBlockStart(source, at);
                var ownAwait = source.LastIndexOf("await ", at, StringComparison.Ordinal);
                if (blockStart >= 0 && ownAwait > blockStart &&
                    source[blockStart..ownAwait].Contains("await ", StringComparison.Ordinal))
                    skippable.Add($"{RepoSource.Relative(file)}:{LineOf(source, at)}");
            }
        }

        checkedWaits.Should().BeGreaterThan(5,
            "this test is worthless if it cannot find the timer loops it is meant to judge");
        skippable.Should().BeEmpty(
            "a throw from the work would skip these waits and leave the loop spinning at full speed");
    }

    /// <summary>
    /// `while (await timer.WaitForNextTickAsync(token))` ends in one of two ways. The timer being disposed
    /// returns <c>false</c> and falls out of the loop; the token being cancelled <b>throws</b>
    /// <see cref="OperationCanceledException"/> straight out of the condition.
    /// <para>
    /// Cancellation is how this app stops its loops, so anything written after such a loop - the final
    /// flush that saves what is still queued - does not run on the only path that reaches it. The work is
    /// lost every shutdown and nothing says so. Cleanup belongs in a <c>finally</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void NoCleanupSitsAfterALoopThatCancellationLeavesByThrowing()
    {
        var unreachable = new List<string>();
        var checkedLoops = 0;

        foreach (var file in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(file);

            for (var at = source.IndexOf(Wait, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(Wait, at + Wait.Length, StringComparison.Ordinal))
            {
                if (!IsLoopCondition(source, at))
                    continue;

                checkedLoops++;

                var bodyStart = source.IndexOf('{', at);
                var afterBody = EndOfBlock(source, bodyStart);
                if (afterBody < 0)
                    continue;

                if (NextMeaningfulCharacter(source, afterBody) is not ('}' or '\0'))
                    unreachable.Add($"{RepoSource.Relative(file)}:{LineOf(source, afterBody)}");
            }
        }

        checkedLoops.Should().BeGreaterThan(5,
            "this test is worthless if it cannot find the timer loops it is meant to judge");
        unreachable.Should().BeEmpty(
            "cancellation throws out of the loop condition, so this code never runs on the path it was written for");
    }

    /// <summary>The index just past the `}` closing the block that opens at <paramref name="openBrace"/>.</summary>
    private static int EndOfBlock(string source, int openBrace)
    {
        if (openBrace < 0) return -1;

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return i + 1;
        }

        return -1;
    }

    /// <summary>The next character that is not whitespace or a comment, or '\0' at end of file.</summary>
    private static char NextMeaningfulCharacter(string source, int from)
    {
        for (var i = from; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i])) continue;

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var lineEnd = source.IndexOf('\n', i);
                if (lineEnd < 0) return '\0';
                i = lineEnd;
                continue;
            }

            return source[i];
        }

        return '\0';
    }

    /// <summary>True when the nearest thing before the call is `while (await `, so the wait paces the loop.</summary>
    private static bool IsLoopCondition(string source, int at)
    {
        var lineStart = source.LastIndexOf('\n', at) + 1;
        return source[lineStart..at].TrimStart().StartsWith("while (await", StringComparison.Ordinal);
    }

    /// <summary>The index just past the `{` of the innermost block containing <paramref name="at"/>.</summary>
    private static int EnclosingBlockStart(string source, int at)
    {
        var depth = 0;
        for (var i = at; i >= 0; i--)
        {
            if (source[i] == '}') depth++;
            else if (source[i] == '{')
            {
                if (depth == 0) return i + 1;
                depth--;
            }
        }

        return -1;
    }

    private static int LineOf(string source, int at) => source.Take(at).Count(c => c == '\n') + 1;
}
