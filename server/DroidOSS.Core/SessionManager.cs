namespace DroidOSS.Core;

/// <summary>What happened to a message we were handed.</summary>
/// <remarks>
/// Distinguishing these matters. "12,000 dropped" is useless; the reasons point
/// at completely different problems. <see cref="Malformed"/> is a version or
/// protocol mismatch, <see cref="Stale"/> is network reordering, and
/// <see cref="UnknownSender"/> is the specific and otherwise miserable case of a
/// phone that believes it is connected while the server has never heard of it.
/// </remarks>
public enum MessageOutcome
{
    /// <summary>Valid, current, and applied to the pad.</summary>
    InputApplied,

    /// <summary>Well-formed, but older than or equal to one already applied.</summary>
    Stale,

    /// <summary>Not a well-formed message of any kind. Wrong length, magic, version or type.</summary>
    Malformed,

    /// <summary>Input from an address that never completed a handshake. Dropped.</summary>
    UnknownSender,

    /// <summary>A HELLO that got a slot. A pad is now plugged in.</summary>
    SessionOpened,

    /// <summary>A HELLO that arrived with all four slots in use.</summary>
    SessionRejected,

    /// <summary>A BYE from a known client. Its pad has been zeroed and unplugged.</summary>
    SessionClosed,

    /// <summary>Understood, but nothing to do — a duplicate HELLO, or a BYE from a stranger.</summary>
    Ignored,
}

/// <summary>Why a session ended.</summary>
public enum SessionCloseReason
{
    /// <summary>The phone said goodbye. The clean path.</summary>
    Bye,

    /// <summary>Silence for <see cref="Protocol.SessionTimeout"/>. The phone vanished.</summary>
    Timeout,

    /// <summary>The server is shutting down.</summary>
    ServerShutdown,
}

/// <summary>A connected phone, as seen from outside.</summary>
public readonly record struct SessionInfo(
    ClientKey Client,
    int Slot,
    PadState LastState,
    long Applied);

public sealed class SessionEventArgs(ClientKey client, int slot, SessionCloseReason? reason = null)
    : EventArgs
{
    public ClientKey Client { get; } = client;
    public int Slot { get; } = slot;

    /// <summary>Why it ended. Null for an opening event.</summary>
    public SessionCloseReason? Reason { get; } = reason;
}

/// <summary>
/// Tracks which phones are connected, which pad each one drives, and when to
/// give up on the ones that stopped talking.
/// </summary>
/// <remarks>
/// Knows nothing about sockets. It is handed a span of bytes and the address it
/// came from — a UDP datagram at runtime, a byte array in a test — and replies,
/// when a reply is called for, by writing into a buffer the caller supplies.
/// That is what lets every interesting behaviour here be tested with no network,
/// no driver, and no waiting.
///
/// The rule that shapes everything: <b>the server assigns slots, the phone
/// asks.</b> A phone that chose its own slot would collide with the next one to
/// connect. Input from an address that never completed a handshake is dropped
/// and counted, never silently adopted — otherwise a stray broadcast could
/// consume one of only four slots with nothing to explain where it went.
/// </remarks>
public sealed class SessionManager
{
    private sealed class Session(ClientKey client, int slot)
    {
        public ClientKey Client { get; } = client;
        public int Slot { get; } = slot;
        public DateTimeOffset LastSeen { get; set; }
        public PadState LastState { get; set; }
        public long Applied { get; set; }
    }

    private readonly IPadBackend _backend;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;

    private readonly Dictionary<ClientKey, Session> _sessions = [];

    // One gate per slot rather than per session, reset when a slot is reused.
    // Per-slot means no allocation when a phone connects; per-client — rather
    // than one shared gate — is essential, because a second phone starting its
    // count at 1 would have every packet rejected as stale by a first phone
    // already somewhere in the tens of thousands.
    private readonly SequenceGate[] _gates;
    private readonly bool[] _slotTaken = new bool[IPadBackend.MaxPads];

    // Reused by the timeout sweep so closing a session allocates nothing. At
    // most four entries, and it never survives the call.
    private readonly List<ClientKey> _expired = new(IPadBackend.MaxPads);

    // Handle runs on the socket loop, SweepTimeouts on a timer, and the status
    // display on a third thread. Uncontended locks cost tens of nanoseconds —
    // far below the 8 ms budget, and much cheaper than a torn session table.
    private readonly Lock _lock = new();

