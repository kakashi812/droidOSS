namespace DroidOSS.Core;

/// <summary>
/// A game's request that one pad vibrate.
/// </summary>
/// <param name="slot">Which pad the request is for.</param>
/// <param name="largeMotor">
/// The heavy low-frequency motor, 0–255. Weight this higher when collapsing both
/// motors into a phone's single vibrator — it carries most of what a player feels.
/// </param>
/// <param name="smallMotor">The light high-frequency motor, 0–255.</param>
public sealed class RumbleEventArgs(int slot, byte largeMotor, byte smallMotor) : EventArgs
{
    public int Slot { get; } = slot;
    public byte LargeMotor { get; } = largeMotor;
    public byte SmallMotor { get; } = smallMotor;
}
