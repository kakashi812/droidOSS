using DroidOSS.Core;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace DroidOSS.ViGEm;

/// <summary>
/// An <see cref="IPadBackend"/> built on the ViGEmBus virtual bus driver.
/// </summary>
/// <remarks>
/// This is the only class in the project that knows ViGEm exists. If the driver
/// is ever replaced, this file is what gets rewritten — everything else talks to
/// <see cref="IPadBackend"/> and is unaffected.
/// </remarks>
public sealed class ViGEmPadBackend : IPadBackend
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller?[] _pads = new IXbox360Controller?[IPadBackend.MaxPads];
    private bool _disposed;

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    /// <summary>Opens a connection to the driver.</summary>
    /// <exception cref="PadDriverUnavailableException">The driver is missing or unreachable.</exception>
    public ViGEmPadBackend()
    {
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

    public void Connect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_pads[slot] is not null) return;   // already plugged in

        var pad = _client.CreateXbox360Controller();

        // We decide when a report goes out, so that one Submit call is one update
        // rather than one per field touched.
        pad.AutoSubmitReport = false;

        // Rumble arrives on a driver thread. Capture the slot so subscribers know
        // which phone to forward it to.
        pad.FeedbackReceived += (_, e) =>
            RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, e.LargeMotor, e.SmallMotor));

        pad.Connect();
        _pads[slot] = pad;
    }

    public void Submit(int slot, in PadState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_pads[slot] is not { } pad) return;   // nothing plugged in — silently ignore

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

    public void Disconnect(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slot);

        if (_pads[slot] is not { } pad) return;

        pad.Disconnect();
        _pads[slot] = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (var slot = 0; slot < _pads.Length; slot++)
        {
            if (_pads[slot] is not { } pad) continue;

            // Best effort — we are shutting down and a failure here helps nobody.
            try { pad.Disconnect(); } catch { /* ignored */ }
            _pads[slot] = null;
        }

        _client.Dispose();
    }

    private static void ValidateSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, IPadBackend.MaxPads);
    }
}
