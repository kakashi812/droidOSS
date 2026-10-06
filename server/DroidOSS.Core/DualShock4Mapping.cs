namespace DroidOSS.Core;

/// <summary>Which controller Windows and games are shown.</summary>
public enum PadKind
{
    /// <summary>The default: every PC game understands it. No light colour.</summary>
    Xbox360,

    /// <summary>
    /// A PlayStation 4 pad, whose lightbar colour games and Steam set — which is
    /// how a phone can show FIFA's or Steam's own colour. Games show PlayStation
    /// button icons, and some older ones only see it through Steam Input.
    /// </summary>
    DualShock4,
}

/// <summary>The eight D-pad directions a DualShock 4 report can hold, plus none.</summary>
public enum DPadDirection
{
    None, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest,
}

/// <summary>
/// Converts the wire's Xbox-shaped state into what a DualShock 4 report wants.
/// </summary>
/// <remarks>
/// The phone always speaks Xbox (<see cref="PadState"/> is <c>XINPUT_GAMEPAD</c>);
/// only the server knows a PS4 pad is being shown. The differences that matter:
/// DualShock sticks are one unsigned byte, centre 128, with Y counting
/// <b>down</b>; its D-pad is a single direction rather than four buttons; and
/// L2/R2 are a digital press as well as an analog travel.
/// </remarks>
public static class DualShock4Mapping
{
    /// <summary>Trigger travel past which L2/R2 also report a digital press.</summary>
    public const byte TriggerPressThreshold = 30;

    /// <summary>−32768…32767 (right positive) → 0…255, centre 128.</summary>
    public static byte StickX(short x) => (byte)((x + 32768) >> 8);

    /// <summary>−32768…32767 (<b>up</b> positive) → 0…255 (<b>down</b> positive), centre 128.</summary>
    public static byte StickY(short y) => (byte)Math.Min(255, (32768 - y) >> 8);

    /// <summary>Four D-pad bits → one direction. Opposites cancel, as on a real pad.</summary>
    public static DPadDirection DPad(ushort buttons)
    {
        var up = (buttons & (ushort)GamepadButtons.DPadUp) != 0;
        var down = (buttons & (ushort)GamepadButtons.DPadDown) != 0;
        var left = (buttons & (ushort)GamepadButtons.DPadLeft) != 0;
        var right = (buttons & (ushort)GamepadButtons.DPadRight) != 0;

        var v = up == down ? 0 : up ? 1 : -1;
        var h = left == right ? 0 : right ? 1 : -1;

        return (v, h) switch
        {
            (1, 0) => DPadDirection.North,
            (1, 1) => DPadDirection.NorthEast,
            (0, 1) => DPadDirection.East,
            (-1, 1) => DPadDirection.SouthEast,
            (-1, 0) => DPadDirection.South,
            (-1, -1) => DPadDirection.SouthWest,
            (0, -1) => DPadDirection.West,
            (1, -1) => DPadDirection.NorthWest,
            _ => DPadDirection.None,
        };
    }

    /// <summary>Whether a trigger's travel also counts as a press of L2/R2.</summary>
    public static bool TriggerPressed(byte travel) => travel >= TriggerPressThreshold;
}

/// <summary>
/// What a game or Steam asked of a DualShock 4: rumble, lightbar, or both.
/// </summary>
/// <remarks>
/// Parsed from the raw USB output report, the same bytes a real pad receives:
///
/// <code>
/// byte 0     1        2   3   4            5            6  7  8
///      0x05  flags0   ..  ..  right motor  left motor   R  G  B
/// </code>
///
/// <c>flags0</c> says which parts are meant: bit 0 the motors, bit 1 the
/// lightbar. A report that sets only the lightbar carries zeros where the
/// motors would be, and must not be read as "stop vibrating". The right motor
/// is the light, fast one — Xbox's "small" — and the left the heavy one.
/// </remarks>
public static class DualShock4OutputReport
{
    public const byte UsbReportId = 0x05;
    public const byte FlagRumble = 0x01;
    public const byte FlagLightbar = 0x02;

    private const int MinLength = 9;

    public static bool TryParse(
        ReadOnlySpan<byte> report, out (byte Large, byte Small)? rumble, out Rgb? lightbar)
    {
        rumble = null;
        lightbar = null;
        if (report.Length < MinLength || report[0] != UsbReportId) return false;

        var flags = report[1];
        if ((flags & FlagRumble) != 0) rumble = (report[5], report[4]);
        if ((flags & FlagLightbar) != 0) lightbar = new Rgb(report[6], report[7], report[8]);
        return true;
    }
}
