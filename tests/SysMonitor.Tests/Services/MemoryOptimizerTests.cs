using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Optimizers;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Only the cases that ask nothing of the rest of the machine. Trimming every process on the developer's box
/// - which the test here used to do on every run - proves nothing either: the old assertion was that the
/// result is not negative, which a method that does nothing at all also satisfies.
/// </summary>
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
}
