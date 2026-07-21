using DroidOSS.Core;

namespace DroidOSS.Tests;

/// <summary>
/// An <see cref="IPadBackend"/> that records what it was told to do.
/// </summary>
/// <remarks>
/// This is why <c>DroidOSS.Core</c> holds no reference to any driver library:
/// the whole server can be exercised on a machine with no ViGEmBus installed,
/// and a test can assert on exactly which states reached the pad.
/// </remarks>
public sealed class FakePadBackend : IPadBackend
{
    public List<int> Connected { get; } = [];
    public List<int> Disconnected { get; } = [];
    public List<(int Slot, PadState State)> Submissions { get; } = [];
    public bool Disposed { get; private set; }

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    /// <summary>The most recent state handed to the driver.</summary>
    public PadState LastSubmitted =>
        Submissions.Count == 0 ? PadState.Neutral : Submissions[^1].State;

    public void Connect(int slot) => Connected.Add(slot);

    public void Submit(int slot, in PadState state) => Submissions.Add((slot, state));

    public void Disconnect(int slot) => Disconnected.Add(slot);

    public void Dispose() => Disposed = true;

    /// <summary>Lets a test pretend a game asked for vibration.</summary>
    public void RaiseRumble(int slot, byte large, byte small) =>
        RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, large, small));
}
