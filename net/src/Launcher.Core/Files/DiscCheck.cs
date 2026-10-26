namespace Launcher.Core;

public enum DiscStatus
{
    /// <summary>No path, or no file there.</summary>
    Missing,

    /// <summary>A file the launcher cannot read as a disc image: a compressed or shrunk image, an archive, no disc
    /// image at all, or one that is cut short.</summary>
    Unsupported,

    /// <summary>A disc image of another game or region, or another revision of the game.</summary>
    WrongGame,

    /// <summary>F-Zero GX (USA), GFZE01, of the disc number and revision the launcher is made for.</summary>
    Ok,
}

/// <summary>Checks the player's disc image for the pages: the game's own disc, F-Zero GX USA
/// (GFZE01). What the right game is, is said in one place, <see cref="DiscPrep.Refusal"/>, which the preparation
/// starts with too: the kind of file by its first bytes (not by its name), the game's id, the disc number and the
/// revision. A disc that is <see cref="DiscStatus.Ok"/> here can still be refused by the preparation, which reads
/// every file.</summary>
public static class DiscCheck
{
    public const string GameId = "GFZE01";

    public static DiscStatus Check(string? path) => DiscPrep.Refusal(path)?.Outcome switch
    {
        null => DiscStatus.Ok,
        DiscPrepOutcome.ImageMissing => DiscStatus.Missing,
        DiscPrepOutcome.WrongGame or DiscPrepOutcome.WrongVersion => DiscStatus.WrongGame,
        _ => DiscStatus.Unsupported,
    };

    /// <summary>The sentence a preparation would refuse this file with at first look; null when
    /// <see cref="Check"/> is <see cref="DiscStatus.Ok"/>.</summary>
    public static string? Problem(string? path) => DiscPrep.Refusal(path)?.Message;
}
