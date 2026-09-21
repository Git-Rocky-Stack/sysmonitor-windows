using System.Text.Json;
using FluentAssertions;
using Moq;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Auto Game Mode is a setting that survives a restart, so the toggle showing ON after one has to mean the
/// service is actually watching for games. It did not: the saved value was written straight into the backing
/// field, which skipped the property setter — the only thing that ever starts the watch. The toggle read ON
/// and nothing was ever detected until the user turned it off and on again.
/// </summary>
public class AutoGameModeStartupTests : IDisposable
{
    private readonly TempDirectory _temp = new("auto-game-mode");
    private readonly List<IAutoGameModeService> _services = [];

    public void Dispose()
    {
        foreach (var service in _services)
            service.StopMonitoringAsync().GetAwaiter().GetResult();

        _temp.Dispose();
    }

    [Fact]
    public async Task AutoModeLeftOn_IsWatchingForGamesAfterARestart()
    {
        var settings = SettingsFile(autoGameModeEnabled: true);

        var service = Service(settings);

        service.AutoModeEnabled.Should().BeTrue("the saved setting is what the toggle shows");
        await WaitUntilAsync(() => service.IsMonitoring);
        service.IsMonitoring.Should().BeTrue("a toggle that reads ON has to mean games are being watched for");
    }

    [Fact]
    public void AutoModeLeftOff_IsNotWatchingForGames()
    {
        var settings = SettingsFile(autoGameModeEnabled: false);

        var service = Service(settings);

        service.AutoModeEnabled.Should().BeFalse();
        service.IsMonitoring.Should().BeFalse("nothing was asked for, so nothing is polled");
    }

    [Fact]
    public void NoSettingsFileAtAll_IsNotWatchingForGames()
    {
        var service = Service(Path.Combine(_temp.Path, "does-not-exist.json"));

        service.AutoModeEnabled.Should().BeFalse();
        service.IsMonitoring.Should().BeFalse("a first run does not opt the user in");
    }

    [Fact]
    public void UnreadableSettings_LeaveAutoModeOffRatherThanGuessing()
    {
        var settings = Path.Combine(_temp.Path, "broken.json");
        File.WriteAllText(settings, "{ this is not json");

        var service = Service(settings);

        service.AutoModeEnabled.Should().BeFalse();
        service.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public async Task TurningAutoModeOff_StopsTheWatchItStartedAtLaunch()
    {
        var service = Service(SettingsFile(autoGameModeEnabled: true));
        await WaitUntilAsync(() => service.IsMonitoring);
        service.IsMonitoring.Should().BeTrue("the test is worthless if nothing was running to stop");

        service.AutoModeEnabled = false;
        await WaitUntilAsync(() => !service.IsMonitoring);

        service.IsMonitoring.Should().BeFalse();
    }

    // ---------------------------------------------------------------- helpers

    private string SettingsFile(bool autoGameModeEnabled)
    {
        var path = Path.Combine(_temp.Path, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["AutoGameModeEnabled"] = autoGameModeEnabled,
        }));
        return path;
    }

    private IAutoGameModeService Service(string settingsPath)
    {
        // The power plan and the background apps are someone else's tests; this one is about the watch
        // starting at all, so Game Mode itself is a stand-in that does nothing.
        var gameMode = new Mock<IGameModeService>();
        gameMode.SetupGet(g => g.IsEnabled).Returns(false);

        var service = new AutoGameModeService(gameMode.Object, settingsPath);
        _services.Add(service);
        return service;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(25);
    }
}
