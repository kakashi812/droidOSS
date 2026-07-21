namespace DroidOSS.Tests;

/// <summary>
/// A clock that only moves when a test tells it to.
/// </summary>
/// <remarks>
/// The session timeout is two seconds. Testing it by actually waiting two
/// seconds would make the suite slow and, worse, flaky — a loaded CI machine
/// turns "1.9 s, still alive" into an intermittent failure. Advancing time by
/// hand makes the boundary exact and the test instant.
///
/// <see cref="TimeProvider"/> is part of the base class library, so injecting it
/// costs <c>DroidOSS.Core</c> no dependency at all.
/// </remarks>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Advance(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
