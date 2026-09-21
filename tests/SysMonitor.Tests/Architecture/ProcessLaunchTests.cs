using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Rules about how this app starts other programs. Both are cases where the code reads as though it does
/// something it does not do, which is the kind of mistake that survives review.
/// </summary>
public class ProcessLaunchTests
{
    private static readonly string[] Folders = ["src/SysMonitor.Core", "src/SysMonitor.App"];

    /// <summary>`new ProcessStartInfo` up to the `{` that opens its initialiser.</summary>
    private static readonly Regex StartInfo = new(@"new (System\.Diagnostics\.)?ProcessStartInfo\s*(\r?\n\s*)?\{");

    [Fact]
    public void NoProcessAsksForElevationItCannotGet()
    {
        var pretending = new List<string>();
        var checkedBlocks = 0;

        foreach (var (file, block, line) in StartInfoBlocks())
        {
            checkedBlocks++;

            var asksToElevate = block.Contains("Verb", StringComparison.Ordinal);
            var cannot = Regex.IsMatch(block, @"UseShellExecute\s*=\s*false");

            if (asksToElevate && cannot)
                pretending.Add($"{file}:{line}");
        }

        checkedBlocks.Should().BeGreaterThan(5,
            "this test is worthless if it cannot find the process launches it is meant to judge");
        pretending.Should().BeEmpty(
            "Verb is only honoured by ShellExecuteEx; with UseShellExecute = false the process starts " +
            "unelevated and the elevation the code appears to ask for never happens");
    }

    /// <summary>
    /// `powershell -Command "... '{value}' ..."` hands the value to a parser, not to a program. A single
    /// quote inside it closes the literal and whatever follows is another statement, running wherever the
    /// process runs. Constant command lines are fine; a value belongs in ArgumentList or -EncodedCommand.
    /// </summary>
    [Fact]
    public void NoPowerShellCommandLineIsBuiltFromAValue()
    {
        var interpolated = new List<string>();

        foreach (var (file, block, line) in StartInfoBlocks())
        {
            foreach (Match argument in Regex.Matches(block, @"Arguments\s*=\s*\$""(?<text>(\\.|[^""\\])*)"""))
            {
                var text = argument.Groups["text"].Value;
                if (text.Contains("-Command", StringComparison.Ordinal) && text.Contains('{'))
                    interpolated.Add($"{file}:{line}");
            }
        }

        interpolated.Should().BeEmpty(
            "a value interpolated into a PowerShell -Command string can close the quote it sits in and " +
            "append a statement of its own");
    }

    /// <summary>Every `new ProcessStartInfo { … }` initialiser in the app, with the file and line it starts on.</summary>
    private static IEnumerable<(string File, string Block, int Line)> StartInfoBlocks()
    {
        foreach (var path in Folders.SelectMany(RepoSource.FilesUnder))
        {
            var source = File.ReadAllText(path);

            foreach (Match match in StartInfo.Matches(source))
            {
                var openBrace = source.IndexOf('{', match.Index);
                var close = EndOfBlock(source, openBrace);
                if (close < 0) continue;

                yield return (
                    RepoSource.Relative(path),
                    source[openBrace..close],
                    source.Take(match.Index).Count(c => c == '\n') + 1);
            }
        }
    }

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
}
