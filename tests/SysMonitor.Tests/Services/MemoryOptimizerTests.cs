using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Only the cases that ask nothing of the rest of the machine. Trimming every process on the developer's box
/// - which the test here used to do on every run - proves nothing either.
/// <para>
/// The three invalid-id cases below are kept because they pin real behaviour, but on their own they were the
/// whole suite, and all three reach the same <c>catch</c>: replacing the entire method body with
/// <c>Task.FromResult(0L)</c> left them green. The tests that follow are the ones that cannot survive that -
/// one proves the trim frees something, the other proves it closes the kernel handle it opened.
/// </para>
/// </summary>
[Collection(SerialCollection.Name)]
public class MemoryOptimizerTests
{
    private readonly MemoryOptimizer _memoryOptimizer = new(Mock.Of<ILogger<MemoryOptimizer>>());

    [Fact]
    public async Task TrimProcessWorkingSetAsync_WithAnImpossibleId_FreesNothing()
    {
        (await _memoryOptimizer.TrimProcessWorkingSetAsync(-1)).Should().Be(0);
    }

    [Fact]
    public async Task TrimProcessWorkingSetAsync_WithAProcessThatIsNotRunning_FreesNothing()
    {
        // Process ids are multiples of four on Windows, so this one cannot be in use.
        (await _memoryOptimizer.TrimProcessWorkingSetAsync(0x7FFFFFFF)).Should().Be(0);
    }

    [Fact]
    public async Task TrimProcessWorkingSetAsync_DoesNotThrowOnTheIdleProcess()
    {
        // Process 0 exists but no handle can be opened for it: the answer is zero, not an exception.
        (await _memoryOptimizer.TrimProcessWorkingSetAsync(0)).Should().Be(0);
    }

    [Fact]
    public async Task TrimProcessWorkingSetAsync_ActuallyFreesSomething()
    {
        // 64 MB touched a page at a time, so the working set genuinely holds it and there is something for
        // EmptyWorkingSet to page out. Without this the method under test could return 0 honestly.
        var ballast = new byte[64 * 1024 * 1024];
        for (var offset = 0; offset < ballast.Length; offset += 4096)
            ballast[offset] = 1;

        using var self = Process.GetCurrentProcess();
        var freed = await _memoryOptimizer.TrimProcessWorkingSetAsync(self.Id);

        GC.KeepAlive(ballast);
        freed.Should().BeGreaterThan(0, "a method whose body is Task.FromResult(0L) must not pass this");
    }

    /// <summary>
    /// <c>proc.Handle</c> forces <c>OpenProcess</c>, so an undisposed <see cref="Process"/> is a real kernel
    /// handle held until a finaliser happens to run. This class is in the serial collection because handle
    /// count belongs to the whole test process, and a dozen other tests opening files at the same time would
    /// drown the signal.
    /// </summary>
    [Fact]
    public async Task TrimProcessWorkingSetAsync_ClosesTheHandleItOpened()
    {
        using var self = Process.GetCurrentProcess();

        // One call first: the first trim of a run allocates things a steady-state loop does not.
        await _memoryOptimizer.TrimProcessWorkingSetAsync(self.Id);

        self.Refresh();
        var before = self.HandleCount;

        const int trims = 300;
        for (var i = 0; i < trims; i++)
            await _memoryOptimizer.TrimProcessWorkingSetAsync(self.Id);

        self.Refresh();
        var leaked = self.HandleCount - before;

        leaked.Should().BeLessThan(trims / 4, $"{trims} trims must not cost {trims} kernel handles");
    }
}
