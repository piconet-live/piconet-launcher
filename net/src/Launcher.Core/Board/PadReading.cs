namespace Launcher.Core.Board;

/// <summary>
/// The local player's controller as the game reads it, from the status board's LocalPad block (protocol/board.toml):
/// PADStatus of port 1 after the SDK's PADClamp, for the launcher's Inputs window ("Show inputs"). Picture only.
/// </summary>
/// <param name="Buttons">PADStatus's button bits (<see cref="Bits"/>).</param>
/// <param name="StickX">The stick, -72 to 72 (right and up positive).</param>
/// <param name="CStickX">The C-stick, -59 to 59.</param>
/// <param name="L">The analog triggers, 0 to 150.</param>
public sealed record PadReading(uint Buttons, int StickX, int StickY, int CStickX, int CStickY, int L, int R)
{
    /// <summary>The reach of the clamped stick, C-stick and triggers (the game's PADClamp table at 0x8008FF40).</summary>
    public const int StickReach = 72, CStickReach = 59, TriggerReach = 150;

    /// <summary>The buttons' names (the controller helper's, <see cref="HelperPad"/>) with PADStatus's bit of each.</summary>
    public static IReadOnlyList<(string Name, uint Bit)> Bits { get; } =
    [
        ("Left", 0x0001), ("Right", 0x0002), ("Down", 0x0004), ("Up", 0x0008), ("Z", 0x0010), ("R", 0x0020), ("L", 0x0040),
        ("A", 0x0100), ("B", 0x0200), ("X", 0x0400), ("Y", 0x0800), ("Start", 0x1000),
    ];

    /// <summary>The buttons held, by name.</summary>
    public IReadOnlySet<string> Held => Bits.Where(b => (Buttons & b.Bit) != 0).Select(b => b.Name).ToHashSet();

    /// <summary>What the board's block says: null while it holds no pad (Held 0: a menu, a replay, a watched race). The
    /// numbers come from another process, so each is kept within its reach.</summary>
    public static PadReading? Of(BoardLocalPad block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Held != 1 ? null : new PadReading(block.Buttons & 0x1F7F,
            Math.Clamp(block.StickX, -StickReach, StickReach), Math.Clamp(block.StickY, -StickReach, StickReach),
            Math.Clamp(block.CStickX, -CStickReach, CStickReach), Math.Clamp(block.CStickY, -CStickReach, CStickReach),
            (int)Math.Min(block.L, TriggerReach), (int)Math.Min(block.R, TriggerReach));
    }
}
