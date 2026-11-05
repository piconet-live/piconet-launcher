using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The racer's trail is one colour (the owner, 2026-11-04): it is the flame at rest, the flame boosting, the sparks of a
/// boost and the light the burner gives. The race's words stay three (<see cref="BoostTrail"/>: normal, boost,
/// sparks), so replays and launch files of the three-colour trail read as before. The game's own colours have no
/// simple relation between rest and boost (Blue Falcon rests (153,153,204) and boosts (153,204,153); booster part 1
/// (166,204,153) and (204,153,153): the hue moves), so one colour is all three words alike.
/// </summary>
public static class TrailColour
{
    /// <summary>The trail a race carries for one colour: the same in all three words; null (the game's own) for none.</summary>
    public static BoostTrail? Trail(Rgb? colour) => colour is null ? null : new BoostTrail(colour, colour, colour);

    /// <summary>The one colour of a trail: the boost flame's, else (boost never set) the flame's at rest, else the
    /// sparks'; null when none is set. A trail kept with three colours reads as this one.</summary>
    public static Rgb? Of(BoostTrail? trail) => trail?.Boost ?? trail?.Normal ?? trail?.Sparks;

    /// <summary>A trail as one colour: three colours kept by an earlier launcher become <see cref="Of"/>'s in all
    /// three words; one already alike, or none, stays as it is.</summary>
    public static BoostTrail? Migrated(BoostTrail? trail) => Trail(Of(trail));
}
