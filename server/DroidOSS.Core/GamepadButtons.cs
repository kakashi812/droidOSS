namespace DroidOSS.Core;

/// <summary>
/// The sixteen gamepad buttons, packed one per bit into a single 16-bit value.
/// </summary>
/// <remarks>
/// These values are fixed by Windows' XInput API — they are not ours to choose.
/// They are also transmitted verbatim in bytes 8–9 of the input packet, so the
/// Kotlin and Python implementations must use identical constants.
/// The canonical table lives in <c>docs/PROTOCOL.md</c>; a test guards these
/// against transcription errors.
///
/// Bit 0 is the rightmost binary digit, which is why the d-pad occupies the low
/// bits and the face buttons the high ones.
/// </remarks>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0x0000,

    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,

    Start = 0x0010,
    Back = 0x0020,

    LeftThumb = 0x0040,
    RightThumb = 0x0080,

    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,

    /// <summary>The centre Xbox button. Unofficial, but ViGEm supports it.</summary>
    Guide = 0x0400,

    // 0x0800 is unused by XInput.

    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}