    /// <summary>Input packets that reached a pad.</summary>
    public long Applied { get; private set; }

    /// <summary>Packets that arrived out of order and were dropped.</summary>
    public long Stale { get; private set; }

    /// <summary>Datagrams rejected before any field was trusted.</summary>
    public long Malformed { get; private set; }

    /// <summary>Input from an address with no session. A lost HELLO looks like this.</summary>
    public long UnknownSender { get; private set; }

    /// <summary>HELLOs refused because all four slots were in use.</summary>
    public long Rejected { get; private set; }

    /// <summary>How many phones are connected right now.</summary>
    public int SessionCount
    {
        get { lock (_lock) return _sessions.Count; }
    }

    public event EventHandler<SessionEventArgs>? SessionOpened;
    public event EventHandler<SessionEventArgs>? SessionClosed;

    /// <param name="backend">Where accepted state is sent.</param>
    /// <param name="clock">Injectable so the timeout can be tested without waiting for it.</param>
    /// <param name="timeout">Silence before a pad is dropped. Defaults to <see cref="Protocol.SessionTimeout"/>.</param>
    public SessionManager(IPadBackend backend, TimeProvider? clock = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(backend);

        _backend = backend;
        _clock = clock ?? TimeProvider.System;
        _timeout = timeout ?? Protocol.SessionTimeout;

        _gates = new SequenceGate[IPadBackend.MaxPads];
        for (var i = 0; i < _gates.Length; i++) _gates[i] = new SequenceGate();
    }

    /// <summary>
    /// Handles one datagram, and produces a reply if the message calls for one.
    /// </summary>
    /// <param name="data">The datagram exactly as received.</param>
    /// <param name="sender">Where it came from. This is what identifies the phone.</param>
    /// <param name="reply">Scratch space for a reply. Must hold at least <see cref="Protocol.SessionMessageSize"/> bytes.</param>
    /// <param name="replyLength">Bytes written to <paramref name="reply"/>. Zero means send nothing.</param>
    public MessageOutcome Handle(
        ReadOnlySpan<byte> data, ClientKey sender, Span<byte> reply, out int replyLength)
    {
        replyLength = 0;

        lock (_lock)
        {
            // Input first: it is the hot path by three orders of magnitude.
            // The two readers cannot be confused for one another — they require
            // different exact lengths.
            if (InputPacket.TryRead(data, out var packet))
                return HandleInput(in packet, sender);

            if (SessionMessage.TryRead(data, out var type, out var pad))
                return HandleSession(type, pad, sender, reply, out replyLength);

            Malformed++;
            return MessageOutcome.Malformed;
        }
    }

    private MessageOutcome HandleInput(in InputPacket packet, ClientKey sender)
    {
        if (!_sessions.TryGetValue(sender, out var session))
        {
            // A phone that thinks it is connected while we have never heard of
            // it — a HELLO was lost, or we restarted underneath it. Dropping is
            // deliberate: the phone re-sends HELLO until it gets a WELCOME, so
            // this resolves itself within a couple of hundred milliseconds.
            UnknownSender++;
            return MessageOutcome.UnknownSender;
        }

        // Liveness before ordering. A duplicate packet is useless as input but
        // still proves the phone is alive, and it would be perverse to time out
        // a phone that is demonstrably still sending.
        session.LastSeen = _clock.GetUtcNow();

        if (!_gates[session.Slot].Accept(packet.Sequence))
        {
            Stale++;
            return MessageOutcome.Stale;
        }

        var state = packet.State;
        _backend.Submit(session.Slot, in state);

        session.LastState = state;
        session.Applied++;
        Applied++;
        return MessageOutcome.InputApplied;
    }

    private MessageOutcome HandleSession(
        MessageType type, byte pad, ClientKey sender, Span<byte> reply, out int replyLength)
    {
        replyLength = 0;

        switch (type)
        {
            case MessageType.Hello:
                return HandleHello(sender, reply, out replyLength);

            case MessageType.Bye:
                // The pad byte is informational — the sender's address is what
                // identifies the session, so a phone cannot close someone else's.
                _ = pad;

                if (!_sessions.TryGetValue(sender, out var session))
                    return MessageOutcome.Ignored;

                CloseSession(session, SessionCloseReason.Bye);
                return MessageOutcome.SessionClosed;

            // WELCOME travels the other way. Receiving one means something is
            // confused; it is not our business to act on it.
            default:
                return MessageOutcome.Ignored;
        }
    }

