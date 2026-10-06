using DroidOSS.Core;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DroidOSS.ViGEm;

/// <summary>
/// Virtual pads through ViGEmBus — Xbox 360 by default, or DualShock 4.
/// </summary>
/// <remarks>
/// The kind is one setting for all four pads. Changing it while phones are
/// connected unplugs their pads and plugs in the other kind in the same slots,
/// zeroed first; the phones stay connected and the next input packet carries
/// on as if nothing happened.
///
/// What comes back from the driver differs by kind. An Xbox 360 pad reports its
/// rumble and the player number Windows gave it; a DualShock 4 reports its
/// rumble and the lightbar colour the game or Steam chose. Both are passed on
/// as <see cref="RumbleReceived"/> and <see cref="LightChanged"/>.
/// </remarks>
public sealed class ViGEmPadBackend : IPadBackend
{
    private readonly ViGEmClient _client;
    private readonly IVirtualGamepad?[] _pads = new IVirtualGamepad?[IPadBackend.MaxPads];

    // One reader per plugged DualShock 4, waiting on its output reports;
    // cancelled when that pad is unplugged.
    private readonly CancellationTokenSource?[] _readers = new CancellationTokenSource?[IPadBackend.MaxPads];

    // Submit runs on the socket thread, Connect/Disconnect under the session
    // lock, and a kind change on the UI thread. Uncontended, this costs tens of
    // nanoseconds — nothing against an 8 ms packet interval.
    private readonly Lock _lock = new();

    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;
    public event EventHandler<LightEventArgs>? LightChanged;

    /// <summary>
    /// Every raw output report a game or Steam sends a DualShock 4, before it is
    /// interpreted — for the developer log, to show exactly what arrived.
    /// Raised on the pad's reader thread.
    /// </summary>
    public event Action<int, byte[]>? OutputReportReceived;

    public ViGEmPadBackend(PadKind kind = PadKind.Xbox360)
    {
        Kind = kind;
        try
        {
            _client = new ViGEmClient();
        }
        catch (Exception ex)
        {
            // The client constructor is where a missing driver first shows up.
            throw new PadDriverUnavailableException(
                "Could not connect to the ViGEmBus driver. It is probably not installed.", ex);
        }
    }

    /// <summary>The kind of pad plugged in for each phone.</summary>
    public PadKind Kind { get; private set; }

    /// <summary>
    /// Switch every pad to <paramref name="kind"/>, re-plugging any already
    /// connected. Games see the old controllers unplugged and new ones appear.
    /// </summary>
    public void SetKind(PadKind kind)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (kind == Kind) return;
            Kind = kind;

