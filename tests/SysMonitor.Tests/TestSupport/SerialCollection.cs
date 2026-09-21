using Xunit;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// Tests that measure something belonging to the whole test process — its handle count, its working set —
/// and so cannot share it with anything else running at the same time.
/// <para>
/// xUnit runs test classes in parallel by default. A handle-leak test measured against a process where a
/// dozen other tests are opening files, registry keys and child processes is not measuring the leak; it is
/// measuring the weather. This collection runs on its own.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialCollection
{
    public const string Name = "measures the whole process";
}