    private MessageOutcome HandleHello(ClientKey sender, Span<byte> reply, out int replyLength)
    {
        // A repeat HELLO is the normal consequence of a lost WELCOME, so it must
        // be harmless: hand back the slot already held rather than consuming a
        // second one. Retries would otherwise exhaust all four slots in under a
        // second.
        if (_sessions.TryGetValue(sender, out var existing))
        {
            existing.LastSeen = _clock.GetUtcNow();
            replyLength = SessionMessage.Write(reply, MessageType.Welcome, (byte)existing.Slot);
            return MessageOutcome.Ignored;
        }

        var slot = FindFreeSlot();
        if (slot < 0)
        {
            // XInput itself supports exactly four. Say so plainly rather than
            // letting the phone retry forever into silence.
            replyLength = SessionMessage.Write(reply, MessageType.Welcome, Protocol.NoPad);
            Rejected++;
            return MessageOutcome.SessionRejected;
        }

        // A fresh phone starts its sequence counter wherever it likes, so the
        // gate must forget whatever the previous occupant of this slot reached.
        _gates[slot].Reset();

        var session = new Session(sender, slot) { LastSeen = _clock.GetUtcNow() };
        _sessions[sender] = session;
        _slotTaken[slot] = true;

        _backend.Connect(slot);

        replyLength = SessionMessage.Write(reply, MessageType.Welcome, (byte)slot);
        SessionOpened?.Invoke(this, new SessionEventArgs(sender, slot));
        return MessageOutcome.SessionOpened;
    }

    private int FindFreeSlot()
    {
        for (var i = 0; i < _slotTaken.Length; i++)
            if (!_slotTaken[i]) return i;

        return -1;
    }

    /// <summary>
    /// Drops any session that has gone quiet, zeroing and unplugging its pad.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the block. Without it a phone that runs out of
    /// battery mid-game leaves the server holding "hard left, A pressed" forever,
    /// and the character runs into a wall until the server is killed.
    /// </remarks>
    /// <returns>How many sessions were closed.</returns>
    public int SweepTimeouts()
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();

            _expired.Clear();
            foreach (var (key, session) in _sessions)
                if (now - session.LastSeen >= _timeout)
                    _expired.Add(key);

            // Collected first: closing mutates the dictionary we are walking.
            foreach (var key in _expired)
                CloseSession(_sessions[key], SessionCloseReason.Timeout);

            return _expired.Count;
        }
    }

    /// <summary>Closes every session. For shutdown.</summary>
    public void CloseAll()
    {
        lock (_lock)
        {
            _expired.Clear();
            _expired.AddRange(_sessions.Keys);

            foreach (var key in _expired)
                CloseSession(_sessions[key], SessionCloseReason.ServerShutdown);
        }
    }

    /// <summary>
    /// Ends one session: centre the pad, <em>then</em> unplug it.
    /// </summary>
    /// <remarks>
    /// <b>That order is the single most important thing in this class.</b> Some
    /// games latch the last state they saw when a controller disappears, so
    /// unplugging while a stick is held leaves the input stuck exactly as if
    /// nothing had been cleaned up at all. Every path out of a session — BYE,
    /// timeout, shutdown — goes through here, so the ordering exists in one
    /// place and cannot drift.
    /// </remarks>
    private void CloseSession(Session session, SessionCloseReason reason)
    {
        var neutral = PadState.Neutral;
        _backend.Submit(session.Slot, in neutral);
        _backend.Disconnect(session.Slot);

        _sessions.Remove(session.Client);
        _slotTaken[session.Slot] = false;

        SessionClosed?.Invoke(this, new SessionEventArgs(session.Client, session.Slot, reason));
    }

    /// <summary>
    /// A point-in-time copy of the connected phones, for display.
    /// </summary>
    /// <remarks>
    /// Named for what it costs. This allocates, so it belongs on the once-a-second
    /// status path and nowhere near the receive loop.
    /// </remarks>
    public IReadOnlyList<SessionInfo> Snapshot()
    {
        lock (_lock)
        {
            var list = new List<SessionInfo>(_sessions.Count);
            foreach (var session in _sessions.Values)
                list.Add(new SessionInfo(
                    session.Client, session.Slot, session.LastState, session.Applied));

            list.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return list;
        }
    }
}
