namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// A clock a test moves by hand, in either direction.
///
/// <c>FakeTimeProvider</c> from <c>Microsoft.Extensions.TimeProvider.Testing</c> refuses to go backwards
/// ("Cannot go back in time"), and going backwards is exactly the case worth testing: the hour that repeats
/// every autumn, and an operator correcting a drifted clock.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestClock(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    public void Rewind(TimeSpan delta) => _utcNow -= delta;
}
