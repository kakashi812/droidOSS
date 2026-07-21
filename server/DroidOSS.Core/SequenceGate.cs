namespace DroidOSS.Core;

/// <summary>
/// Discards packets that are older than one already seen.
/// </summary>
/// <remarks>
/// UDP can deliver out of order. If snapshot 58 arrives after 59, applying it
/// would jerk the stick back to a stale position — a visible glitch. We never
/// ask for a missing packet back, because the next one 8 ms later already
/// contains the entire truth.
///
/// One gate per connected phone; they have independent counters.
/// </remarks>
public sealed class SequenceGate
{
    private uint _last;
    private bool _seenAny;

    /// <summary>The highest sequence accepted so far. Zero before the first packet.</summary>
    public uint Last => _last;

    /// <summary>
    /// Decides whether a packet is new enough to apply.
    /// </summary>
    /// <returns><c>true</c> to apply it; <c>false</c> if it is stale or a duplicate.</returns>
    public bool Accept(uint sequence)
    {
        // The first packet has nothing to compare against, so it always wins.
        // Its sequence becomes the baseline — a phone may reconnect and start
        // counting from anywhere.
        if (!_seenAny)
        {
            _seenAny = true;
            _last = sequence;
            return true;
        }

        // Subtract as unsigned, then read the result as signed. Across the u32
        // boundary the difference still comes out as a small positive number,
        // so 0xFFFFFFFF -> 0 reads as "one newer" rather than "four billion older".
        //
        // The obvious `sequence <= _last` looks equivalent and is not: at
        // roughly 1.1 years of continuous play the counter wraps to zero, every
        // subsequent packet is rejected, and the controller freezes forever.
        if ((int)(sequence - _last) <= 0) return false;

        _last = sequence;
        return true;
    }

    /// <summary>
    /// Forgets everything, so the next packet is treated as a first packet.
    /// </summary>
    /// <remarks>Used when a phone disconnects and its slot is reused.</remarks>
    public void Reset()
    {
        _last = 0;
        _seenAny = false;
    }
}
