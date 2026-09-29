using System.Diagnostics;
using FluentAssertions;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Game Mode used to close browsers, Teams, Discord and OneDrive and kill whatever had not gone a second
/// later. Nothing here may kill anything: the background apps in these tests are real processes, and every
/// one of them is expected to still be running at the end. The power plan is a stand-in, so this machine's
/// own plan is never touched.
/// </summary>
public class GameModeTests : IDisposable
{
    private const string AppName = "ping";

    private readonly TempDirectory _temp = new("game-mode");
    private readonly List<Process> _started = [];
    private readonly FakePowerPlans _powerPlans = new();

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // It has already gone.
            }

            process.Dispose();
        }

        _temp.Dispose();
    }

    [Fact]
    public async Task ABackgroundAppIsMovedOutOfTheWayAndNeverClosed()
    {
        var app = StartBackgroundApp();
        var service = Service();

        var result = await service.EnableAsync(Options(BackgroundAppAction.LowerPriority));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.BackgroundAppsAffected.Should().Contain(AppName);
        Refreshed(app).PriorityClass.Should().Be(ProcessPriorityClass.BelowNormal);
        app.HasExited.Should().BeFalse("nothing is ever closed to make room for a game");
    }

    [Fact]
    public async Task EndingGameModePutsThePriorityBack()
    {
        var app = StartBackgroundApp();
        var before = Refreshed(app).PriorityClass;
        var service = Service();
        await service.EnableAsync(Options(BackgroundAppAction.LowerPriority));

        await service.DisableAsync();

        Refreshed(app).PriorityClass.Should().Be(before);
        app.HasExited.Should().BeFalse();
    }

    [Fact]
    public async Task AnAppThatWillNotCloseIsLeftRunningAndSaidToBeRunning()
    {
        // A console process has no window to ask, which is exactly the case the old code killed outright.
        var app = StartBackgroundApp();
        var service = Service();

        var result = await service.EnableAsync(Options(BackgroundAppAction.AskToClose) with { CloseTimeout = TimeSpan.FromSeconds(1) });

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.BackgroundAppsStillRunning.Should().Contain(AppName);
        result.BackgroundAppsAffected.Should().NotContain(AppName);
        app.HasExited.Should().BeFalse("it would not close, so it was left alone");
    }

    [Fact]
    public async Task LeavingThemAloneTouchesNothing()
    {
        var app = StartBackgroundApp();
        var before = Refreshed(app).PriorityClass;
        var service = Service();

        var result = await service.EnableAsync(Options(BackgroundAppAction.LeaveAlone));

        result.BackgroundAppsAffected.Should().BeEmpty();
        result.BackgroundAppsStillRunning.Should().BeEmpty("nothing was asked of them at all");
        Refreshed(app).PriorityClass.Should().Be(before);
        app.HasExited.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyTheAppsTheProfileNamesAreTouched()
    {
        var app = StartBackgroundApp();
        var service = Service();

        // A profile that keeps this app - "Streaming Mode" keeps Discord and OBS the same way.
        var result = await service.EnableAsync(Options(BackgroundAppAction.LowerPriority) with
        {
            BackgroundAppNames = ["some-app-that-is-not-running"],
        });

        result.BackgroundAppsAffected.Should().BeEmpty();
        Refreshed(app).PriorityClass.Should().Be(ProcessPriorityClass.Normal, "this app was not on the profile's list");
    }

    [Fact]
    public async Task ThePowerPlanIsPutBackWhenGameModeEnds()
    {
        _powerPlans.Active = "381b4222-f694-41f0-9685-ff5bb260df2e";
        var service = Service();

        await service.EnableAsync(Options(BackgroundAppAction.LeaveAlone));
        _powerPlans.Active.Should().Be(GameModeService.HighPerformanceGuid);

        await service.DisableAsync();

        _powerPlans.Active.Should().Be("381b4222-f694-41f0-9685-ff5bb260df2e");
    }

    [Fact]
    public async Task ThePowerPlanIsPutBackWhenTheAppClosesWithGameModeOn()
    {
        _powerPlans.Active = "381b4222-f694-41f0-9685-ff5bb260df2e";
        var service = Service();
        await service.EnableAsync(Options(BackgroundAppAction.LeaveAlone));

        service.Dispose();

        _powerPlans.Active.Should().Be("381b4222-f694-41f0-9685-ff5bb260df2e");
    }

    [Fact]
    public async Task ThePowerPlanIsPutBackOnTheNextStartAfterACrash()
    {
        _powerPlans.Active = "381b4222-f694-41f0-9685-ff5bb260df2e";
        var crashed = Service();
        await crashed.EnableAsync(Options(BackgroundAppAction.LeaveAlone));

        // The process went away without disabling anything: the plan is still High Performance.
        _powerPlans.Active.Should().Be(GameModeService.HighPerformanceGuid);

        var nextStart = Service();
        var restored = await nextStart.RestorePowerPlanAfterCrashAsync();

        restored.Should().Be("381b4222-f694-41f0-9685-ff5bb260df2e");
        _powerPlans.Active.Should().Be("381b4222-f694-41f0-9685-ff5bb260df2e");

        // And having put it back, it does not do it again.
        (await Service().RestorePowerPlanAfterCrashAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ASessionThatEndedProperlyLeavesNothingToRestore()
    {
        _powerPlans.Active = "381b4222-f694-41f0-9685-ff5bb260df2e";
        var service = Service();
        await service.EnableAsync(Options(BackgroundAppAction.LeaveAlone));
        await service.DisableAsync();

        (await Service().RestorePowerPlanAfterCrashAsync()).Should().BeNull();
    }

    [Fact]
    public void AProfileSaysWhatToDoWithBackgroundAppsAndKeepsLoadingOldOnes()
    {
        var profile = new PerformanceProfile();
        profile.BackgroundAppAction.Should().Be(BackgroundAppAction.LowerPriority, "nothing closes unless asked");

        // A profile saved by an earlier version, when the list was called ProcessesToKill.
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PerformanceProfile>(
            """{"Name":"Old","ProcessesToKill":["chrome","discord"]}""");

        loaded!.BackgroundApps.Should().BeEquivalentTo(["chrome", "discord"]);
        loaded.BackgroundAppAction.Should().Be(BackgroundAppAction.LowerPriority);
    }

    private GameModeService Service() => new(
        StubMemoryOptimizer(),
        _powerPlans,
        Path.Combine(_temp.Path, "game-mode-power-plan.txt"));

    private static GameModeOptions Options(BackgroundAppAction action) => new()
    {
        BackgroundApps = action,
        BackgroundAppNames = [AppName],
        OptimizeMemory = false,
    };

    /// <summary>A long-running process with no window, standing in for a background app.</summary>
    private Process StartBackgroundApp()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = "-n 120 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;

        _started.Add(process);

        // A child inherits its parent's priority class, and CI runners start the test host below normal - so an
        // app started from here would already be at BelowNormal, which Game Mode rightly leaves alone
        // (GameModeService.LowerPriority). The app these tests stand in for runs at Normal, so it is put there.
        process.PriorityClass = ProcessPriorityClass.Normal;
        return process;
    }

    private static Process Refreshed(Process process)
    {
        process.Refresh();
        return process;
    }

    private static IMemoryOptimizer StubMemoryOptimizer()
    {
        // The real one trims every process on the machine; a Game Mode test has no business doing that.
        var optimizer = new Mock<IMemoryOptimizer>();
        optimizer.Setup(o => o.OptimizeMemoryAsync()).ReturnsAsync(0L);
        return optimizer.Object;
    }

    private sealed class FakePowerPlans : IPowerPlanController
    {
        public string? Active { get; set; }

        public Task<string?> GetActiveAsync() => Task.FromResult(Active);

        public Task<bool> SetActiveAsync(string guid)
        {
            Active = guid;
            return Task.FromResult(true);
        }
    }
}
