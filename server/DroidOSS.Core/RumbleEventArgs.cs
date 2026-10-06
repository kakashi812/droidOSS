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

/// <summary>
/// Which player a pad is, and what colour its light should be — as the
/// driver, the game or Steam last set it.
/// </summary>
/// <param name="slot">Which pad.</param>
/// <param name="player">The player number games show, 1–4.</param>
/// <param name="colour">The colour to show for it on the phone.</param>
public sealed class LightEventArgs(int slot, byte player, Rgb colour) : EventArgs
{
    public int Slot { get; } = slot;
    public byte Player { get; } = player;
    public Rgb Colour { get; } = colour;
}
