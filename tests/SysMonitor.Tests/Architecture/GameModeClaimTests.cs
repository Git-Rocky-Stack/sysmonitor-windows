using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Game Mode does one of two things to a background app: lowers its priority and puts it back on the way
/// out, or sends <c>CloseMainWindow()</c> — the same request as clicking the X — and leaves the process
/// running if it declines. There is no <c>Kill()</c> anywhere in it.
/// <para>
/// The in-app guide said "Kills browsers, Discord, Spotify, etc." and the feature list said "processes
/// killed". A user reading that and clicking the button is being told their unsaved work will be closed by
/// force. It will not be — but the reverse is the dangerous direction, and this test is the thing that
/// notices if the words and the code ever drift apart again.
/// </para>
/// <para>
/// The rule runs both ways. If Game Mode is ever given the power to kill, this test starts failing and the
/// fix is to say so in the guide.
/// </para>
/// </summary>
public class GameModeClaimTests
{
    private const string GameModeFolder = "src/SysMonitor.Core/Services/GameMode";

    private static readonly string[] UserFacingText =
    [
        "src/SysMonitor.App/Views/UserGuidePage.xaml",
        "V2_ENHANCEMENTS.txt",
    ];

    /// <summary>"kills", "killed", "kill " — a claim that something is ended by force.</summary>
    private static readonly Regex KillClaim = new(@"\bkill(s|ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A real call to <c>Process.Kill</c>, not the word in a comment.</summary>
    private static readonly Regex KillCall = new(@"^[^/\r\n]*\.\s*Kill\s*\(", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void TheWordsAboutGameModeMatchWhatGameModeDoes()
    {
        var gameModeSource = string.Join("\n", RepoSource.FilesUnder(GameModeFolder).Select(File.ReadAllText));
        gameModeSource.Should().Contain("CloseMainWindow",
            "this test is worthless if it is not reading the Game Mode source it is meant to judge");

        var gameModeKills = KillCall.IsMatch(gameModeSource);

        var claims = new List<string>();
        foreach (var relative in UserFacingText)
        {
            var path = Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (KillClaim.IsMatch(lines[i]))
                    claims.Add($"{relative}:{i + 1}  {lines[i].Trim()}");
            }
        }

        if (gameModeKills)
        {
            claims.Should().NotBeEmpty(
                "Game Mode now ends processes by force, and the user has to be told before they click it");
        }
        else
        {
            claims.Should().BeEmpty(
                "nothing in Game Mode calls Kill(); it asks a window to close and lets it refuse");
        }
    }
}
