namespace DroidOSS.Core;

/// <summary>
/// Somewhere to send gamepad state that makes a controller appear to Windows.
/// </summary>
/// <remarks>
/// This interface is the seam between our logic and the virtual-pad driver, and
/// it exists for two reasons:
///
/// <list type="bullet">
/// <item>ViGEmBus was retired in November 2023. It works, but it should never be
/// welded into the core of this project.</item>
/// <item>The same seam is what a Linux <c>uinput</c> backend would implement.</item>
/// </list>
///
/// Nothing behind this interface knows about UDP, phones, or packets, and
/// nothing in front of it knows about ViGEm. <see cref="DroidOSS.Core"/> holds
/// no reference to any driver library, which is also what makes it testable
/// without a driver installed.
///
/// Slot <i>assignment</i> is not this interface's job — a backend owns four pads
/// and is told which one to act on.
/// </remarks>
public interface IPadBackend : IDisposable
{
    /// <summary>How many pads a backend can present. Four is XInput's own limit.</summary>
    const int MaxPads = 4;

    /// <summary>
    /// Plug in the virtual pad for <paramref name="slot"/>. Windows enumerates a
    /// new controller at this moment, and games will see it appear.
    /// </summary>
    void Connect(int slot);

    /// <summary>Push a new state snapshot to the pad in <paramref name="slot"/>.</summary>
    /// <remarks>Called at the packet rate, so it must not allocate.</remarks>
    void Submit(int slot, in PadState state);

    /// <summary>
    /// Unplug the pad in <paramref name="slot"/>.
    /// </summary>
    /// <remarks>
    /// Submit <see cref="PadState.Neutral"/> <b>before</b> calling this. Some
    /// games latch the last state they saw when a controller disappears, so
    /// unplugging mid-input can leave a stick stuck down.
    /// </remarks>
    void Disconnect(int slot);

    /// <summary>
    /// Raised when a game asks a pad to vibrate.
    /// </summary>
    /// <remarks>
    /// The only data that travels backwards through this system: game → driver →
    /// here → over the network → the phone's vibration motor.
    /// Fires on a driver thread, not the caller's.
    /// </remarks>
    event EventHandler<RumbleEventArgs>? RumbleReceived;
}
