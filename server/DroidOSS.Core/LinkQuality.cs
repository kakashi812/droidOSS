namespace DroidOSS.Core;

/// <summary>How well a phone's input is getting through, in words a player understands.</summary>
public enum LinkQuality
{
    /// <summary>Nothing measured yet — the phone has only just connected.</summary>
    Unknown,

    /// <summary>Near the phone's full send rate. Nothing to do.</summary>
    Good,

    /// <summary>Noticeably short. Playable, but input may feel a little late.</summary>
    Fair,

    /// <summary>Most input is not arriving. Worth moving closer to the router.</summary>
    Poor,
}

/// <summary>
/// Packets per second from a running total, measured over roughly one second.
/// </summary>
/// <remarks>
/// Fed the session's <see cref="SessionInfo.Applied"/> count on every UI tick,
/// however often that is. Short ticks are accumulated rather than measured on
/// their own, so a 30 fps display and a once-a-second status line report the
/// same steady number instead of one jittering with the tick timing.
/// </remarks>
public sealed class RateMeter
{
    /// <summary>How much time one measurement spans.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private long _windowStartCount;
    private DateTimeOffset _windowStart;
    private bool _started;

    /// <summary>The last complete measurement, or null until one second has passed.</summary>
    public double? Rate { get; private set; }

    /// <summary>Take a reading of the running total.</summary>
    public void Update(long total, DateTimeOffset now)
    {
        // A smaller total means a new session took the slot: start over.
        if (!_started || total < _windowStartCount)
        {
            Reset();
            _started = true;
            _windowStartCount = total;
            _windowStart = now;
            return;
        }

        var elapsed = now - _windowStart;
        if (elapsed < Window) return;

        Rate = (total - _windowStartCount) / elapsed.TotalSeconds;
        _windowStartCount = total;
        _windowStart = now;
    }

    /// <summary>Forget everything, for when the slot empties.</summary>
    public void Reset()
    {
        _started = false;
        _windowStartCount = 0;
        Rate = null;
    }
}

/// <summary>Turns a packet rate into a <see cref="LinkQuality"/>.</summary>
public static class LinkQualityRating
{
    /// <summary>The phone app sends every 8 ms.</summary>
    public const double ExpectedRate = 125;

    /// <summary>At or above this share of <see cref="ExpectedRate"/>: good.</summary>
    public const double GoodShare = 0.8;

    /// <summary>At or above this share: fair. Below it: poor.</summary>
    public const double FairShare = 0.5;

    public static LinkQuality FromRate(double? packetsPerSecond) => packetsPerSecond switch
    {
        null => LinkQuality.Unknown,
        >= ExpectedRate * GoodShare => LinkQuality.Good,
        >= ExpectedRate * FairShare => LinkQuality.Fair,
        _ => LinkQuality.Poor,
    };
}
