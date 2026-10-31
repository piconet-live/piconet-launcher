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

/// <summary>The disc check's answers for the pages, which ask at every refresh on the window's thread: an answer
/// known is given at once and looked at again off that thread when it is some seconds old (<paramref name="changed"/>
/// is called, on another thread, when it came out different). A path never asked about is read there and then, once:
/// the launcher asks for its own disc before its window shows.</summary>
public sealed class DiscAnswers(Action changed)
{
    private const long OldMs = 5000;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DiscStatus Status, long At, bool Looking)> _answers = new(StringComparer.Ordinal);

    public DiscStatus Known(string? path)
    {
        var key = path ?? "";
        lock (_gate)
        {
            if (_answers.TryGetValue(key, out var answer))
            {
                if (!answer.Looking && Environment.TickCount64 - answer.At >= OldMs)
                {
                    _answers[key] = answer with { Looking = true };
                    _ = Task.Run(() => LookAgain(key));
                }
                return answer.Status;
            }
        }
        var status = DiscCheck.Check(path);
        lock (_gate)
        {
            _answers[key] = (status, Environment.TickCount64, false);
        }
        return status;
    }

    private void LookAgain(string key)
    {
        var status = DiscCheck.Check(key);
        bool different;
        lock (_gate)
        {
            different = _answers[key].Status != status;
            _answers[key] = (status, Environment.TickCount64, false);
        }
        if (different)
        {
            changed();
        }
    }
}
