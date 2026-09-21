using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Turning a startup item off has to be undoable from inside the app, and has to be the same "off" that Task
/// Manager and Settings show. Everything here runs against a throwaway key and a temporary folder; the real
/// startup entries of this machine are never touched.
/// </summary>
public class StartupOptimizerTests : IDisposable
{
    private const string Run = @"Run";
    private const string Approved = @"Explorer\StartupApproved\Run";
    private const string SetAside = @"Run-Disabled";
    private const string RunOnce = @"RunOnce";
    private const string RunOnceSetAside = @"RunOnce-Disabled";

    private readonly TestRegistryKey _registry = new();
    private readonly TempDirectory _startupFolder = new("startup-folder");
    private readonly StartupOptimizer _optimizer;

    public StartupOptimizerTests()
    {
        var locations = new List<StartupOptimizer.StartupLocation>
        {
            new(Registry.CurrentUser, "HKCU Run", Path(Run), Path(Approved), Path(SetAside)),
            new(Registry.CurrentUser, "HKCU RunOnce", Path(RunOnce), null, Path(RunOnceSetAside)),
        };
        var folder = new StartupOptimizer.StartupFolder(
            _startupFolder.Path, Registry.CurrentUser, Path(@"Explorer\StartupApproved\StartupFolder"));

        _optimizer = new StartupOptimizer(Mock.Of<ILogger<StartupOptimizer>>(), locations, folder);
    }

    public void Dispose()
    {
        _registry.Dispose();
        _startupFolder.Dispose();
    }

