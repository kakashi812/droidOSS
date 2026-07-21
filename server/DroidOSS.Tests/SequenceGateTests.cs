using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class SequenceGateTests
{
    [Fact]
    public void Accepts_the_very_first_packet_whatever_its_value()
    {
        var gate = new SequenceGate();

        // A phone that reconnects may resume counting from anywhere, so there is
        // no such thing as an implausible first sequence.
        Assert.True(gate.Accept(999_999));
        Assert.Equal(999_999u, gate.Last);
    }

    [Fact]
    public void Accepts_a_strictly_increasing_run()
    {
        var gate = new SequenceGate();

        for (uint seq = 1; seq <= 100; seq++)
            Assert.True(gate.Accept(seq), $"sequence {seq} should have been accepted");
    }

    [Fact]
    public void Rejects_a_duplicate()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(10));
        Assert.False(gate.Accept(10));
    }

    /// <summary>
    /// The out-of-order case this whole class exists for: 57, 59, then a late
    /// 58. Applying 58 would jerk the stick back to a stale position.
    /// </summary>
    [Fact]
    public void Rejects_a_packet_that_arrives_late()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(57));
        Assert.True(gate.Accept(59));
        Assert.False(gate.Accept(58));

        // ...and the late arrival must not have moved the baseline.
        Assert.Equal(59u, gate.Last);
        Assert.True(gate.Accept(60));
    }

    /// <summary>
    /// The test that justifies the subtract-and-cast form.
    /// </summary>
    /// <remarks>
    /// At 125 packets a second the u32 counter wraps after about 1.1 years of
    /// continuous play. With the naive <c>sequence &lt;= _last</c> comparison,
    /// every packet after the wrap is rejected and the controller freezes
    /// permanently. Nobody would ever reach it by accident, and it would be
    /// almost impossible to diagnose if they did.
    /// </remarks>
    [Fact]
    public void Survives_the_counter_wrapping_past_its_maximum()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(uint.MaxValue - 2));   // 0xFFFFFFFD
        Assert.True(gate.Accept(uint.MaxValue - 1));   // 0xFFFFFFFE
        Assert.True(gate.Accept(uint.MaxValue));       // 0xFFFFFFFF

        // The wrap itself: 0 is one newer than 0xFFFFFFFF, not four billion older.
        Assert.True(gate.Accept(0));
        Assert.True(gate.Accept(1));
        Assert.True(gate.Accept(2));
    }

    /// <summary>Ordering must still be enforced on the far side of a wrap.</summary>
    [Fact]
    public void Still_rejects_stale_packets_across_a_wrap()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(uint.MaxValue));
        Assert.True(gate.Accept(1));

        // 0 was sent before 1, so it is stale even though it is numerically smaller.
        Assert.False(gate.Accept(0));

        // And the pre-wrap value is older still.
        Assert.False(gate.Accept(uint.MaxValue));
    }

    /// <summary>
    /// A jump far ahead is accepted — that is packet loss, not corruption, and
    /// the newest state is always the one worth having.
    /// </summary>
    [Fact]
    public void Accepts_a_large_forward_jump_after_packet_loss()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(1));
        Assert.True(gate.Accept(5_000));
    }

    [Fact]
    public void Reset_makes_the_next_packet_a_first_packet_again()
    {
        var gate = new SequenceGate();

        Assert.True(gate.Accept(500));
        Assert.False(gate.Accept(100));   // stale

        gate.Reset();

        // A new phone took the slot and counts from its own baseline.
        Assert.True(gate.Accept(100));
        Assert.Equal(100u, gate.Last);
    }
}
