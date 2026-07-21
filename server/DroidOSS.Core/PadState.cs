using System.Runtime.InteropServices;

namespace DroidOSS.Core;

/// <summary>
/// The complete state of one gamepad at one instant — a snapshot, never an event.
/// </summary>
/// <remarks>
/// The field order and sizes here are <b>not</b> arbitrary. They mirror Windows'
/// <c>XINPUT_GAMEPAD</c> structure exactly, which is also bytes 8–19 of the input
/// packet described in <c>docs/PROTOCOL.md</c>. That alignment is what lets the
/// server read state straight off the wire and hand it to the driver without
/// converting anything.
///
/// Changing a field, its order, or its size breaks the wire protocol. The size
/// is guarded by a test.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PadState
{
    /// <summary>All sixteen buttons, one bit each. See <see cref="GamepadButtons"/>.</summary>
    public ushort Buttons;

    /// <summary>Left trigger travel, 0–255.</summary>
    public byte LeftTrigger;

    /// <summary>Right trigger travel, 0–255.</summary>
    public byte RightTrigger;

    /// <summary>Left stick X. −32768 (full left) to 32767 (full right).</summary>
    public short ThumbLX;

    /// <summary>Left stick Y. Positive is <b>up</b> — screens count the other way.</summary>
    public short ThumbLY;

    /// <summary>Right stick X.</summary>
    public short ThumbRX;

    /// <summary>Right stick Y. Positive is <b>up</b>.</summary>
    public short ThumbRY;

    /// <summary>Everything released, both sticks centred.</summary>
    /// <remarks>
    /// Submitted before unplugging a pad. A game that latches the last state it
    /// saw would otherwise hold a stick down forever.
    /// </remarks>
    public static PadState Neutral => default;

    /// <summary>True when this state holds the given button down.</summary>
    public readonly bool IsPressed(GamepadButtons button) => (Buttons & (ushort)button) != 0;
}