    [Fact]
    public async Task AnItemWindowsIsAllowedToRunIsListedAsOn()
    {
        GivenRunEntry("Good", @"C:\App\good.exe");

        var item = await SingleItemAsync("Good");

        item.IsEnabled.Should().BeTrue();
        item.Command.Should().Be(@"C:\App\good.exe");
        item.Location.Should().Be("HKCU Run");
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    // Both of these are on this machine, written by something other than Task Manager.
    [InlineData(new byte[] { 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    public async Task AnItemIsListedByWhatWindowsRecordsAboutIt(byte[] approval, bool expectedEnabled)
    {
        GivenRunEntry("Thing", @"C:\App\thing.exe");
        SetApproval(Approved, "Thing", approval);

        (await SingleItemAsync("Thing")).IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public async Task TurningAnItemOffLeavesItInPlaceAndMarksItTheWayWindowsDoes()
    {
        GivenRunEntry("Thing", @"C:\App\thing.exe");
        var item = await SingleItemAsync("Thing");

        var result = await _optimizer.DisableStartupItemAsync(item);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("no longer start");
        ValueIn(Run, "Thing").Should().Be(@"C:\App\thing.exe", "the entry itself stays, which is what makes this undoable");

        var approval = (byte[])ValueIn(Approved, "Thing")!;
        approval.Should().HaveCount(12);
        approval[0].Should().Be(0x03, "Task Manager writes 03 for an item that may not run");
        BitConverter.ToInt64(approval, 4).Should().BeGreaterThan(0, "and records when it was turned off");

        (await SingleItemAsync("Thing")).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task TurningAnItemBackOnUndoesThat()
    {
        GivenRunEntry("Thing", @"C:\App\thing.exe");
        var item = await SingleItemAsync("Thing");
        await _optimizer.DisableStartupItemAsync(item);

        var result = await _optimizer.EnableStartupItemAsync(await SingleItemAsync("Thing"));

        result.Success.Should().BeTrue();
        ((byte[])ValueIn(Approved, "Thing")!)[0].Should().Be(0x02);
        (await SingleItemAsync("Thing")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task AnItemAnOlderVersionMovedAsideIsListedAsOffAndCanBeBroughtBack()
    {
        // What the previous implementation left behind: the entry moved out of Run and no way back.
        SetValue(SetAside, "Legacy", @"C:\App\legacy.exe");

        var item = await SingleItemAsync("Legacy");
        item.IsEnabled.Should().BeFalse("it does not start with Windows while it sits there");

        var result = await _optimizer.EnableStartupItemAsync(item);

        result.Success.Should().BeTrue();
        ValueIn(Run, "Legacy").Should().Be(@"C:\App\legacy.exe", "and it is back where Windows looks for it");
        ValueIn(SetAside, "Legacy").Should().BeNull();
        (await SingleItemAsync("Legacy")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task AStaleSetAsideCopyIsNeverPutBackOverTheEntryWindowsIsUsing()
    {
        // The state this machine is in: the app set an entry aside, the program was updated and wrote itself
        // back into Run with a new path, and the copy left behind still points at the version that is gone.
        SetValue(SetAside, "Updater", @"C:\App\1.0\updater.exe");
        GivenRunEntry("Updater", @"C:\App\2.0\updater.exe");

        var item = await SingleItemAsync("Updater");
        item.Command.Should().Be(@"C:\App\2.0\updater.exe", "the live entry is the one listed");

        var result = await _optimizer.EnableStartupItemAsync(item);

        result.Success.Should().BeTrue();
        ValueIn(Run, "Updater").Should().Be(@"C:\App\2.0\updater.exe",
            "putting the old path back would point Windows at a version that is no longer installed");
        ValueIn(SetAside, "Updater").Should().BeNull("the stale copy is dropped rather than kept for next time");
    }

    [Fact]
    public async Task ARunOnceItemIsSetAsideWhereItCanBeFoundAgain()
    {
        SetValue(RunOnce, "Once", @"C:\App\once.exe");
        var item = (await _optimizer.GetStartupItemsAsync()).Single(i => i.Name == "Once");
        item.Location.Should().Be("HKCU RunOnce");

        (await _optimizer.DisableStartupItemAsync(item)).Success.Should().BeTrue();

        ValueIn(RunOnce, "Once").Should().BeNull("RunOnce has no key for whether an entry may run");
        ValueIn(RunOnceSetAside, "Once").Should().Be(@"C:\App\once.exe");
        (await SingleItemAsync("Once")).IsEnabled.Should().BeFalse();

        (await _optimizer.EnableStartupItemAsync(await SingleItemAsync("Once"))).Success.Should().BeTrue();
        ValueIn(RunOnce, "Once").Should().Be(@"C:\App\once.exe");
    }

    [Fact]
    public async Task AnItemThatIsNoLongerThereIsReportedAsSuchAndNothingIsWritten()
    {
        var ghost = new StartupItem
        {
            Name = "Ghost",
            Command = "nonexistent.exe",
            Location = "HKCU Run",
            Type = StartupItemType.Registry,
        };

        var result = await _optimizer.DisableStartupItemAsync(ghost);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no longer listed");
        ValueIn(SetAside, "Ghost").Should().BeNull("a change that did not happen leaves nothing behind");
        ValueIn(Approved, "Ghost").Should().BeNull();
    }

    [Fact]
    public async Task AnItemInAPlaceThisAppDoesNotManageIsRefused()
    {
        var result = await _optimizer.DisableStartupItemAsync(new StartupItem
        {
            Name = "Elsewhere",
            Location = "HKLM Run",
            Type = StartupItemType.Registry,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("does not manage");
    }

    [Fact]
    public async Task DeletingAnItemRemovesTheEntryAndWhatWindowsRecordedAboutIt()
    {
        GivenRunEntry("Thing", @"C:\App\thing.exe");
        await _optimizer.DisableStartupItemAsync(await SingleItemAsync("Thing"));

        var result = await _optimizer.DeleteStartupItemAsync(await SingleItemAsync("Thing"));

        result.Success.Should().BeTrue();
        ValueIn(Run, "Thing").Should().BeNull();
        ValueIn(Approved, "Thing").Should().BeNull("a leftover flag would turn up again on a name reused later");
        (await _optimizer.GetStartupItemsAsync()).Should().NotContain(i => i.Name == "Thing");
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereFails()
    {
        var result = await _optimizer.DeleteStartupItemAsync(new StartupItem
        {
            Name = "NonExistent",
            FilePath = System.IO.Path.Combine(_startupFolder.Path, "nothing.lnk"),
            Type = StartupItemType.StartupFolder,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no longer in the startup folder");
    }

    [Fact]
    public async Task AShortcutInTheStartupFolderIsTurnedOffWhereWindowsLooks()
    {
        var shortcut = System.IO.Path.Combine(_startupFolder.Path, "App.lnk");
        await File.WriteAllTextAsync(shortcut, "not really a shortcut");

        var item = (await _optimizer.GetStartupItemsAsync()).Single(i => i.Type == StartupItemType.StartupFolder);
        item.IsEnabled.Should().BeTrue();

        (await _optimizer.DisableStartupItemAsync(item)).Success.Should().BeTrue();

        File.Exists(shortcut).Should().BeTrue("the shortcut stays; only the flag changes");
        ((byte[])ValueIn(@"Explorer\StartupApproved\StartupFolder", "App.lnk")!)[0].Should().Be(0x03);
        (await _optimizer.GetStartupItemsAsync()).Single(i => i.Type == StartupItemType.StartupFolder)
            .IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void TheValueWrittenForAnItemThatMayNotRunIsWhatWindowsWrites()
    {
        var disabledAt = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

        var value = StartupOptimizer.ApprovalValue(enabled: false, disabledAt);

        value.Should().HaveCount(12);
        value[0].Should().Be(0x03);
        BitConverter.ToInt64(value, 4).Should().Be(disabledAt.ToFileTimeUtc());
        StartupOptimizer.MayRun(value).Should().BeFalse();

        StartupOptimizer.MayRun(StartupOptimizer.ApprovalValue(enabled: true, disabledAt)).Should().BeTrue();
        StartupOptimizer.MayRun(null).Should().BeTrue("an entry nothing was recorded about may run");
        StartupOptimizer.MayRun([]).Should().BeTrue();
    }

    private string Path(string relative) => $@"{_registry.SubPath}\{relative}";

    private void GivenRunEntry(string name, string command) => SetValue(Run, name, command);

    private void SetValue(string relativePath, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path(relativePath), writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    private void SetApproval(string relativePath, string name, byte[] value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path(relativePath), writable: true);
        key.SetValue(name, value, RegistryValueKind.Binary);
    }

    private object? ValueIn(string relativePath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path(relativePath));
        return key?.GetValue(name);
    }

    private async Task<StartupItem> SingleItemAsync(string name) =>
        (await _optimizer.GetStartupItemsAsync()).Single(i => i.Name == name);
}
