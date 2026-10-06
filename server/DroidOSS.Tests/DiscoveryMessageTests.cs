using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class DiscoveryMessageTests
{
    // Golden vectors, derived by hand from docs/PROTOCOL.md. tools/fake_phone.py
    // and the Android PacketCodecTest assert the same bytes independently.
    private static readonly byte[] GoldenDiscover = [0xDA, 0x01, 0x06, 0xFF];

    // Server "PC", input port 27500 (0x6B6C, little-endian), 3 pads free.
    private static readonly byte[] GoldenAnnounce =
        [0xDA, 0x01, 0x03, 0xFF, 0x6C, 0x6B, 0x03, 0x02, 0x50, 0x43];

    [Fact]
    public void Discover_matches_the_golden_bytes()
    {
        var buffer = new byte[DiscoveryMessage.DiscoverSize];
        DiscoveryMessage.WriteDiscover(buffer);

        Assert.Equal(GoldenDiscover, buffer);
        Assert.True(DiscoveryMessage.IsDiscover(buffer));
    }

    [Fact]
    public void Announce_matches_the_golden_bytes()
    {
        var buffer = new byte[DiscoveryMessage.MaxAnnounceSize];
        var written = DiscoveryMessage.WriteAnnounce(buffer, "PC", Protocol.InputPort, 3);

        Assert.Equal(GoldenAnnounce, buffer.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void Announce_round_trips()
    {
        var buffer = new byte[DiscoveryMessage.MaxAnnounceSize];
        var written = DiscoveryMessage.WriteAnnounce(buffer, "Living-room PC", 27500, 1);

        Assert.True(DiscoveryMessage.TryReadAnnounce(
            buffer.AsSpan(0, written), out var name, out var port, out var free));
        Assert.Equal("Living-room PC", name);
        Assert.Equal((ushort)27500, port);
        Assert.Equal(1, free);
    }

    /// <summary>
    /// The announcement must never be mistaken for a session WELCOME, which is
    /// exactly four bytes. Even an empty name keeps it longer than that.
    /// </summary>
    [Fact]
    public void An_announcement_is_never_a_session_message()
    {
        var buffer = new byte[DiscoveryMessage.MaxAnnounceSize];
        var written = DiscoveryMessage.WriteAnnounce(buffer, "", Protocol.InputPort, 4);

        Assert.True(written > Protocol.SessionMessageSize);
        Assert.False(SessionMessage.TryRead(buffer.AsSpan(0, written), out _, out _));
    }

    [Fact]
    public void Long_names_are_cut_without_splitting_a_character()
    {
        // 40 two-byte characters: 80 bytes, over the 64-byte limit.
        var name = new string('é', 40);
        var buffer = new byte[DiscoveryMessage.MaxAnnounceSize];
        var written = DiscoveryMessage.WriteAnnounce(buffer, name, Protocol.InputPort, 4);

        Assert.True(DiscoveryMessage.TryReadAnnounce(
            buffer.AsSpan(0, written), out var read, out _, out _));
        Assert.Equal(new string('é', 32), read);
    }

    [Fact]
    public void Free_pads_are_clamped_to_what_xinput_allows()
    {
        var buffer = new byte[DiscoveryMessage.MaxAnnounceSize];
        var written = DiscoveryMessage.WriteAnnounce(buffer, "PC", Protocol.InputPort, 9);

        Assert.True(DiscoveryMessage.TryReadAnnounce(
            buffer.AsSpan(0, written), out _, out _, out var free));
        Assert.Equal(IPadBackend.MaxPads, free);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Discover_must_be_exactly_four_bytes(int length)
    {
        var buffer = new byte[length];
        if (length >= DiscoveryMessage.DiscoverSize) DiscoveryMessage.WriteDiscover(buffer);

        Assert.False(DiscoveryMessage.IsDiscover(buffer));
    }

    [Theory]
    [InlineData(Protocol.Offset.Magic, 0x00)]
    [InlineData(Protocol.Offset.Version, Protocol.Version + 1)]
    [InlineData(Protocol.Offset.Type, (int)MessageType.Hello)]
    public void A_corrupted_discover_is_ignored(int offset, int value)
    {
        var buffer = GoldenDiscover.ToArray();
        buffer[offset] = (byte)value;

        Assert.False(DiscoveryMessage.IsDiscover(buffer));
    }

    [Fact]
    public void An_announcement_whose_length_disagrees_with_its_name_is_rejected()
    {
        var truncated = GoldenAnnounce.AsSpan(0, GoldenAnnounce.Length - 1).ToArray();

        Assert.False(DiscoveryMessage.TryReadAnnounce(truncated, out _, out _, out _));
    }
}
