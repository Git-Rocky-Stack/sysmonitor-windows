using System.Text.Json;
using FluentAssertions;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Settings;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The settings used to live in two places. The Settings page saved to LocalSettings in the packaged build and
/// to settings.json otherwise, while the alert service, the minimize-to-tray check and Auto Game Mode read
/// settings.json alone - so in the packaged build a toggle on that page was saved and then ignored. In the
/// unpackaged build the page wrote the whole file back from a copy it took when it opened, erasing anything
/// Auto Game Mode had saved in the meantime.
/// <para>
/// <see cref="SettingsStore"/> is now the one place, shared by every reader and writer. These tests pin down
/// what that has to mean: what one part of the app writes is what the others read; a save lays its own
/// changes over the file and leaves everyone else's alone; a file an earlier version wrote reads the same;
/// and what an earlier packaged version kept in LocalSettings is carried across, once.
/// </para>
/// </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new("settings");
    private readonly Mock<ITemperatureMonitor> _temperature = new();
    private readonly Mock<IMemoryMonitor> _memory = new();
    private readonly Mock<IBatteryMonitor> _battery = new();
    private readonly List<IDisposable> _services = new();

    private double _cpuTemperature = 40;

    public SettingsStoreTests()
    {
        _temperature.Setup(t => t.GetCpuTemperatureAsync()).ReturnsAsync(() => _cpuTemperature);
        _temperature.Setup(t => t.GetGpuTemperatureAsync()).ReturnsAsync(0);
        _memory.Setup(m => m.GetMemoryInfoAsync()).ReturnsAsync(new MemoryInfo { UsagePercent = 10 });
        _battery.Setup(b => b.GetBatteryInfoAsync()).ReturnsAsync((BatteryInfo?)null);
    }

    public void Dispose()
    {
        foreach (var service in _services)
            service.Dispose();
        _temp.Dispose();
    }

    private string SettingsPath => Path.Combine(_temp.Path, "settings.json");

    // ---------------------------------------------------------------- every reader sees what was saved

    [Fact]
    public void TheAlertServiceReadsWhatTheSettingsPageWroteThroughTheStore()
    {
        var store = new SettingsStore(SettingsPath);
        var alerts = Alerts(settings: store);
        alerts.AreAlertsEnabled.Should().BeTrue("notifications are on until someone turns them off");

        // What the Settings page does when Show Notifications is switched off.
        store.Set("ShowNotifications", false);
        alerts.AreAlertsEnabled.Should().BeFalse("every reader sees a value the moment it is set");

        store.Save().Should().BeTrue();
        alerts.AreAlertsEnabled.Should().BeFalse();
        new SettingsStore(SettingsPath).Get("ShowNotifications", true).Should().BeFalse(
            "a save puts it on disk, where the next start finds it");
    }

    [Fact]
    public async Task TheAlertThresholdsAreTheOnesTheSettingsPageSaved()
    {
        var store = new SettingsStore(SettingsPath);
        var alerts = Alerts(settings: store);
        var fired = new List<AlertNotification>();
        alerts.AlertTriggered += (_, notification) => fired.Add(notification);

        _cpuTemperature = 65;
        await alerts.CheckThresholdsAsync();
        fired.Should().BeEmpty("65 degrees is under the default warning of 75");

        store.Set("CpuTempWarning", 60);
        store.Save().Should().BeTrue();

        await alerts.CheckThresholdsAsync();
        fired.Should().ContainSingle(alert => alert.Type == AlertType.CpuTempWarning,
            "the threshold the page saved is the one the alert service measures against");
    }

    [Fact]
    public void AnAlertServiceStartedLaterReadsWhatWasSavedBefore()
    {
        var store = new SettingsStore(SettingsPath);
        store.Set("ShowNotifications", false);
        store.Save().Should().BeTrue();

        // The next start, reaching the same file through the service's own test seam.
        Alerts(settingsPath: SettingsPath).AreAlertsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AReaderOnAnotherStoreSeesASaveThatLeftTheTimestampWhereItWas()
    {
        var writer = new SettingsStore(SettingsPath);
        writer.Set("MinimizeToTray", false);
        writer.Save().Should().BeTrue();

        var reader = new SettingsStore(SettingsPath);
        reader.Get("MinimizeToTray", true).Should().BeFalse();

        // A second save inside the same tick of the clock that stamps the file leaves the stamp where the first
        // one put it - about 16 ms on NTFS, 2 s on FAT. Putting it back by hand makes that certain here, on any
        // file system: a reader that trusted the stamp alone would never see this save.
        var stamp = File.GetLastWriteTimeUtc(SettingsPath);
        writer.Set("MinimizeToTray", true);
        writer.Save().Should().BeTrue();
        File.SetLastWriteTimeUtc(SettingsPath, stamp);

        reader.Get("MinimizeToTray", false).Should().BeTrue("the second save is as real as the first");
    }

    // ---------------------------------------------------------------- no writer erases another

    [Fact]
    public async Task ACustomGameAddedWhileTheSettingsPageIsOpenSurvivesItsSave()
    {
        var store = new SettingsStore(SettingsPath);

        // The Settings page opens and reads everything it shows.
        store.Get("ShowNotifications", true);

        // Meanwhile Auto Game Mode, sharing the store, is given a game to watch for.
        var autoGameMode = AutoGameMode(settings: store);
        await autoGameMode.AddCustomGameAsync("mygame", "My Game");

        // Then Save is pressed on the page.
        store.Set("ShowNotifications", false);
        store.Save().Should().BeTrue();

        var reread = new SettingsStore(SettingsPath);
        reread.Get<List<GameDefinition>?>("CustomGames", null).Should()
            .ContainSingle(game => game.ProcessName == "mygame", "the page's save wrote its own keys, not a stale copy of the file");
        reread.Get("ShowNotifications", true).Should().BeFalse();
    }

    [Fact]
    public void TheLastAutoGameModeChangeBeforeShutdownIsTheOneOnDisk()
    {
        // Every write held back a little, as a busy disk would, so a shutdown that did not wait would be seen.
        var store = new SlowSaves(new SettingsStore(SettingsPath), TimeSpan.FromMilliseconds(400));
        var autoGameMode = AutoGameMode(settings: store);

        // Three flicks of the switch, each saved on a pool thread without anyone waiting for it, and then the
        // app closes. The writes must land in the order the changes were made, and before the service is gone.
        autoGameMode.AutoModeEnabled = true;
        autoGameMode.AutoModeEnabled = false;
        autoGameMode.AutoModeEnabled = true;
        autoGameMode.Dispose();

        new SettingsStore(SettingsPath).Get("AutoGameModeEnabled", false).Should().BeTrue(
            "the switch was left on, so that is what the next start has to find");
    }

    [Fact]
    public void ASaveKeepsWhatAnotherWriterSavedAfterThisStoreLastReadTheFile()
    {
        var page = new SettingsStore(SettingsPath);
        page.Get("ThemeIndex", 2);

        // Another process - or this one before a restart - saves a key the page never touches.
        var other = new SettingsStore(SettingsPath);
        other.Set("AutoGameModeEnabled", true);
        other.Save().Should().BeTrue();

        page.Set("ThemeIndex", 1);
        page.Save().Should().BeTrue();

        var reread = new SettingsStore(SettingsPath);
        reread.Get("AutoGameModeEnabled", false).Should().BeTrue("a save lays its own changes over the file");
        reread.Get("ThemeIndex", 2).Should().Be(1);
    }

    [Fact]
    public async Task ManyThreadsReadingAndWritingOneStoreLoseNothing()
    {
        var store = new SettingsStore(SettingsPath);

        // The UI thread, the alert timer and Auto Game Mode's pool thread all share the one store.
        var workers = Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                store.Set($"Worker{worker}", i);
                store.Get($"Worker{(worker + 1) % 16}", -1);
                if (i % 5 == 4)
                    store.Save();
            }
        }));

        await Task.WhenAll(workers);
        store.Save().Should().BeTrue();

        var reread = new SettingsStore(SettingsPath);
        Enumerable.Range(0, 16).Select(worker => reread.Get($"Worker{worker}", -1))
            .Should().AllSatisfy(value => value.Should().Be(24));
    }

    // ---------------------------------------------------------------- what is on disk

    [Fact]
    public void AFileAnEarlierVersionWroteReadsTheSame()
    {
        // The shape the Settings page and Auto Game Mode used to write.
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ThemeIndex"] = 1,
            ["MinimizeToTray"] = false,
            ["RefreshInterval"] = 5,
            ["AutoGameModeEnabled"] = true,
            ["CustomGames"] = new List<GameDefinition> { new() { ProcessName = "mygame", DisplayName = "My Game", IsCustom = true } },
        }, new JsonSerializerOptions { WriteIndented = true }));

        var store = new SettingsStore(SettingsPath);

        store.Get("ThemeIndex", 2).Should().Be(1);
        store.Get("MinimizeToTray", true).Should().BeFalse();
        store.Get("RefreshInterval", 2).Should().Be(5);
        store.Get("AutoGameModeEnabled", false).Should().BeTrue();
        store.Get<List<GameDefinition>?>("CustomGames", null).Should().ContainSingle(game => game.DisplayName == "My Game");
        store.Get("CpuTempWarning", 75).Should().Be(75, "a key that was never saved reads as its default");
    }

    [Fact]
    public void AValueThatDoesNotReadAsTheTypeAskedForIsItsDefault()
    {
        _temp.File("settings.json", """{ "CpuTempWarning": "hot", "MinimizeToTray": 1, "RefreshInterval": 2.5 }""");

        var store = new SettingsStore(SettingsPath);

        store.Get("CpuTempWarning", 75).Should().Be(75);
        store.Get("MinimizeToTray", true).Should().BeTrue();
        store.Get("RefreshInterval", 2).Should().Be(2);
    }

    [Fact]
    public void AFileThatIsNotSettingsReadsAsDefaultsAndTheNextSaveReplacesIt()
    {
        _temp.File("settings.json", "{ this is not JSON");

        var store = new SettingsStore(SettingsPath);
        store.Get("ShowNotifications", true).Should().BeTrue();

        store.Set("ShowNotifications", false);
        store.Save().Should().BeTrue();

        using var written = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        written.RootElement.GetProperty("ShowNotifications").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void AFileThatCannotBeReadIsNeverWrittenOver()
    {
        const string Saved = """{ "CustomGames": [ { "ProcessName": "mygame", "DisplayName": "My Game" } ] }""";
        _temp.File("settings.json", Saved);
        var store = new SettingsStore(SettingsPath);
        store.Set("ShowNotifications", false);

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Save().Should().BeFalse(
                "a save over a file whose contents it could not read would erase whatever is in it");
        }

        File.ReadAllText(SettingsPath).Should().Be(Saved);

        // Nothing was lost on this side either: the change is kept, and goes in with the next save.
        store.Save().Should().BeTrue();
        var reread = new SettingsStore(SettingsPath);
        reread.Get("ShowNotifications", true).Should().BeFalse();
        reread.Get<List<GameDefinition>?>("CustomGames", null).Should().ContainSingle();
    }

    [Fact]
    public void ClearingResetsEverySettingAndASaveMakesItLast()
    {
        var store = new SettingsStore(SettingsPath);
        store.Set("ShowNotifications", false);
        store.Set("CpuTempWarning", 60);
        store.Save().Should().BeTrue();

        store.Clear();
        store.Get("ShowNotifications", true).Should().BeTrue("a cleared setting reads as its default");
        store.Get("CpuTempWarning", 75).Should().Be(75);

        store.Save().Should().BeTrue();
        var reread = new SettingsStore(SettingsPath);
        reread.Get("ShowNotifications", true).Should().BeTrue();
        reread.Get("CpuTempWarning", 75).Should().Be(75);
    }

    [Fact]
    public void ReadingNeverCreatesTheFile()
    {
        var store = new SettingsStore(SettingsPath, readPackageSettings: () => null, clearPackageSettings: null, logger: null);

        store.Get("ShowNotifications", true).Should().BeTrue();

        File.Exists(SettingsPath).Should().BeFalse("unpackaged, there is nothing to carry across and nothing to write");
    }

    // ---------------------------------------------------------------- LocalSettings, from earlier packaged versions

    [Fact]
    public void WhatAnEarlierPackagedVersionKeptInLocalSettingsIsCarriedAcrossOnce()
    {
        // Before this store, the packaged build's Settings page wrote LocalSettings and Auto Game Mode wrote the file.
        _temp.File("settings.json", """{ "AutoGameModeEnabled": true }""");
        var localSettings = new Dictionary<string, object>
        {
            ["ShowNotifications"] = false,
            ["CpuTempWarning"] = 65,
            ["AutoGameModeEnabled"] = false,
        };
        var reads = 0;

        IReadOnlyDictionary<string, object> ReadLocalSettings()
        {
            reads++;
            return localSettings;
        }

        var store = new SettingsStore(SettingsPath, ReadLocalSettings, localSettings.Clear, logger: null);

        store.Get("ShowNotifications", true).Should().BeFalse();
        store.Get("CpuTempWarning", 75).Should().Be(65);
        store.Get("AutoGameModeEnabled", false).Should().BeTrue("what the file already holds is kept");

        // The next start finds all of it in the file, and does not go back to LocalSettings for more.
        localSettings["MinimizeToTray"] = false;
        var nextStart = new SettingsStore(SettingsPath, ReadLocalSettings, localSettings.Clear, logger: null);

        nextStart.Get("CpuTempWarning", 75).Should().Be(65);
        nextStart.Get("MinimizeToTray", true).Should().BeTrue("LocalSettings is read on one start and no other");
        reads.Should().Be(1);
    }

    [Fact]
    public void ClearingInThePackagedBuildEmptiesLocalSettingsAndDoesNotInviteItBack()
    {
        var localSettings = new Dictionary<string, object> { ["CpuTempWarning"] = 65 };
        var store = new SettingsStore(SettingsPath, () => localSettings, localSettings.Clear, logger: null);
        store.Get("CpuTempWarning", 75).Should().Be(65, "the test is worthless if nothing was carried across");

        store.Clear();
        store.Save().Should().BeTrue();

        localSettings.Should().BeEmpty("no copy of the cleared settings is left behind");

        var stale = new Dictionary<string, object> { ["CpuTempWarning"] = 65 };
        var nextStart = new SettingsStore(SettingsPath, () => stale, stale.Clear, logger: null);
        nextStart.Get("CpuTempWarning", 75).Should().Be(75, "LocalSettings was taken over once; a clear does not reopen it");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A store whose saves take a while to reach the disk.</summary>
    private sealed class SlowSaves(ISettingsStore inner, TimeSpan delay) : ISettingsStore
    {
        public T Get<T>(string key, T defaultValue) => inner.Get(key, defaultValue);

        public void Set<T>(string key, T value) => inner.Set(key, value);

        public void Clear() => inner.Clear();

        public bool Save()
        {
            Thread.Sleep(delay);
            return inner.Save();
        }
    }

    private AlertService Alerts(ISettingsStore? settings = null, string? settingsPath = null) =>
        new(Mock.Of<ICpuMonitor>(), _memory.Object, _temperature.Object, _battery.Object,
            logger: null, settingsPath: settingsPath, settings: settings);

    private AutoGameModeService AutoGameMode(ISettingsStore settings)
    {
        var gameMode = new Mock<IGameModeService>();
        gameMode.SetupGet(g => g.IsEnabled).Returns(false);

        var service = new AutoGameModeService(gameMode.Object, settings: settings);
        _services.Add(service);
        return service;
    }
}
