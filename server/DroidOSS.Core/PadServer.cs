namespace DroidOSS.Core;

/// <summary>What happened to a packet we were handed.</summary>
/// <remarks>
/// Distinguishing these matters. "12,000 malformed" and "12,000 stale" look the
/// same in a total-dropped counter but point at completely different problems —
/// the first is a protocol or version mismatch, the second is network reordering.
/// Guessing between them later is the kind of avoidable evening this project
/// keeps trying to prevent.
/// </remarks>
public enum PacketOutcome
{
    /// <summary>Valid and newer than the last one. The pad has been updated.</summary>
    Applied,

    /// <summary>Not a well-formed INPUT packet. Wrong length, magic, version, or type.</summary>
    Malformed,

    /// <summary>Well-formed, but older than or equal to one already applied.</summary>
    Stale,
}

/// <summary>
/// Turns received bytes into controller movement.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about sockets. It is handed a span of bytes from
/// wherever — a UDP datagram at runtime, a byte array in a test — which is what
/// lets the interesting behaviour (rejecting garbage, dropping stale packets) be
/// tested with no network and no driver involved.
///
/// B2 handles a single client on slot 0. Multiple clients, slot assignment and
/// the silence timeout arrive at B3, at which point this grows a map from sender
/// address to slot and one <see cref="SequenceGate"/> per client.
/// </remarks>
public sealed class PadServer
{
    private readonly IPadBackend _backend;
    private readonly SequenceGate _gate = new();
    private readonly int _slot;

    /// <summary>Packets that were valid, current, and reached the pad.</summary>
    public long Applied { get; private set; }

    /// <summary>Packets rejected before any field was trusted.</summary>
    public long Malformed { get; private set; }

    /// <summary>Packets that were valid but arrived out of order.</summary>
    public long Stale { get; private set; }

    /// <summary>Total seen, whatever became of them.</summary>
    public long Total => Applied + Malformed + Stale;

    /// <summary>The most recent state actually applied. Useful for a status display.</summary>
    public PadState LastApplied { get; private set; }

    public PadServer(IPadBackend backend, int slot = 0)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, IPadBackend.MaxPads);

        _backend = backend;
        _slot = slot;
    }

    /// <summary>
    /// Validates one datagram and, if it is worth applying, moves the pad.
    /// </summary>
    /// <remarks>
    /// Order is deliberate: parse and validate before trusting anything, then
    /// check ordering, and only then touch the driver. Nothing here allocates —
    /// it runs 125 times a second per client.
    /// </remarks>
    public PacketOutcome Handle(ReadOnlySpan<byte> data)
    {
        // Anything that isn't unmistakably ours is dropped without further
        // thought. A UDP socket receives port scans and other applications'
        // strays; interpreting one as controller state makes the character spasm
        // with nothing in the logs to explain it.
        if (!InputPacket.TryRead(data, out var packet))
        {
            Malformed++;
            return PacketOutcome.Malformed;
        }

        // UDP can deliver out of order. Applying an older snapshot would jerk the
        // stick back to a position the thumb has already left.
        if (!_gate.Accept(packet.Sequence))
        {
            Stale++;
            return PacketOutcome.Stale;
        }

        var state = packet.State;
        _backend.Submit(_slot, in state);

        LastApplied = state;
        Applied++;
        return PacketOutcome.Applied;
    }

    /// <summary>
    /// Centres the pad and releases every button.
    /// </summary>
    /// <remarks>
    /// Call this before unplugging. A game that latches the last state it saw
    /// would otherwise hold a stick down forever.
    /// </remarks>
    public void Neutralise()
    {
        var neutral = PadState.Neutral;
        _backend.Submit(_slot, in neutral);
        LastApplied = neutral;
    }
}
