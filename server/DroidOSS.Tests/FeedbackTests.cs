using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class FeedbackMessageTests
{
    // Golden vectors, shared with tools/fake_phone.py and the Android tests.
    // Rumble: pad 0, large 200, small 40. Light: pad 1, player 2, #E53B3B.
    private const string GoldenRumble = "DA0105 00 C8 28";
    private const string GoldenLight = "DA0107 01 02 E53B3B";

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    [Fact]
    public void Rumble_matches_the_golden_vector()
    {
        var buffer = new byte[FeedbackMessage.RumbleSize];
        Assert.Equal(6, FeedbackMessage.WriteRumble(buffer, 0, 200, 40));
        Assert.Equal(Hex(GoldenRumble), buffer);
    }

    [Fact]
    public void Light_matches_the_golden_vector()
    {
        var buffer = new byte[FeedbackMessage.LightSize];
        Assert.Equal(8, FeedbackMessage.WriteLight(buffer, 1, 2, new Rgb(0xE5, 0x3B, 0x3B)));
        Assert.Equal(Hex(GoldenLight), buffer);
    }

    [Fact]
    public void Both_round_trip()
    {
        Assert.True(FeedbackMessage.TryReadRumble(Hex(GoldenRumble), out var pad, out var large, out var small));
        Assert.Equal((0, 200, 40), (pad, large, small));

        Assert.True(FeedbackMessage.TryReadLight(Hex(GoldenLight), out pad, out var player, out var colour));
        Assert.Equal((1, 2), (pad, player));
        Assert.Equal(new Rgb(0xE5, 0x3B, 0x3B), colour);
    }

    [Fact]
    public void Neither_is_confused_with_the_other_or_with_a_welcome()
    {
        Assert.False(FeedbackMessage.TryReadRumble(Hex(GoldenLight), out _, out _, out _));
        Assert.False(FeedbackMessage.TryReadLight(Hex(GoldenRumble), out _, out _, out _));
        Assert.False(FeedbackMessage.TryReadRumble(Hex("DA010300"), out _, out _, out _));
        Assert.False(FeedbackMessage.TryReadRumble(Hex("DA0205 00 C8 28"), out _, out _, out _));   // wrong version
    }
}

public class FeedbackSchedulerTests
{
    private readonly ManualClock _clock = new();
    private readonly FeedbackScheduler _s;

    public FeedbackSchedulerTests() => _s = new FeedbackScheduler(_clock);

    private List<string> Collect() =>
        _s.Collect().Select(o => $"{o.Slot}:{(MessageType)o.Message[2]}:{Convert.ToHexString(o.Message[4..])}").ToList();

    [Fact]
    public void Nothing_is_sent_to_a_slot_with_no_phone()
    {
        _s.SetRumble(0, 100, 0);
        _s.SetLight(0, 1, PlayerColors.Player1);
        Assert.Empty(Collect());
    }

    [Fact]
    public void A_light_known_before_the_phone_connects_is_sent_as_it_connects()
    {
        _s.SetLight(2, 3, PlayerColors.Player3);
        _s.SessionOpened(2);
        Assert.Equal(["2:Light:033DDC6A"], Collect());
    }

    [Fact]
    public void Rumble_is_sent_at_once_then_repeated_while_it_runs()
    {
        _s.SessionOpened(0);
        _s.SetRumble(0, 200, 40);
        Assert.Equal(["0:Rumble:C828"], Collect());

        _clock.Advance(0.1);
        Assert.Empty(Collect());

        _clock.Advance(0.15);
        Assert.Equal(["0:Rumble:C828"], Collect());
    }

    [Fact]
    public void A_stop_is_sent_three_times_then_nothing()
    {
        _s.SessionOpened(0);
        _s.SetRumble(0, 200, 40);
        Collect();

        _s.SetRumble(0, 0, 0);
        var sent = 0;
        for (var i = 0; i < 10; i++)
        {
            sent += Collect().Count(m => m == "0:Rumble:0000");
            _clock.Advance(0.25);
        }
        Assert.Equal(FeedbackScheduler.StopRepeats, sent);
    }

    [Fact]
    public void An_unchanged_rumble_is_not_sent_early()
    {
        _s.SessionOpened(0);
        _s.SetRumble(0, 50, 50);
        Collect();
        _s.SetRumble(0, 50, 50);
        Assert.Empty(Collect());
    }

