using System.Buffers.Binary;
using System.Text;

namespace DroidOSS.Core;

/// <summary>
/// The two messages that let a phone find servers without anyone typing an
/// address: DISCOVER, broadcast by the phone, and the WELCOME announcement each
/// server answers it with.
/// </summary>
/// <remarks>
/// Both travel on <see cref="Protocol.DiscoveryPort"/> only, never the input
/// port, so neither can be confused with a session message even though the
/// announcement reuses the WELCOME type byte.
///
/// The announcement carries a payload, unlike a session WELCOME, because a phone
/// that hears from several servers needs something to show the person choosing
/// between them:
///
/// <code>
/// byte  0     1    2     3      4-5        6        7          8..
///     0xDA  ver  0x03  0xFF  inputPort  freePads  nameLength  name (UTF-8)
///                            u16        u8        u8
/// </code>
///
/// The pad byte is always <see cref="Protocol.NoPad"/>: no slot is assigned by
/// an announcement. The phone still says HELLO on the input port to get one.
/// </remarks>
public static class DiscoveryMessage
{
    /// <summary>DISCOVER is the header and nothing else.</summary>
    public const int DiscoverSize = Protocol.HeaderSize;

    /// <summary>Header + port + free pads + name length, before the name itself.</summary>
    public const int AnnounceFixedSize = Protocol.HeaderSize + 4;

    /// <summary>Longest name an announcement carries, in UTF-8 bytes.</summary>
    public const int MaxNameBytes = 64;

    /// <summary>Largest possible announcement.</summary>
    public const int MaxAnnounceSize = AnnounceFixedSize + MaxNameBytes;

    private const int PortOffset = Protocol.HeaderSize;
    private const int FreePadsOffset = PortOffset + 2;
    private const int NameLengthOffset = FreePadsOffset + 1;
    private const int NameOffset = NameLengthOffset + 1;

    /// <summary>True if this is a well-formed DISCOVER from a phone.</summary>
    public static bool IsDiscover(ReadOnlySpan<byte> buffer) =>
        buffer.Length == DiscoverSize
        && buffer[Protocol.Offset.Magic] == Protocol.MagicByte
        && buffer[Protocol.Offset.Version] == Protocol.Version
        && buffer[Protocol.Offset.Type] == (byte)MessageType.Discover;

    /// <summary>Writes a DISCOVER. Only the tests and tools send one; the server answers them.</summary>
    /// <returns>Bytes written, always <see cref="DiscoverSize"/>.</returns>
    public static int WriteDiscover(Span<byte> buffer)
    {
        if (buffer.Length < DiscoverSize)
            throw new ArgumentException(
                $"Need at least {DiscoverSize} bytes, got {buffer.Length}.", nameof(buffer));

        buffer[Protocol.Offset.Magic] = Protocol.MagicByte;
        buffer[Protocol.Offset.Version] = Protocol.Version;
        buffer[Protocol.Offset.Type] = (byte)MessageType.Discover;
        buffer[Protocol.Offset.Pad] = Protocol.NoPad;
        return DiscoverSize;
    }

    /// <summary>
    /// Writes the announcement a server sends in answer to DISCOVER.
    /// </summary>
    /// <param name="name">Shown on the phone. Truncated to <see cref="MaxNameBytes"/> on a character boundary.</param>
    /// <param name="inputPort">Where the phone should send HELLO.</param>
    /// <param name="freePads">Slots still available, so a full server can be shown as such before anyone tries it.</param>
    /// <returns>Bytes written.</returns>
    public static int WriteAnnounce(Span<byte> buffer, string name, ushort inputPort, int freePads)
    {
        ArgumentNullException.ThrowIfNull(name);

        name = Truncate(name, MaxNameBytes);
        var nameBytes = Encoding.UTF8.GetByteCount(name);
        var total = AnnounceFixedSize + nameBytes;

        if (buffer.Length < total)
            throw new ArgumentException(
                $"Need at least {total} bytes, got {buffer.Length}.", nameof(buffer));

        buffer[Protocol.Offset.Magic] = Protocol.MagicByte;
        buffer[Protocol.Offset.Version] = Protocol.Version;
        buffer[Protocol.Offset.Type] = (byte)MessageType.Welcome;
        buffer[Protocol.Offset.Pad] = Protocol.NoPad;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer[PortOffset..], inputPort);
        buffer[FreePadsOffset] = (byte)Math.Clamp(freePads, 0, IPadBackend.MaxPads);
        buffer[NameLengthOffset] = (byte)nameBytes;
        Encoding.UTF8.GetBytes(name, buffer.Slice(NameOffset, nameBytes));

        return total;
    }

    /// <summary>Parses an announcement. The server never needs this; the tests do.</summary>
    /// <returns><c>false</c> if this is not a well-formed announcement.</returns>
    public static bool TryReadAnnounce(
        ReadOnlySpan<byte> buffer, out string name, out ushort inputPort, out int freePads)
    {
        name = "";
        inputPort = 0;
        freePads = 0;

        if (buffer.Length < AnnounceFixedSize) return false;
        if (buffer[Protocol.Offset.Magic] != Protocol.MagicByte) return false;
        if (buffer[Protocol.Offset.Version] != Protocol.Version) return false;
        if (buffer[Protocol.Offset.Type] != (byte)MessageType.Welcome) return false;

        var nameLength = buffer[NameLengthOffset];
        if (nameLength > MaxNameBytes) return false;
        if (buffer.Length != AnnounceFixedSize + nameLength) return false;

        inputPort = BinaryPrimitives.ReadUInt16LittleEndian(buffer[PortOffset..]);
        freePads = buffer[FreePadsOffset];
        name = Encoding.UTF8.GetString(buffer.Slice(NameOffset, nameLength));
        return true;
    }

    /// <summary>Shortens to at most <paramref name="maxBytes"/> of UTF-8 without splitting a character.</summary>
    private static string Truncate(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

        var end = value.Length;
        while (end > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, end)) > maxBytes)
            end--;

        // Never leave half a surrogate pair dangling.
        if (end > 0 && char.IsHighSurrogate(value[end - 1])) end--;

        return value[..end];
    }
}
