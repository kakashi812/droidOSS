using System.Buffers.Binary;

namespace DroidOSS.Core;

/// <summary>
/// One INPUT message: which pad it came from, its ordering number, and the
/// complete controller state at that instant.
/// </summary>
/// <remarks>
/// Reading and writing never allocate — both work over a caller-supplied span,
/// so the server can reuse a single receive buffer forever. That matters because
/// this runs 125 times a second per connected phone, and garbage collection is
/// one of the few things that can cause a visible latency spike.
///
/// Every multi-byte field is explicitly little-endian. See
/// <c>docs/PROTOCOL.md</c> — Kotlin's ByteBuffer defaults the other way, which
/// is the single easiest way to break this protocol silently.
/// </remarks>
public readonly struct InputPacket
{
    /// <summary>Which of the four virtual pads this belongs to.</summary>
    public byte Pad { get; }

    /// <summary>Monotonic counter, incremented once per packet sent.</summary>
    public uint Sequence { get; }

    /// <summary>The controller state carried by this packet.</summary>
    public PadState State { get; }

    private InputPacket(byte pad, uint sequence, in PadState state)
    {
        Pad = pad;
        Sequence = sequence;
        State = state;
    }

    /// <summary>
    /// Parses an INPUT packet, rejecting anything that isn't one.
    /// </summary>
    /// <remarks>
    /// Validation happens before any field is read. Garbage must be discarded,
    /// never interpreted — a stray broadcast read as controller state makes the
    /// character spasm, with nothing in the logs to explain it.
    /// </remarks>
    /// <returns><c>false</c> if the buffer is not a well-formed INPUT packet.</returns>
    public static bool TryRead(ReadOnlySpan<byte> buffer, out InputPacket packet)
    {
        packet = default;

        if (buffer.Length != Protocol.InputPacketSize) return false;
        if (buffer[Protocol.Offset.Magic] != Protocol.MagicByte) return false;
        if (buffer[Protocol.Offset.Version] != Protocol.Version) return false;
        if (buffer[Protocol.Offset.Type] != (byte)MessageType.Input) return false;

        var state = new PadState
        {
            Buttons = BinaryPrimitives.ReadUInt16LittleEndian(buffer[Protocol.Offset.Buttons..]),
            LeftTrigger = buffer[Protocol.Offset.LeftTrigger],
            RightTrigger = buffer[Protocol.Offset.RightTrigger],
            ThumbLX = BinaryPrimitives.ReadInt16LittleEndian(buffer[Protocol.Offset.ThumbLX..]),
            ThumbLY = BinaryPrimitives.ReadInt16LittleEndian(buffer[Protocol.Offset.ThumbLY..]),
            ThumbRX = BinaryPrimitives.ReadInt16LittleEndian(buffer[Protocol.Offset.ThumbRX..]),
            ThumbRY = BinaryPrimitives.ReadInt16LittleEndian(buffer[Protocol.Offset.ThumbRY..]),
        };

        packet = new InputPacket(
            buffer[Protocol.Offset.Pad],
            BinaryPrimitives.ReadUInt32LittleEndian(buffer[Protocol.Offset.Sequence..]),
            in state);

        return true;
    }

    /// <summary>
    /// Writes an INPUT packet into <paramref name="buffer"/>.
    /// </summary>
    /// <remarks>
    /// The server does not need this in normal operation — the phone sends, the
    /// server receives. It exists so the round trip can be tested, and so tools
    /// can generate traffic.
    /// </remarks>
    /// <returns>Bytes written, always <see cref="Protocol.InputPacketSize"/>.</returns>
    /// <exception cref="ArgumentException">The buffer is too small.</exception>
    public static int Write(Span<byte> buffer, byte pad, uint sequence, in PadState state)
    {
        if (buffer.Length < Protocol.InputPacketSize)
            throw new ArgumentException(
                $"Need at least {Protocol.InputPacketSize} bytes, got {buffer.Length}.",
                nameof(buffer));

        buffer[Protocol.Offset.Magic] = Protocol.MagicByte;
        buffer[Protocol.Offset.Version] = Protocol.Version;
        buffer[Protocol.Offset.Type] = (byte)MessageType.Input;
        buffer[Protocol.Offset.Pad] = pad;

        BinaryPrimitives.WriteUInt32LittleEndian(buffer[Protocol.Offset.Sequence..], sequence);

        BinaryPrimitives.WriteUInt16LittleEndian(buffer[Protocol.Offset.Buttons..], state.Buttons);
        buffer[Protocol.Offset.LeftTrigger] = state.LeftTrigger;
        buffer[Protocol.Offset.RightTrigger] = state.RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(buffer[Protocol.Offset.ThumbLX..], state.ThumbLX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[Protocol.Offset.ThumbLY..], state.ThumbLY);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[Protocol.Offset.ThumbRX..], state.ThumbRX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer[Protocol.Offset.ThumbRY..], state.ThumbRY);

        return Protocol.InputPacketSize;
    }
}
