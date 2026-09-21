using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// FEATURES_AND_USER_GUIDE.md is written for the person using the app, not for someone auditing it, so it
/// carries no <c>path:line</c> anchors — a numbered instruction like "Click <b>Enable</b> to restore
/// auto-start" is worse for its reader with a source citation stapled to it. That does not make the claim
/// unverifiable; it moves the verification here, where it is checked on every run instead of by a reader
/// who happens to be curious.
/// <para>
/// This follows <see cref="GameModeClaimTests"/>, and for the same reason: an anchor rots silently, a test
/// fails loudly. Each fact below reads the guide as well as the code, so a claim that is quietly deleted
/// from the guide cannot leave a test passing against nothing.
/// </para>
/// <para>
/// The rule runs both ways. If the code behind one of these sentences is removed, the fact covering it
/// fails and the fix is to correct the guide — not to loosen the assertion.
/// </para>
/// </summary>
public class UserGuideClaimTests
{
    private const string Guide = "FEATURES_AND_USER_GUIDE.md";

    /// <summary>The four capability areas the guide's opening sentence promises, and the folder behind each.</summary>
    private static readonly (string Promise, string Folder)[] OpeningClaims =
    [
        ("real-time hardware monitoring", "src/SysMonitor.Core/Services/Monitors"),
        ("system optimization", "src/SysMonitor.Core/Services/Optimizers"),
        ("privacy protection", "src/SysMonitor.Core/Services/Cleaners"),
        ("productivity tools", "src/SysMonitor.Core/Services/Utilities"),
    ];

    [Fact]
    public void EveryCapabilityTheOverviewPromisesHasAServiceTheAppResolves()
    {
        var composition = Source("src/SysMonitor.App/App.xaml.cs");
        var unbacked = new List<string>();

        foreach (var (promise, folder) in OpeningClaims)
        {
            StillClaimed(promise);

            // A service the container never hands out is a capability the user cannot reach, however many
            // files sit in the folder. The registration is what makes the promise true.
            var registered = RepoSource.FilesUnder(folder)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(type => type is not null && !type.StartsWith("I", StringComparison.Ordinal))
                .Any(type => Regex.IsMatch(composition, $@"Add(Singleton|Transient|Scoped)<\s*{Regex.Escape(type!)}\s*>"));

            if (!registered)
                unbacked.Add($"\"{promise}\" — nothing in {folder} is registered in App.xaml.cs");
        }

        unbacked.Should().BeEmpty(
            "the guide's opening sentence promises these four things to every reader on the first screen");
    }

    [Fact]
    public void TheDashboardShowsTheHealthAndPerformanceTheGuideDescribes()
    {
        StillClaimed("The Dashboard provides a comprehensive overview of your system's health and performance");

        ShouldMatch("src/SysMonitor.App/ViewModels/DashboardViewModel.cs", @"HealthScore\s*=",
            "the guide promises a health overview");
        ShouldMatch("src/SysMonitor.App/ViewModels/DashboardViewModel.cs", @"CpuUsage\s*=",
            "the guide promises a performance overview");
        ShouldMatch("src/SysMonitor.App/ViewModels/DashboardViewModel.cs", @"MemoryUsage\s*=",
            "the guide promises a performance overview");
        ShouldContain("src/SysMonitor.App/Views/DashboardPage.xaml", "ViewModel.HealthScore",
            "a health score the dashboard never binds is one the user never sees");
    }

    [Fact]
    public void TheStartupInstructionMatchesTheButtonTheUserIsToldToClick()
    {
        StillClaimed("Click **Enable** to restore auto-start");

        ShouldMatch("src/SysMonitor.App/Views/StartupPage.xaml", @"Content\s*=\s*""ENABLE""",
            "the guide tells the user to click a button with this label");
        ShouldContain("src/SysMonitor.App/Views/StartupPage.xaml", "ViewModel.EnableItemCommand",
            "a button bound to nothing restores nothing");
        ShouldContain("src/SysMonitor.App/ViewModels/StartupViewModel.cs", "EnableItemAsync",
            "the command the button invokes has to exist behind it");
    }

    [Fact]
    public void TheNotificationsToggleTheGuideDescribesActuallySilencesAlerts()
    {
        StillClaimed("**Show Notifications** - Enable/disable alerts");

        ShouldContain("src/SysMonitor.App/Views/SettingsPage.xaml", "ViewModel.ShowNotifications",
            "the guide describes a setting the user can see and change");

        // The dangerous direction: a toggle that saves a preference nothing reads looks like it works and
        // leaves the alerts coming. Turning it off has to stop an alert, not just record a bool.
        ShouldContain("src/SysMonitor.Core/Services/Alerts/AlertService.cs", "ShowNotifications",
            "the alert service has to read the setting the guide points the user at");
        ShouldMatch("src/SysMonitor.Core/Services/Alerts/AlertService.cs", @"if\s*\(\s*!\s*AreAlertsEnabled\s*\)\s*return",
            "disabling notifications has to actually stop the alert being raised");
    }

    /// <summary>
    /// The guide still says this. Without it a fact could go on passing against a sentence someone deleted,
    /// which is the quiet way a claim test stops testing anything.
    /// </summary>
    private static void StillClaimed(string sentence) =>
        Source(Guide).Contains(sentence, StringComparison.Ordinal).Should().BeTrue(
            $"this fact is worthless if {Guide} has stopped saying \"{sentence}\"");

    /// <summary>
    /// Asserts on a marker in a file, reporting the path rather than the file. FluentAssertions prints the
    /// whole subject on failure, and a 700-line XAML dump buries the one line that matters.
    /// </summary>
    private static void ShouldContain(string relative, string marker, string because) =>
        Source(relative).Contains(marker, StringComparison.Ordinal).Should().BeTrue(
            $"{relative} has to contain \"{marker}\" — {because}");

    private static void ShouldMatch(string relative, string pattern, string because) =>
        Regex.IsMatch(Source(relative), pattern).Should().BeTrue(
            $"{relative} has to match /{pattern}/ — {because}");

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
