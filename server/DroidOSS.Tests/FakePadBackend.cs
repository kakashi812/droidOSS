using DroidOSS.Core;

namespace DroidOSS.Tests;

/// <summary>Which method was called on the backend.</summary>
public enum PadCall
{
    Connect,
    Submit,
    Disconnect,
}

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

    /// <summary>
    /// Every call in the order it happened.
    /// </summary>
    /// <remarks>
    /// The lists above answer "what reached the pad"; this answers "in what
    /// order", which they cannot. It exists for one assertion in particular:
    /// that a pad is zeroed <em>before</em> it is unplugged. Games that latch
    /// the last state they saw make the reverse order indistinguishable from no
    /// cleanup at all.
    /// </remarks>
    public List<(PadCall Call, int Slot, PadState State)> Log { get; } = [];

    public event EventHandler<RumbleEventArgs>? RumbleReceived;

    /// <summary>The most recent state handed to the driver.</summary>
    public PadState LastSubmitted =>
        Submissions.Count == 0 ? PadState.Neutral : Submissions[^1].State;

    public void Connect(int slot)
    {
        Connected.Add(slot);
        Log.Add((PadCall.Connect, slot, PadState.Neutral));
    }

    public void Submit(int slot, in PadState state)
    {
        Submissions.Add((slot, state));
        Log.Add((PadCall.Submit, slot, state));
    }

    public void Disconnect(int slot)
    {
        Disconnected.Add(slot);
        Log.Add((PadCall.Disconnect, slot, PadState.Neutral));
    }

    public void Dispose() => Disposed = true;

    /// <summary>Lets a test pretend a game asked for vibration.</summary>
    public void RaiseRumble(int slot, byte large, byte small) =>
        RumbleReceived?.Invoke(this, new RumbleEventArgs(slot, large, small));

    /// <summary>Every state submitted to one slot, in order.</summary>
    public List<PadState> SubmissionsTo(int slot) =>
        [.. Submissions.Where(s => s.Slot == slot).Select(s => s.State)];
}
