using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class InputPacketTests
{
    /// <summary>
    /// The golden vector — a packet whose every field has a distinctive value,
    /// and the exact 20 bytes it must produce.
    /// </summary>
    /// <remarks>
    /// <c>py tools/fake_phone.py --selftest</c> builds this same packet from an
    /// independent Python implementation and prints its hex. If the two ever
    /// disagree, the protocol has drifted between languages — which is exactly
    /// the failure that produces no error message, only a controller that
    /// behaves oddly.
    ///
    /// Keep this in sync with GOLDEN_* in tools/fake_phone.py.
    /// </remarks>
    private const byte GoldenPad = 2;

    private const uint GoldenSequence = 0x12345678;

    private static PadState GoldenState => new()
    {
        Buttons = 0x1004,          // A + DPadLeft
        LeftTrigger = 0x20,        // 32
        RightTrigger = 0xC8,       // 200
        ThumbLX = 0x03E8,          // 1000
        ThumbLY = -1000,           // 0xFC18
        ThumbRX = short.MaxValue,  // 32767
        ThumbRY = short.MinValue,  // -32768
    };

    private static readonly byte[] GoldenBytes =
    [
        0xDA,                     // magic
        0x01,                     // version
        0x01,                     // type = INPUT
        0x02,                     // pad
        0x78, 0x56, 0x34, 0x12,   // sequence 0x12345678, little-endian
        0x04, 0x10,               // buttons 0x1004
        0x20,                     // left trigger
        0xC8,                     // right trigger
        0xE8, 0x03,               // LX  1000
        0x18, 0xFC,               // LY -1000
        0xFF, 0x7F,               // RX  32767
        0x00, 0x80,               // RY -32768
    ];

    [Fact]
    public void Golden_vector_encodes_to_exactly_these_bytes()
    {
        Span<byte> buffer = stackalloc byte[Protocol.InputPacketSize];

        var written = InputPacket.Write(buffer, GoldenPad, GoldenSequence, GoldenState);

        Assert.Equal(Protocol.InputPacketSize, written);
        Assert.Equal(GoldenBytes, buffer.ToArray());
    }

    [Fact]
    public void Golden_vector_decodes_back_to_the_same_values()
    {
        Assert.True(InputPacket.TryRead(GoldenBytes, out var packet));

        Assert.Equal(GoldenPad, packet.Pad);
        Assert.Equal(GoldenSequence, packet.Sequence);

        var expected = GoldenState;
        Assert.Equal(expected.Buttons, packet.State.Buttons);
        Assert.Equal(expected.LeftTrigger, packet.State.LeftTrigger);
        Assert.Equal(expected.RightTrigger, packet.State.RightTrigger);
        Assert.Equal(expected.ThumbLX, packet.State.ThumbLX);
        Assert.Equal(expected.ThumbLY, packet.State.ThumbLY);
        Assert.Equal(expected.ThumbRX, packet.State.ThumbRX);
        Assert.Equal(expected.ThumbRY, packet.State.ThumbRY);
    }

    /// <summary>
    /// The endianness trap, made explicit. 1000 is 0x03E8; little-endian puts
    /// the low byte first. Get this backwards and the value silently becomes
    /// 59395 instead — no exception, just a stick behaving bizarrely.
    /// </summary>
    [Fact]
    public void Multibyte_fields_are_little_endian()
    {
        Span<byte> buffer = stackalloc byte[Protocol.InputPacketSize];
        var state = PadState.Neutral;
        state.ThumbLX = 1000;   // 0x03E8

        InputPacket.Write(buffer, pad: 0, sequence: 1, in state);

        Assert.Equal(0xE8, buffer[Protocol.Offset.ThumbLX]);
        Assert.Equal(0x03, buffer[Protocol.Offset.ThumbLX + 1]);

        // And the sequence, which is four bytes rather than two.
        InputPacket.Write(buffer, pad: 0, sequence: 0x01020304, in state);
        Assert.Equal(0x04, buffer[Protocol.Offset.Sequence]);
        Assert.Equal(0x03, buffer[Protocol.Offset.Sequence + 1]);
        Assert.Equal(0x02, buffer[Protocol.Offset.Sequence + 2]);
        Assert.Equal(0x01, buffer[Protocol.Offset.Sequence + 3]);
    }

    [Theory]
    [InlineData(0, 0u, (ushort)0, (byte)0, (byte)0, (short)0, (short)0, (short)0, (short)0)]
    [InlineData(3, uint.MaxValue, (ushort)0xFFFF, (byte)255, (byte)255,
        short.MinValue, short.MaxValue, short.MinValue, short.MaxValue)]
    [InlineData(1, 1u, (ushort)0x8000, (byte)1, (byte)254, (short)-1, (short)1, (short)-32768, (short)32767)]
    public void Round_trip_preserves_every_field(
        byte pad, uint sequence, ushort buttons, byte lt, byte rt,
        short lx, short ly, short rx, short ry)
    {
        var original = new PadState
        {
            Buttons = buttons,
            LeftTrigger = lt,
            RightTrigger = rt,
            ThumbLX = lx,
            ThumbLY = ly,
            ThumbRX = rx,
            ThumbRY = ry,
        };

        Span<byte> buffer = stackalloc byte[Protocol.InputPacketSize];
        InputPacket.Write(buffer, pad, sequence, in original);

        Assert.True(InputPacket.TryRead(buffer, out var packet));

        Assert.Equal(pad, packet.Pad);
        Assert.Equal(sequence, packet.Sequence);
        Assert.Equal(original.Buttons, packet.State.Buttons);
        Assert.Equal(original.LeftTrigger, packet.State.LeftTrigger);
        Assert.Equal(original.RightTrigger, packet.State.RightTrigger);
        Assert.Equal(original.ThumbLX, packet.State.ThumbLX);
        Assert.Equal(original.ThumbLY, packet.State.ThumbLY);
        Assert.Equal(original.ThumbRX, packet.State.ThumbRX);
        Assert.Equal(original.ThumbRY, packet.State.ThumbRY);
    }

    /// <summary>
    /// Anything that isn't unmistakably ours gets dropped without further
    /// thought. This is what stops a port scan from moving the character.
    /// </summary>
    [Fact]
    public void Rejects_a_wrong_magic_byte()
    {
        var bytes = (byte[])GoldenBytes.Clone();
        bytes[Protocol.Offset.Magic] = 0xAB;

        Assert.False(InputPacket.TryRead(bytes, out _));
    }

    [Fact]
    public void Rejects_an_unknown_protocol_version()
    {
        var bytes = (byte[])GoldenBytes.Clone();
        bytes[Protocol.Offset.Version] = 99;

        Assert.False(InputPacket.TryRead(bytes, out _));
    }

    [Theory]
    [InlineData((byte)MessageType.Hello)]
    [InlineData((byte)MessageType.Welcome)]
    [InlineData((byte)MessageType.Bye)]
    [InlineData((byte)MessageType.Rumble)]
    [InlineData((byte)MessageType.Discover)]
    [InlineData((byte)0x99)]
    public void Rejects_anything_that_is_not_an_input_message(byte type)
    {
        var bytes = (byte[])GoldenBytes.Clone();
        bytes[Protocol.Offset.Type] = type;

        Assert.False(InputPacket.TryRead(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]   // one short
    [InlineData(21)]   // one long
    [InlineData(64)]
    public void Rejects_a_buffer_of_the_wrong_length(int length)
    {
        var bytes = new byte[length];
        if (length > Protocol.Offset.Type)
        {
            bytes[Protocol.Offset.Magic] = Protocol.MagicByte;
            bytes[Protocol.Offset.Version] = Protocol.Version;
            bytes[Protocol.Offset.Type] = (byte)MessageType.Input;
        }

        Assert.False(InputPacket.TryRead(bytes, out _));
    }

    [Fact]
    public void Write_refuses_a_buffer_that_is_too_small()
    {
        var state = PadState.Neutral;
        var tooSmall = new byte[Protocol.InputPacketSize - 1];

        Assert.Throws<ArgumentException>(() =>
            InputPacket.Write(tooSmall, pad: 0, sequence: 0, in state));
    }
}
