namespace Launcher.Core;

// The game's menu sound bank, for the launcher's sounds (GameSounds).
public sealed partial class DiscArt
{
    /// <summary>The menu sound bank's bytes (<see cref="GameSounds.BankFile"/>); null when the disc has none or it
    /// cannot be read.</summary>
    public byte[]? SoundBank() => Read(GameSounds.BankFile);
}