            for (var slot = 0; slot < _pads.Length; slot++)
            {
                if (_pads[slot] is null) continue;
                Unplug(slot);
                Plug(slot);
            }
        }
    }

    public void Connect(int slot)
    {
        ValidateSlot(slot);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pads[slot] is not null) return;   // already plugged in
            Plug(slot);
        }
    }

    public void Submit(int slot, in PadState state)
    {
        ValidateSlot(slot);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            switch (_pads[slot])
            {
                case IXbox360Controller x:
                    SubmitXbox(x, in state);
                    break;
                case IDualShock4Controller ds4:
                    SubmitDualShock4(ds4, in state);
                    break;
                // Nothing plugged in — silently ignore.
            }
        }
    }

    public void Disconnect(int slot)
    {
        ValidateSlot(slot);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pads[slot] is null) return;
            Unplug(slot);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            for (var slot = 0; slot < _pads.Length; slot++)
            {
                _readers[slot]?.Cancel();
                if (_pads[slot] is not { } pad) continue;

                // Best effort — we are shutting down and a failure here helps nobody.
                try { pad.Disconnect(); } catch { /* ignored */ }
                _pads[slot] = null;
            }

            _client.Dispose();
        }
    }

    // ── plugging ─────────────────────────────────────────────────────────

    private void Plug(int slot)
    {
        if (Kind == PadKind.DualShock4)
        {
            var pad = _client.CreateDualShock4Controller();
            pad.AutoSubmitReport = false;
            pad.Connect();
            _pads[slot] = pad;
            StartOutputReader(slot, pad);

            // Until a game or Steam sets the lightbar, show the player colour a
            // real DualShock 4 would light up in.
            RaiseLight(slot, (byte)(slot + 1), PlayerColors.For(slot + 1));
        }
        else
        {
            var pad = _client.CreateXbox360Controller();

            // We decide when a report goes out, so that one Submit call is one
            // update rather than one per field touched.
            pad.AutoSubmitReport = false;
            pad.FeedbackReceived += (_, e) => OnXboxFeedback(slot, e);
            pad.Connect();
            _pads[slot] = pad;

            var player = PlayerNumber(pad, slot);
            RaiseLight(slot, player, PlayerColors.For(player));
        }
    }

    /// <summary>Zero, then unplug — the order every path out of a pad must keep.</summary>
    private void Unplug(int slot)
    {
        if (_pads[slot] is not { } pad) return;

        _readers[slot]?.Cancel();
        _readers[slot] = null;

        var neutral = PadState.Neutral;
        switch (pad)
        {
            case IXbox360Controller x: SubmitXbox(x, in neutral); break;
            case IDualShock4Controller ds4: SubmitDualShock4(ds4, in neutral); break;
        }

        pad.Disconnect();
        _pads[slot] = null;
    }

    // ── reports ──────────────────────────────────────────────────────────

    private static void SubmitXbox(IXbox360Controller pad, in PadState state)
    {
        // These are ref-returning properties, so each assignment writes straight
        // into the report buffer. No allocation, no per-button calls.
        pad.SetButtonsFull(state.Buttons);
        pad.LeftTrigger = state.LeftTrigger;
        pad.RightTrigger = state.RightTrigger;
        pad.LeftThumbX = state.ThumbLX;
        pad.LeftThumbY = state.ThumbLY;
        pad.RightThumbX = state.ThumbRX;
        pad.RightThumbY = state.ThumbRY;
        pad.SubmitReport();
    }

    private static void SubmitDualShock4(IDualShock4Controller pad, in PadState state)
    {
        var buttons = state.Buttons;
        bool Down(GamepadButtons b) => (buttons & (ushort)b) != 0;

        pad.SetButtonState(DualShock4Button.Cross, Down(GamepadButtons.A));
        pad.SetButtonState(DualShock4Button.Circle, Down(GamepadButtons.B));
        pad.SetButtonState(DualShock4Button.Square, Down(GamepadButtons.X));
        pad.SetButtonState(DualShock4Button.Triangle, Down(GamepadButtons.Y));
        pad.SetButtonState(DualShock4Button.ShoulderLeft, Down(GamepadButtons.LeftShoulder));
        pad.SetButtonState(DualShock4Button.ShoulderRight, Down(GamepadButtons.RightShoulder));
        pad.SetButtonState(DualShock4Button.Share, Down(GamepadButtons.Back));
        pad.SetButtonState(DualShock4Button.Options, Down(GamepadButtons.Start));
        pad.SetButtonState(DualShock4Button.ThumbLeft, Down(GamepadButtons.LeftThumb));
        pad.SetButtonState(DualShock4Button.ThumbRight, Down(GamepadButtons.RightThumb));
        pad.SetButtonState(DualShock4Button.TriggerLeft, DualShock4Mapping.TriggerPressed(state.LeftTrigger));
        pad.SetButtonState(DualShock4Button.TriggerRight, DualShock4Mapping.TriggerPressed(state.RightTrigger));
        pad.SetButtonState(DualShock4SpecialButton.Ps, Down(GamepadButtons.Guide));

        pad.SetDPadDirection(DualShock4Mapping.DPad(state.Buttons) switch
        {
            DPadDirection.North => DualShock4DPadDirection.North,
            DPadDirection.NorthEast => DualShock4DPadDirection.Northeast,
            DPadDirection.East => DualShock4DPadDirection.East,
            DPadDirection.SouthEast => DualShock4DPadDirection.Southeast,
            DPadDirection.South => DualShock4DPadDirection.South,
            DPadDirection.SouthWest => DualShock4DPadDirection.Southwest,
            DPadDirection.West => DualShock4DPadDirection.West,
            DPadDirection.NorthWest => DualShock4DPadDirection.Northwest,
            _ => DualShock4DPadDirection.None,
        });

        pad.SetSliderValue(DualShock4Slider.LeftTrigger, state.LeftTrigger);
        pad.SetSliderValue(DualShock4Slider.RightTrigger, state.RightTrigger);
        pad.SetAxisValue(DualShock4Axis.LeftThumbX, DualShock4Mapping.StickX(state.ThumbLX));
        pad.SetAxisValue(DualShock4Axis.LeftThumbY, DualShock4Mapping.StickY(state.ThumbLY));
        pad.SetAxisValue(DualShock4Axis.RightThumbX, DualShock4Mapping.StickX(state.ThumbRX));
        pad.SetAxisValue(DualShock4Axis.RightThumbY, DualShock4Mapping.StickY(state.ThumbRY));
        pad.SubmitReport();
    }

    // ── what comes back ──────────────────────────────────────────────────

    private void OnXboxFeedback(int slot, Xbox360FeedbackReceivedEventArgs e)
    {
        RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, e.LargeMotor, e.SmallMotor));

        // The ring of light: 0–3 for players 1–4. Anything else is a pattern
        // (blinking, all on), not a player.
        if (e.LedNumber <= 3)
        {
            var player = (byte)(e.LedNumber + 1);
            RaiseLight(slot, player, PlayerColors.For(player));
        }
    }

    /// <summary>
    /// Waits on one DualShock 4's output reports — rumble and lightbar — until
    /// it is unplugged.
    /// </summary>
    /// <remarks>
    /// The raw report rather than the library's FeedbackReceived event, which
    /// ViGEm itself marks unreliable for the DualShock 4. A dedicated thread,
    /// because the wait blocks; it wakes every 250 ms to notice being cancelled.
    /// </remarks>
    private void StartOutputReader(int slot, IDualShock4Controller pad)
    {
        var cts = new CancellationTokenSource();
        _readers[slot] = cts;

        var thread = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                byte[] report;
                try
                {
                    report = pad.AwaitRawOutputReport(250, out var timedOut).ToArray();
                    if (timedOut) continue;
                }
                catch (Exception)
                {
                    // Unplugged under us, or the driver went away. Either way, done.
                    return;
                }

                if (cts.IsCancellationRequested) return;
                OutputReportReceived?.Invoke(slot, report);
                if (!DualShock4OutputReport.TryParse(report, out var rumble, out var lightbar)) continue;

                if (rumble is { } r)
                    RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, r.Large, r.Small));

                // An unlit lightbar says nothing useful to show; keep the player colour.
                if (lightbar is { } colour)
                    RaiseLight(slot, (byte)(slot + 1), colour.IsBlack ? PlayerColors.For(slot + 1) : colour);
            }
        })
        {
            IsBackground = true,
            Name = $"droidoss-ds4-{slot}",
        };
        thread.Start();
    }

    private void RaiseLight(int slot, byte player, Rgb colour) =>
        LightChanged?.Invoke(this, new LightEventArgs(slot, player, colour));

    /// <summary>
    /// The player number games will show for this pad. Not always the slot:
    /// a real controller already plugged in takes player 1.
    /// </summary>
    private static byte PlayerNumber(IXbox360Controller pad, int slot)
    {
        try
        {
            var index = pad.UserIndex;
            if (index is >= 0 and <= 3) return (byte)(index + 1);
        }
        catch (Exception)
        {
            // Not assigned yet; the LED report that follows will correct it.
        }
        return (byte)(slot + 1);
    }

    private static void ValidateSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, IPadBackend.MaxPads);
    }
}
