namespace DroidOSS.Core;

/// <summary>
/// The header-only messages that open and close a session: HELLO, WELCOME, BYE.
/// </summary>
/// <remarks>
/// These carry no payload. A four-byte header already says everything: which
/// protocol, which version, which kind of message, and which pad. Anything more
/// would be inventing structure for its own sake.
///
/// Note what is <em>not</em> here: a sequence number. Ordering only matters for
/// the 125 Hz stream of snapshots, where a late packet would jerk the stick
/// backwards. A session message is a one-off announcement, and a duplicate HELLO
/// is handled by giving the phone back the slot it already has.
///
/// Deliberately mirrors <see cref="InputPacket"/> — same validation order, same
/// span-based no-allocation shape.
/// </remarks>
public static class SessionMessage
{
    /// <summary>
    /// Parses HELLO, WELCOME or BYE, rejecting anything else.
    /// </summary>
    /// <remarks>
    /// Validates before reading, exactly like <see cref="InputPacket.TryRead"/>:
    /// length, magic, version, and only then the type. An INPUT packet is
    /// correctly rejected here — it is well-formed but belongs to the other
    /// reader.
    /// </remarks>
    /// <returns><c>false</c> if this is not a well-formed session message.</returns>
    public static bool TryRead(ReadOnlySpan<byte> buffer, out MessageType type, out byte pad)
    {
        type = default;
        pad = default;

        if (buffer.Length != Protocol.SessionMessageSize) return false;
        if (buffer[Protocol.Offset.Magic] != Protocol.MagicByte) return false;
        if (buffer[Protocol.Offset.Version] != Protocol.Version) return false;

        var candidate = (MessageType)buffer[Protocol.Offset.Type];
        if (candidate is not (MessageType.Hello or MessageType.Welcome or MessageType.Bye))
            return false;

        type = candidate;
        pad = buffer[Protocol.Offset.Pad];
        return true;
    }

    /// <summary>
    /// Writes a session message into <paramref name="buffer"/>.
    /// </summary>
    /// <returns>Bytes written, always <see cref="Protocol.SessionMessageSize"/>.</returns>
    /// <exception cref="ArgumentException">The buffer is too small.</exception>
    public static int Write(Span<byte> buffer, MessageType type, byte pad)
    {
        if (buffer.Length < Protocol.SessionMessageSize)
            throw new ArgumentException(
                $"Need at least {Protocol.SessionMessageSize} bytes, got {buffer.Length}.",
                nameof(buffer));

        buffer[Protocol.Offset.Magic] = Protocol.MagicByte;
        buffer[Protocol.Offset.Version] = Protocol.Version;
        buffer[Protocol.Offset.Type] = (byte)type;
        buffer[Protocol.Offset.Pad] = pad;

        return Protocol.SessionMessageSize;
    }
}
