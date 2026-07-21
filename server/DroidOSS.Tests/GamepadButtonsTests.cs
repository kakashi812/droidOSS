using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class GamepadButtonsTests
{
    /// <summary>
    /// These sixteen constants are fixed by XInput and are hand-transcribed into
    /// three languages (C#, Kotlin, Python). A single wrong digit produces a pad
    /// where one button does something else entirely, with no error to follow.
    /// The canonical table is in docs/PROTOCOL.md — this test is the C# copy's guard.
    /// </summary>
    [Theory]
    [InlineData(GamepadButtons.DPadUp, 0x0001)]
    [InlineData(GamepadButtons.DPadDown, 0x0002)]
    [InlineData(GamepadButtons.DPadLeft, 0x0004)]
    [InlineData(GamepadButtons.DPadRight, 0x0008)]
    [InlineData(GamepadButtons.Start, 0x0010)]
    [InlineData(GamepadButtons.Back, 0x0020)]
    [InlineData(GamepadButtons.LeftThumb, 0x0040)]
    [InlineData(GamepadButtons.RightThumb, 0x0080)]
    [InlineData(GamepadButtons.LeftShoulder, 0x0100)]
    [InlineData(GamepadButtons.RightShoulder, 0x0200)]
    [InlineData(GamepadButtons.Guide, 0x0400)]
    [InlineData(GamepadButtons.A, 0x1000)]
    [InlineData(GamepadButtons.B, 0x2000)]
    [InlineData(GamepadButtons.X, 0x4000)]
    [InlineData(GamepadButtons.Y, 0x8000)]
    public void Button_matches_its_XInput_value(GamepadButtons button, int expected)
    {
        Assert.Equal(expected, (ushort)button);
    }

    /// <summary>Every button must own a distinct single bit, or they overlap in the mask.</summary>
    [Fact]
    public void Every_button_is_a_distinct_single_bit()
    {
        var buttons = Enum.GetValues<GamepadButtons>()
            .Where(b => b != GamepadButtons.None)
            .ToArray();

        foreach (var button in buttons)
        {
            var value = (ushort)button;
            Assert.True((value & (value - 1)) == 0, $"{button} (0x{value:X4}) is not a single bit");
        }

        Assert.Equal(buttons.Length, buttons.Distinct().Count());
    }

    /// <summary>
    /// Bit 11 (0x0800) is unused by XInput. Claiming it would look harmless and
    /// then be ignored by the driver.
    /// </summary>
    [Fact]
    public void Reserved_bit_is_left_alone()
    {
        var claimed = Enum.GetValues<GamepadButtons>()
            .Aggregate(0, (mask, b) => mask | (ushort)b);

        Assert.Equal(0, claimed & 0x0800);
    }
}
