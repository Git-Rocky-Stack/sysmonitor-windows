using FluentAssertions;
using Moq;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Turning Game Mode on records the power plan the machine was on, then changes it. Turning it off puts the
/// recorded one back, and a note on disk means a crash mid-session is recovered from on the next launch.
/// <para>
/// All of that rests on the recorded plan being the one the user actually had. Three callers can turn Game
/// Mode on — the page's toggle, the auto-detect timer and a performance profile — from three different
/// threads, and <c>_isEnabled</c> was a plain <c>bool</c> with no lock around the read-then-act. A second
/// activation while the first is still running records <b>High Performance</b> as the plan to go back to.
/// The machine then never leaves it: not on disable, and not on the next launch either, because the note on
/// disk says High Performance is where it belongs.
/// </para>
/// </summary>
public class GameModeReentryTests : IDisposable
{
    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private readonly TempDirectory _temp = new("game-mode-reentry");
    private readonly FakePowerPlans _powerPlans = new() { Active = Balanced };

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task TurningItOnTwice_StillRemembersThePlanTheMachineStartedOn()
    {
        var service = Service();

        await service.EnableAsync(HighPerformanceOptions());
        _powerPlans.Active.Should().Be(HighPerformance, "the test is worthless if the first activation did nothing");

        await service.EnableAsync(HighPerformanceOptions());
        await service.DisableAsync();

        _powerPlans.Active.Should().Be(Balanced,
            "the second activation must not record High Performance as the plan to go back to");
    }

    [Fact]
    public async Task ThreeCallersTurningItOnAtOnce_StillRememberThePlanTheMachineStartedOn()
    {
        var service = Service();

        // The page's toggle, the auto-detect timer and a profile, all arriving together.
        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => service.EnableAsync(HighPerformanceOptions()))));

        await service.DisableAsync();

        _powerPlans.Active.Should().Be(Balanced);
    }

    [Fact]
    public async Task TurningItOnTwice_LeavesTheCrashNoteNamingThePlanTheMachineStartedOn()
    {
        var statePath = Path.Combine(_temp.Path, "game-mode-power-plan.txt");
        var service = Service(statePath);

        await service.EnableAsync(HighPerformanceOptions());
        await service.EnableAsync(HighPerformanceOptions());

        File.Exists(statePath).Should().BeTrue("the note is what recovers the plan after a crash");
        File.ReadAllText(statePath).Trim().Should().Be(Balanced,
            "a note naming High Performance would re-apply it on every launch, for ever");
    }

    [Fact]
    public async Task TurningItOffTwice_DoesNotThrowAndLeavesThePlanAlone()
    {
        var service = Service();
        await service.EnableAsync(HighPerformanceOptions());

        await service.DisableAsync();
        await service.DisableAsync();

        _powerPlans.Active.Should().Be(Balanced);
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task TurningItOffWithoutEverTurningItOn_ChangesNothing()
    {
        var service = Service();

        await service.DisableAsync();

        _powerPlans.Active.Should().Be(Balanced, "there was nothing to put back");
        service.IsEnabled.Should().BeFalse();
    }

    // ---------------------------------------------------------------- helpers

    private GameModeService Service(string? statePath = null) => new(
        Mock.Of<IMemoryOptimizer>(),
        _powerPlans,
        statePath ?? Path.Combine(_temp.Path, "game-mode-power-plan.txt"));

    /// <summary>Game Mode with nothing to close, so only the power plan is in play.</summary>
    private static GameModeOptions HighPerformanceOptions() => new()
    {
        BackgroundApps = BackgroundAppAction.LowerPriority,
        BackgroundAppNames = [],
        OptimizeMemory = false,
        PowerPlanGuid = HighPerformance,
    };

    private sealed class FakePowerPlans : IPowerPlanController
    {
        private readonly object _gate = new();
        private string? _active;

        public string? Active
        {
            get { lock (_gate) return _active; }
            set { lock (_gate) _active = value; }
        }

        public Task<string?> GetActiveAsync() => Task.FromResult(Active);

        public Task<bool> SetActiveAsync(string guid)
        {
            Active = guid;
            return Task.FromResult(true);
        }
    }
}
