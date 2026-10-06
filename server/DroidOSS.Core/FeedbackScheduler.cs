namespace DroidOSS.Core;

/// <summary>A RUMBLE or LIGHT ready to go to the phone in <see cref="Slot"/>.</summary>
public readonly record struct OutgoingFeedback(int Slot, byte[] Message);

/// <summary>
/// Decides when each phone is sent its RUMBLE and LIGHT.
/// </summary>
/// <remarks>
/// UDP loses packets, and a lost "stop vibrating" would leave a phone buzzing
/// in someone's hands until the game happened to change its mind. So both
/// messages are state, sent whenever it changes and then repeated:
///
/// <list type="bullet">
/// <item>While the motors run, RUMBLE is repeated every <see cref="RumbleRepeat"/>.
/// The phone vibrates a little longer than that for each one, so it stays on
/// while they keep coming and stops by itself if they don't — a server that
/// crashes cannot leave a phone vibrating.</item>
/// <item>A stop is sent <see cref="StopRepeats"/> times, then nothing.</item>
/// <item>LIGHT is repeated every <see cref="LightRepeat"/>, and sent at once when
/// a phone connects, so a phone always learns its colour within a second.</item>
/// </list>
///
/// Knows nothing about sockets or drivers: told what the game wants and what
/// time it is, it says what to send. Thread-safe — the driver reports from its
/// own threads while a timer collects.
/// </remarks>
public sealed class FeedbackScheduler(TimeProvider? clock = null)
{
    public static readonly TimeSpan RumbleRepeat = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan LightRepeat = TimeSpan.FromSeconds(1);
    public const int StopRepeats = 3;

    private sealed class SlotState
    {
        public bool Connected;

        public byte Large, Small;
        public bool RumbleDue;
        public int StopsLeft;
        public DateTimeOffset RumbleSent;

        public bool HasLight;
        public byte Player;
        public Rgb Colour;
        public bool LightDue;
        public DateTimeOffset LightSent;
    }

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SlotState[] _slots =
        Enumerable.Range(0, IPadBackend.MaxPads).Select(_ => new SlotState()).ToArray();
    private readonly Lock _lock = new();

    /// <summary>A phone took this slot: start talking to it, light first.</summary>
    public void SessionOpened(int slot)
    {
        lock (_lock)
        {
            var s = _slots[slot];
            s.Connected = true;
            s.Large = s.Small = 0;
            s.RumbleDue = false;
            s.StopsLeft = 0;
            // Keep what the driver has already said about the light — it may
            // well have spoken before the session existed — but send it now.
            s.LightDue = s.HasLight;
        }
    }

    /// <summary>The phone left: nobody to send to.</summary>
    public void SessionClosed(int slot)
    {
        lock (_lock)
        {
            var s = _slots[slot];
            s.Connected = false;
            s.RumbleDue = false;
            s.StopsLeft = 0;
            s.LightDue = false;
        }
    }

    /// <summary>What the game wants the motors doing now.</summary>
    public void SetRumble(int slot, byte largeMotor, byte smallMotor)
    {
        lock (_lock)
        {
            var s = _slots[slot];
            if (largeMotor == s.Large && smallMotor == s.Small) return;

            s.Large = largeMotor;
            s.Small = smallMotor;
            s.RumbleDue = true;
            s.StopsLeft = largeMotor == 0 && smallMotor == 0 ? StopRepeats : 0;
        }
    }

    /// <summary>Which player this pad is, and the colour to show.</summary>
    public void SetLight(int slot, byte player, Rgb colour)
    {
        lock (_lock)
        {
            var s = _slots[slot];
            if (s.HasLight && s.Player == player && s.Colour == colour) return;

            s.HasLight = true;
            s.Player = player;
            s.Colour = colour;
            s.LightDue = true;
        }
    }

    /// <summary>The light last set for a slot, for display.</summary>
    public (byte Player, Rgb Colour)? LightOf(int slot)
    {
        lock (_lock)
        {
            var s = _slots[slot];
            return s.HasLight ? (s.Player, s.Colour) : null;
        }
    }

    /// <summary>Everything due to be sent now. Call often — every 50 ms is plenty.</summary>
    public List<OutgoingFeedback> Collect()
    {
        var now = _clock.GetUtcNow();
        var due = new List<OutgoingFeedback>();

        lock (_lock)
        {
            for (var slot = 0; slot < _slots.Length; slot++)
            {
                var s = _slots[slot];
                if (!s.Connected) continue;

                if (RumbleIsDue(s, now))
                {
                    var message = new byte[FeedbackMessage.RumbleSize];
                    FeedbackMessage.WriteRumble(message, (byte)slot, s.Large, s.Small);
                    due.Add(new OutgoingFeedback(slot, message));

                    s.RumbleDue = false;
                    s.RumbleSent = now;
                    if (s.Large == 0 && s.Small == 0 && s.StopsLeft > 0) s.StopsLeft--;
                }

                if (s.HasLight && (s.LightDue || now - s.LightSent >= LightRepeat))
                {
                    var message = new byte[FeedbackMessage.LightSize];
                    FeedbackMessage.WriteLight(message, (byte)slot, s.Player, s.Colour);
                    due.Add(new OutgoingFeedback(slot, message));

                    s.LightDue = false;
                    s.LightSent = now;
                }
            }
        }

        return due;
    }

    private static bool RumbleIsDue(SlotState s, DateTimeOffset now)
    {
        if (s.RumbleDue) return true;
        if (now - s.RumbleSent < RumbleRepeat) return false;

        var running = s.Large != 0 || s.Small != 0;
        return running || s.StopsLeft > 0;
    }
}
