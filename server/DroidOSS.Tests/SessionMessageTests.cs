using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class SessionMessageTests
{
    // Golden vectors, derived by hand from docs/PROTOCOL.md rather than produced
    // by the code under test. A round-trip test only proves the writer and
    // reader agree with each other — if both are wrong in the same direction it
    // passes perfectly. These are the external anchor, and tools/fake_phone.py
    // asserts the same bytes from an independent implementation.
    private static readonly byte[] GoldenHello = [0xDA, 0x01, 0x02, 0xFF];
    private static readonly byte[] GoldenWelcome = [0xDA, 0x01, 0x03, 0x01];
    private static readonly byte[] GoldenWelcomeFull = [0xDA, 0x01, 0x03, 0xFF];
    private static readonly byte[] GoldenBye = [0xDA, 0x01, 0x04, 0x01];

    [Fact]
    public void Hello_matches_the_golden_bytes()
    {
        var buffer = new byte[Protocol.SessionMessageSize];
        var written = SessionMessage.Write(buffer, MessageType.Hello, Protocol.NoPad);

        Assert.Equal(Protocol.SessionMessageSize, written);
        Assert.Equal(GoldenHello, buffer);
    }

    [Fact]
    public void Welcome_matches_the_golden_bytes()
    {
        var buffer = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(buffer, MessageType.Welcome, 1);

        Assert.Equal(GoldenWelcome, buffer);
    }

    /// <summary>
    /// The "server full" reply. It reuses WELCOME with an impossible slot rather
    /// than introducing a seventh message type for a case the phone must already
    /// be able to receive.
    /// </summary>
    [Fact]
    public void A_rejecting_welcome_carries_the_no_pad_marker()
    {
        var buffer = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(buffer, MessageType.Welcome, Protocol.NoPad);

        Assert.Equal(GoldenWelcomeFull, buffer);
    }

    [Fact]
    public void Bye_matches_the_golden_bytes()
    {
        var buffer = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(buffer, MessageType.Bye, 1);

        Assert.Equal(GoldenBye, buffer);
    }

    [Theory]
    [InlineData(MessageType.Hello, Protocol.NoPad)]
    [InlineData(MessageType.Welcome, (byte)0)]
    [InlineData(MessageType.Welcome, (byte)3)]
    [InlineData(MessageType.Bye, (byte)2)]
    public void Round_trips(MessageType type, byte pad)
    {
        var buffer = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(buffer, type, pad);

        Assert.True(SessionMessage.TryRead(buffer, out var readType, out var readPad));
        Assert.Equal(type, readType);
        Assert.Equal(pad, readPad);
    }

    [Fact]
    public void Rejects_the_wrong_magic_byte()
    {
        var buffer = GoldenHello.ToArray();
        buffer[Protocol.Offset.Magic] = 0x00;

        Assert.False(SessionMessage.TryRead(buffer, out _, out _));
    }

    [Fact]
    public void Rejects_an_unknown_version()
    {
        var buffer = GoldenHello.ToArray();
        buffer[Protocol.Offset.Version] = Protocol.Version + 1;

        Assert.False(SessionMessage.TryRead(buffer, out _, out _));
    }

    /// <summary>
    /// INPUT is well-formed but belongs to <see cref="InputPacket"/>. The two
    /// readers must not overlap, or a 20-byte packet could be read as a session
    /// message and vice versa.
    /// </summary>
    [Theory]
    [InlineData((byte)MessageType.Input)]
    [InlineData((byte)MessageType.Rumble)]
    [InlineData((byte)MessageType.Discover)]
    [InlineData((byte)0x99)]
    public void Rejects_types_that_are_not_session_messages(byte type)
    {
        var buffer = GoldenHello.ToArray();
        buffer[Protocol.Offset.Type] = type;

        Assert.False(SessionMessage.TryRead(buffer, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(64)]
    public void Rejects_anything_that_is_not_exactly_four_bytes(int length)
    {
        var buffer = new byte[length];
        if (length >= Protocol.SessionMessageSize)
            SessionMessage.Write(buffer, MessageType.Hello, Protocol.NoPad);

        Assert.False(SessionMessage.TryRead(buffer, out _, out _));
    }

    [Fact]
    public void Writing_into_too_small_a_buffer_throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> tiny = stackalloc byte[Protocol.SessionMessageSize - 1];
            SessionMessage.Write(tiny, MessageType.Hello, Protocol.NoPad);
        });
    }

    /// <summary>
    /// An INPUT packet must never parse as a session message, and a session
    /// message must never parse as INPUT. They are told apart by length alone,
    /// so this is the assertion that keeps that true.
    /// </summary>
    [Fact]
    public void The_two_readers_never_both_accept_the_same_bytes()
    {
        var hello = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(hello, MessageType.Hello, Protocol.NoPad);

        var input = new byte[Protocol.InputPacketSize];
        InputPacket.Write(input, 0, 1, PadState.Neutral);

        Assert.True(SessionMessage.TryRead(hello, out _, out _));
        Assert.False(InputPacket.TryRead(hello, out _));

        Assert.True(InputPacket.TryRead(input, out _));
        Assert.False(SessionMessage.TryRead(input, out _, out _));
    }
}
