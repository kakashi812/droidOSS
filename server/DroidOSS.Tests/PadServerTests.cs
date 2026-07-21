using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class PadServerTests
{
    private static byte[] Packet(uint sequence, in PadState state, byte pad = 0)
    {
        var buffer = new byte[Protocol.InputPacketSize];
        InputPacket.Write(buffer, pad, sequence, in state);
        return buffer;
    }

    private static PadState SampleState => new()
    {
        Buttons = (ushort)(GamepadButtons.A | GamepadButtons.RightShoulder),
        LeftTrigger = 40,
        RightTrigger = 210,
        ThumbLX = -12345,
        ThumbLY = 23456,
        ThumbRX = 999,
        ThumbRY = -999,
    };

    /// <summary>
    /// The end-to-end path that matters: bytes in, the identical state out to
    /// the driver, with nothing altered along the way.
    /// </summary>
    [Fact]
    public void A_valid_packet_reaches_the_pad_with_every_field_intact()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);
        var expected = SampleState;

        var outcome = server.Handle(Packet(1, in expected));

        Assert.Equal(PacketOutcome.Applied, outcome);
        var (slot, actual) = Assert.Single(backend.Submissions);

        Assert.Equal(0, slot);
        Assert.Equal(expected.Buttons, actual.Buttons);
        Assert.Equal(expected.LeftTrigger, actual.LeftTrigger);
        Assert.Equal(expected.RightTrigger, actual.RightTrigger);
        Assert.Equal(expected.ThumbLX, actual.ThumbLX);
        Assert.Equal(expected.ThumbLY, actual.ThumbLY);
        Assert.Equal(expected.ThumbRX, actual.ThumbRX);
        Assert.Equal(expected.ThumbRY, actual.ThumbRY);
    }

    /// <summary>
    /// The point of the magic byte. A stray datagram must never become
    /// controller movement — that is a character spasming with nothing in the
    /// logs to explain it.
    /// </summary>
    [Fact]
    public void Garbage_never_reaches_the_pad()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);

        Assert.Equal(PacketOutcome.Malformed, server.Handle(new byte[Protocol.InputPacketSize]));
        Assert.Equal(PacketOutcome.Malformed, server.Handle("hello"u8));
        Assert.Equal(PacketOutcome.Malformed, server.Handle([]));

        Assert.Empty(backend.Submissions);
        Assert.Equal(3, server.Malformed);
        Assert.Equal(0, server.Applied);
    }

    [Fact]
    public void Rejects_a_packet_with_the_wrong_magic_byte()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);

        var bytes = Packet(1, SampleState);
        bytes[Protocol.Offset.Magic] = 0x00;

        Assert.Equal(PacketOutcome.Malformed, server.Handle(bytes));
        Assert.Empty(backend.Submissions);
    }

    [Fact]
    public void Rejects_a_packet_from_a_future_protocol_version()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);

        var bytes = Packet(1, SampleState);
        bytes[Protocol.Offset.Version] = Protocol.Version + 1;

        Assert.Equal(PacketOutcome.Malformed, server.Handle(bytes));
        Assert.Empty(backend.Submissions);
    }

    [Theory]
    [InlineData((byte)MessageType.Hello)]
    [InlineData((byte)MessageType.Bye)]
    [InlineData((byte)MessageType.Discover)]
    public void Ignores_messages_that_are_not_input(byte type)
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);

        var bytes = Packet(1, SampleState);
        bytes[Protocol.Offset.Type] = type;

        // These are real message types the server will handle at B3 — but they
        // are not input, so they must never move a stick.
        Assert.Equal(PacketOutcome.Malformed, server.Handle(bytes));
        Assert.Empty(backend.Submissions);
    }

    [Fact]
    public void A_duplicate_packet_is_dropped()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);
        var state = SampleState;

        Assert.Equal(PacketOutcome.Applied, server.Handle(Packet(7, in state)));
        Assert.Equal(PacketOutcome.Stale, server.Handle(Packet(7, in state)));

        Assert.Single(backend.Submissions);
        Assert.Equal(1, server.Stale);
    }

    /// <summary>
    /// The reordering case. Applying the late 58 would jerk the stick back to a
    /// position the thumb has already left.
    /// </summary>
    [Fact]
    public void A_packet_that_arrives_late_is_dropped_and_does_not_block_later_ones()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);
        var state = SampleState;

        Assert.Equal(PacketOutcome.Applied, server.Handle(Packet(57, in state)));
        Assert.Equal(PacketOutcome.Applied, server.Handle(Packet(59, in state)));
        Assert.Equal(PacketOutcome.Stale, server.Handle(Packet(58, in state)));
        Assert.Equal(PacketOutcome.Applied, server.Handle(Packet(60, in state)));

        Assert.Equal(3, backend.Submissions.Count);
        Assert.Equal(3, server.Applied);
        Assert.Equal(1, server.Stale);
    }

    [Fact]
    public void Counters_add_up_across_a_mixed_stream()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);
        var state = SampleState;

        server.Handle(Packet(1, in state));            // applied
        server.Handle(Packet(2, in state));            // applied
        server.Handle(Packet(1, in state));            // stale
        server.Handle(new byte[Protocol.InputPacketSize]);  // malformed
        server.Handle(Packet(3, in state));            // applied
        server.Handle("junk"u8);                       // malformed

        Assert.Equal(3, server.Applied);
        Assert.Equal(1, server.Stale);
        Assert.Equal(2, server.Malformed);
        Assert.Equal(6, server.Total);
        Assert.Equal(3, backend.Submissions.Count);
    }

    [Fact]
    public void Neutralise_centres_everything()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend);
        var state = SampleState;

        server.Handle(Packet(1, in state));
        server.Neutralise();

        var last = backend.LastSubmitted;
        Assert.Equal(0, last.Buttons);
        Assert.Equal(0, last.ThumbLX);
        Assert.Equal(0, last.ThumbLY);
        Assert.Equal(0, last.LeftTrigger);
        Assert.Equal(0, last.RightTrigger);
    }

    [Fact]
    public void Submits_to_whichever_slot_it_was_given()
    {
        var backend = new FakePadBackend();
        var server = new PadServer(backend, slot: 2);
        var state = SampleState;

        server.Handle(Packet(1, in state));

        var (slot, _) = Assert.Single(backend.Submissions);
        Assert.Equal(2, slot);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void Refuses_a_slot_outside_the_four_XInput_supports(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PadServer(new FakePadBackend(), slot));
    }
}
