namespace DroidOSS.Core;

/// <summary>An sRGB colour, one byte per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public bool IsBlack => R == 0 && G == 0 && B == 0;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The colour each player number is shown in, when the game itself gives none.
/// </summary>
/// <remarks>
/// The PlayStation convention — blue, red, green, pink — because it is the one
/// players already associate with "player 1, 2, 3, 4", and the one a
/// DualShock 4 lights up in by default.
/// </remarks>
public static class PlayerColors
{
    public static readonly Rgb Player1 = new(0x3D, 0x7E, 0xFF);
    public static readonly Rgb Player2 = new(0xFF, 0x4A, 0x4A);
    public static readonly Rgb Player3 = new(0x3D, 0xDC, 0x6A);
    public static readonly Rgb Player4 = new(0xFF, 0x5C, 0xCB);

    /// <summary>The colour for player 1–4; anything else gets player 1's.</summary>
    public static Rgb For(int player) => player switch
    {
        2 => Player2,
        3 => Player3,
        4 => Player4,
        _ => Player1,
    };
}

/// <summary>
/// The two messages the PC sends back to a phone: RUMBLE, what the game wants
/// the motors doing, and LIGHT, which player the phone is and what colour to
/// show for it.
/// </summary>
/// <remarks>
/// <code>
/// RUMBLE  byte 0     1    2     3    4      5
///             0xDA  ver  0x05  pad  large  small         6 bytes
///
/// LIGHT   byte 0     1    2     3    4       5  6  7
///             0xDA  ver  0x07  pad  player  R  G  B      8 bytes
/// </code>
///
/// <c>pad</c> is the slot the server gave the phone, for information. Both
/// messages are <b>state, not events</b>: each says what should be happening
/// now, so a lost one is repaired by the next, and the server repeats them
/// (see <see cref="FeedbackScheduler"/>). The lengths differ from every other
/// message, so neither can be mistaken for a WELCOME.
/// </remarks>
public static class FeedbackMessage
{
    public const int RumbleSize = Protocol.HeaderSize + 2;
    public const int LightSize = Protocol.HeaderSize + 4;

    private const int LargeOffset = Protocol.HeaderSize;
    private const int SmallOffset = LargeOffset + 1;
    private const int PlayerOffset = Protocol.HeaderSize;
    private const int RedOffset = PlayerOffset + 1;

    /// <returns>Bytes written, always <see cref="RumbleSize"/>.</returns>
    public static int WriteRumble(Span<byte> buffer, byte pad, byte largeMotor, byte smallMotor)
    {
        WriteHeader(buffer, MessageType.Rumble, pad, RumbleSize);
        buffer[LargeOffset] = largeMotor;
        buffer[SmallOffset] = smallMotor;
        return RumbleSize;
    }

    /// <returns>Bytes written, always <see cref="LightSize"/>.</returns>
    public static int WriteLight(Span<byte> buffer, byte pad, byte player, Rgb colour)
    {
        WriteHeader(buffer, MessageType.Light, pad, LightSize);
        buffer[PlayerOffset] = player;
        buffer[RedOffset] = colour.R;
        buffer[RedOffset + 1] = colour.G;
        buffer[RedOffset + 2] = colour.B;
        return LightSize;
    }

    /// <summary>Parses a RUMBLE. The server never receives one; the tests and tools do.</summary>
    public static bool TryReadRumble(ReadOnlySpan<byte> buffer, out byte pad, out byte largeMotor, out byte smallMotor)
    {
        pad = largeMotor = smallMotor = 0;
        if (!HasHeader(buffer, MessageType.Rumble, RumbleSize)) return false;

        pad = buffer[Protocol.Offset.Pad];
        largeMotor = buffer[LargeOffset];
        smallMotor = buffer[SmallOffset];
        return true;
    }

    /// <summary>Parses a LIGHT. The server never receives one; the tests and tools do.</summary>
    public static bool TryReadLight(ReadOnlySpan<byte> buffer, out byte pad, out byte player, out Rgb colour)
    {
        pad = player = 0;
        colour = default;
        if (!HasHeader(buffer, MessageType.Light, LightSize)) return false;

        pad = buffer[Protocol.Offset.Pad];
        player = buffer[PlayerOffset];
        colour = new Rgb(buffer[RedOffset], buffer[RedOffset + 1], buffer[RedOffset + 2]);
        return true;
    }

    private static void WriteHeader(Span<byte> buffer, MessageType type, byte pad, int size)
    {
        if (buffer.Length < size)
            throw new ArgumentException($"Need at least {size} bytes, got {buffer.Length}.", nameof(buffer));

        buffer[Protocol.Offset.Magic] = Protocol.MagicByte;
        buffer[Protocol.Offset.Version] = Protocol.Version;
        buffer[Protocol.Offset.Type] = (byte)type;
        buffer[Protocol.Offset.Pad] = pad;
    }

    private static bool HasHeader(ReadOnlySpan<byte> buffer, MessageType type, int size) =>
        buffer.Length == size
        && buffer[Protocol.Offset.Magic] == Protocol.MagicByte
        && buffer[Protocol.Offset.Version] == Protocol.Version
        && buffer[Protocol.Offset.Type] == (byte)type;
}