    [Fact]
    public void Light_changes_go_at_once_and_repeat_every_second()
    {
        _s.SessionOpened(1);
        _s.SetLight(1, 2, PlayerColors.Player2);
        Assert.Single(Collect());

        _s.SetLight(1, 2, new Rgb(1, 2, 3));
        Assert.Equal(["1:Light:02010203"], Collect());

        _clock.Advance(0.5);
        Assert.Empty(Collect());
        _clock.Advance(0.5);
        Assert.Equal(["1:Light:02010203"], Collect());
    }

    [Fact]
    public void Closing_a_session_stops_everything_for_that_slot_but_remembers_the_light()
    {
        _s.SessionOpened(0);
        _s.SetLight(0, 1, PlayerColors.Player1);
        _s.SetRumble(0, 9, 9);
        Collect();

        _s.SessionClosed(0);
        _clock.Advance(5);
        Assert.Empty(Collect());
        Assert.Equal((byte)1, _s.LightOf(0)!.Value.Player);
    }

    [Fact]
    public void A_new_session_starts_with_the_motors_off()
    {
        _s.SessionOpened(0);
        _s.SetRumble(0, 9, 9);
        _s.SessionClosed(0);

        _s.SessionOpened(0);
        _clock.Advance(1);
        Assert.DoesNotContain(Collect(), m => m.Contains("Rumble"));
    }
}

public class DualShock4MappingTests
{
    [Theory]
    [InlineData(0, 128)]
    [InlineData(-32768, 0)]
    [InlineData(32767, 255)]
    public void Stick_X_is_unsigned_with_centre_128(short x, byte expected) =>
        Assert.Equal(expected, DualShock4Mapping.StickX(x));

    [Theory]
    [InlineData(0, 128)]
    [InlineData(32767, 0)]       // full up is 0 on a DualShock
    [InlineData(-32768, 255)]    // full down is 255
    public void Stick_Y_is_flipped(short y, byte expected) =>
        Assert.Equal(expected, DualShock4Mapping.StickY(y));

    [Theory]
    [InlineData(GamepadButtons.None, DPadDirection.None)]
    [InlineData(GamepadButtons.DPadUp, DPadDirection.North)]
    [InlineData(GamepadButtons.DPadUp | GamepadButtons.DPadRight, DPadDirection.NorthEast)]
    [InlineData(GamepadButtons.DPadDown | GamepadButtons.DPadLeft, DPadDirection.SouthWest)]
    [InlineData(GamepadButtons.DPadLeft, DPadDirection.West)]
    [InlineData(GamepadButtons.DPadUp | GamepadButtons.DPadDown, DPadDirection.None)]
    [InlineData(GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadRight, DPadDirection.East)]
    public void DPad_bits_become_one_direction(GamepadButtons buttons, DPadDirection expected) =>
        Assert.Equal(expected, DualShock4Mapping.DPad((ushort)buttons));

    [Fact]
    public void A_light_trigger_touch_is_not_a_press()
    {
        Assert.False(DualShock4Mapping.TriggerPressed(10));
        Assert.True(DualShock4Mapping.TriggerPressed(255));
    }

    [Fact]
    public void An_output_report_with_both_flags_gives_rumble_and_lightbar()
    {
        byte[] report = [0x05, 0x07, 0x04, 0x00, 40, 200, 0xE5, 0x3B, 0x3B, 0, 0];
        Assert.True(DualShock4OutputReport.TryParse(report, out var rumble, out var lightbar));
        Assert.Equal(((byte)200, (byte)40), rumble);
        Assert.Equal(new Rgb(0xE5, 0x3B, 0x3B), lightbar);
    }

    [Fact]
    public void A_lightbar_only_report_is_not_read_as_stop_vibrating()
    {
        byte[] report = [0x05, 0x02, 0x04, 0x00, 0, 0, 0x10, 0x20, 0x30];
        Assert.True(DualShock4OutputReport.TryParse(report, out var rumble, out var lightbar));
        Assert.Null(rumble);
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), lightbar);
    }

    [Fact]
    public void Other_reports_are_ignored()
    {
        Assert.False(DualShock4OutputReport.TryParse([0x11, 0xFF, 0, 0, 0, 0, 0, 0, 0], out _, out _));
        Assert.False(DualShock4OutputReport.TryParse([0x05, 0x07], out _, out _));
    }
}
