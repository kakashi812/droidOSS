using DroidOSS.Core;

namespace DroidOSS.IntegrationTests;

/// <summary>
/// An <see cref="IPadBackend"/> that records what reached each pad.
/// </summary>
/// <remarks>
/// Unlike the unit tests' fake, this one is called from the server's socket and
/// sweep threads while the test thread reads it, so every access takes a lock and
/// readers get copies.
/// </remarks>
public sealed class RecordingPadBackend : IPadBackend
{
    private readonly Lock _lock = new();
    private readonly List<(string Call, int Slot, PadState State)> _log = [];

    /// <summary>No game runs here, so nothing ever asks for vibration.</summary>
    public event EventHandler<RumbleEventArgs>? RumbleReceived { add { } remove { } }

    public void Connect(int slot)
    {
        lock (_lock) _log.Add(("connect", slot, PadState.Neutral));
    }

    public void Submit(int slot, in PadState state)
    {
        lock (_lock) _log.Add(("submit", slot, state));
    }

    public void Disconnect(int slot)
    {
        lock (_lock) _log.Add(("disconnect", slot, PadState.Neutral));
    }

    public void Dispose() { }

    /// <summary>Every call so far, in order.</summary>
    public List<(string Call, int Slot, PadState State)> Log()
    {
        lock (_lock) return [.. _log];
    }

    public int Count(string call, int slot) => Log().Count(e => e.Call == call && e.Slot == slot);

    public List<PadState> SubmissionsTo(int slot) =>
        [.. Log().Where(e => e.Call == "submit" && e.Slot == slot).Select(e => e.State)];
}
