using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class PadStateTests
{
    /// <summary>
    /// The whole zero-conversion design rests on this. If a field is added,
    /// reordered, or padded, the struct stops matching bytes 8–19 of the input
    /// packet and the wire protocol silently breaks — a stick that moves when you
    /// press B, with no exception anywhere.
    /// </summary>
    [Fact]
    public void PadState_is_exactly_twelve_bytes()
    {
        Assert.Equal(12, Marshal.SizeOf<PadState>());
        Assert.Equal(12, Unsafe.SizeOf<PadState>());
    }

    /// <summary>
    /// Field offsets must match XINPUT_GAMEPAD exactly, in the order the packet
    /// carries them.
    /// </summary>
    [Theory]
    [InlineData(nameof(PadState.Buttons), 0)]
    [InlineData(nameof(PadState.LeftTrigger), 2)]
    [InlineData(nameof(PadState.RightTrigger), 3)]
    [InlineData(nameof(PadState.ThumbLX), 4)]
    [InlineData(nameof(PadState.ThumbLY), 6)]
    [InlineData(nameof(PadState.ThumbRX), 8)]
    [InlineData(nameof(PadState.ThumbRY), 10)]
    public void Field_sits_at_its_protocol_offset(string field, int expectedOffset)
    {
        var actual = Marshal.OffsetOf<PadState>(field).ToInt32();
        Assert.Equal(expectedOffset, actual);
    }

    /// <summary>
    /// Neutral is what gets submitted before unplugging a pad, so "all zeros"
    /// is a correctness requirement rather than a convenience.
    /// </summary>
    [Fact]
    public void Neutral_has_everything_released_and_centred()
    {
        var state = PadState.Neutral;

        Assert.Equal(0, state.Buttons);
        Assert.Equal(0, state.LeftTrigger);
        Assert.Equal(0, state.RightTrigger);
        Assert.Equal(0, state.ThumbLX);
        Assert.Equal(0, state.ThumbLY);
        Assert.Equal(0, state.ThumbRX);
        Assert.Equal(0, state.ThumbRY);
    }

    [Fact]
    public void IsPressed_reads_individual_bits_out_of_the_mask()
    {
        var state = PadState.Neutral;
        state.Buttons = (ushort)(GamepadButtons.A | GamepadButtons.DPadLeft);

        Assert.True(state.IsPressed(GamepadButtons.A));
        Assert.True(state.IsPressed(GamepadButtons.DPadLeft));

        Assert.False(state.IsPressed(GamepadButtons.B));
        Assert.False(state.IsPressed(GamepadButtons.Y));
        Assert.False(state.IsPressed(GamepadButtons.Start));
    }

    /// <summary>PadState is a struct, so assignment must copy rather than alias.</summary>
    [Fact]
    public void Assignment_copies_rather_than_shares()
    {
        var original = PadState.Neutral;
        original.ThumbLX = 1234;

        var copy = original;
        copy.ThumbLX = -5000;

        Assert.Equal(1234, original.ThumbLX);
        Assert.Equal(-5000, copy.ThumbLX);
    }
}
