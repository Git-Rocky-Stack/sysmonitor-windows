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

    /// <summary>
    /// The Game Mode page lists the applications it acts on, and captioned that list "These apps will be
    /// closed when Game Mode is enabled". Neither path closes them unconditionally: the default action is
    /// <see cref="BackgroundAppAction.LowerPriority"/>, which only moves them down the processor queue,
    /// and the opt-in path asks and accepts a refusal. A user reading that caption and clicking Enable
    /// was told their browser was about to be shut.
    /// <para>
    /// The dangerous direction is the caption promising less than the code does, so this fact reads both:
    /// the default that ships, and the words above the list.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTargetApplicationListSaysWhatHappensToTheAppsOnIt()
    {
        var options = Read("src/SysMonitor.Core/Services/GameMode/IGameModeService.cs");
        var page = Read("src/SysMonitor.App/Views/GameModePage.xaml");

        // The claim below is only correct while lowering priority is what happens unless you opt in.
        options.Should().MatchRegex(
            @"BackgroundApps\s*\{\s*get;\s*init;\s*\}\s*=\s*BackgroundAppAction\.LowerPriority",
            "this fact describes the default, so it has to read the default rather than assume it");

        page.Should().NotMatchRegex(@"apps will be closed when Game Mode is enabled",
            "the default lowers priority and the opt-in only asks, so nothing here closes an app outright");

        page.Should().Contain("Their priority is lowered",
            "the caption above the list has to name what actually happens to the apps on it");
        page.Should().Contain("anything that declines keeps running",
            "the opt-in path asks, and an app that refuses is left alone; the caption has to say so");
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
