using System.Text.Json;

namespace Launcher.Core;

// What the game's folders hold, read: what is missing for a start, the build's name, a replay's image against the
// same-race table, a match file's name for its replay.
public static partial class RecompLaunch
{
    /// <summary>What is missing to start the game, in the player's words; null when everything is in place.</summary>
    public static string? Problem(RecompPaths paths) => Problem(paths, HostCpu.RunsTheGame);

    /// <summary><see cref="Problem(RecompPaths)"/> with the processor's answer given: a processor the module cannot
    /// run on (<see cref="HostCpu"/>) comes before anything about the files.</summary>
    public static string? Problem(RecompPaths paths, bool processorRunsTheGame)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!processorRunsTheGame)
        {
            return HostCpu.NotSupported;
        }
        if (!File.Exists(paths.Runner) || !File.Exists(paths.Module))
        {
            return paths.Installed ? NoProgramInstalled : paths.PlayerCopy ? NoProgramInACopy : NoProgram;
        }
        if (paths.NoGame || !File.Exists(Path.Combine(paths.GameFolder, "sys", "main.dol")))
        {
            return DiscPrep.NotPreparedMessage;
        }
        return null;
    }

    /// <summary>The runner or the module is not there, in a launcher that is no player copy.</summary>
    public const string NoProgram = "Game program not installed";

    /// <summary>The same in a player copy, into which nothing is ever installed: the copy is not whole.</summary>
    public const string NoProgramInACopy = "Game program missing · unzip the copy again";

    public const string NoOnlinePart = "Online part not installed";

    /// <summary>The same in an installed launcher, whose folder Setup writes whole: running Setup again repairs it.</summary>
    public const string NoProgramInstalled = "Game program missing · run Setup again";

    public const string NoOnlinePartInstalled = "Online part missing · run Setup again";

    public const string NoOnlinePartInACopy = "Online part missing · unzip the copy again";

    /// <summary>What the first screen has to prepare from the player's disc before the launcher opens: the state of
    /// the prepared folder, with its sentence; null when there is nothing to prepare. Nothing is, when a game start
    /// has the game's files (the prepared folder is ready, after its words were brought up to date without the disc
    /// if that was all it took: <see cref="DiscPrep.ReadyFolder"/>; or the launcher's own <c>recomp\game</c> has
    /// them, as a development copy does and a player copy never), when the settings name a game folder (a development start: a prepared
    /// folder would not be used), and when the game program is not installed (nothing could start from the files:
    /// the play buttons say so, <see cref="Problem"/>).</summary>
    /// <param name="playerCopy">The launcher is a player copy: a <c>recomp\game</c> in it does not count
    /// (<see cref="Resolve(RecompSettings?, string, string, DiscPins?, bool)"/>).</param>
    public static PreparedStatus? PreparationWanted(RecompSettings? settings, string launcherFolder, string dataFolder, DiscPins? pins = null,
        bool playerCopy = false) => Look(settings, launcherFolder, dataFolder, pins, playerCopy).Wanted;

    /// <summary>One look at what a game start has, for the first screen and for the buttons that start a game: the
    /// paths (<see cref="Resolve(RecompSettings?, string, string, DiscPins?, bool)"/>, which reads the prepared
    /// folder's state once and brings a stale one up to date), what a start lacks, and what the first screen has to
    /// prepare (<see cref="PreparationWanted"/> says when that is nothing). With files that are ready this opens the
    /// prepared game's files once; only when something is to prepare is their state read a second time, for its
    /// sentence. It reads thousands of files: never on the window's thread but for the very first answer.</summary>
    /// <param name="installed">The launcher is an installed one (<see cref="Install.Installed"/>): its problems say so.</param>
    public static GameLook Look(RecompSettings? settings, string launcherFolder, string dataFolder, DiscPins? pins = null, bool playerCopy = false,
        bool installed = false)
    {
        var paths = Resolve(settings, launcherFolder, dataFolder, pins, playerCopy) with { Installed = installed };
        var problem = Problem(paths);
        PreparedStatus? wanted = null;
        if (problem is not null && problem != HostCpu.NotSupported && settings?.GameFolder is null && File.Exists(paths.Runner) && File.Exists(paths.Module))
        {
            wanted = DiscPrep.Status(dataFolder, paths.Bundle, pins);
            if (wanted.State == PreparedState.Ready)
            {
                // made ready between the two readings (another window's preparation): the paths again
                paths = Resolve(settings, launcherFolder, dataFolder, pins, playerCopy) with { Installed = installed };
                (problem, wanted) = (Problem(paths), null);
            }
            else if (wanted is { State: PreparedState.Stale, NeedsDisc: false })
            {
                problem = wanted.Message;   // whole files that could not be brought up to date: why, not "not prepared"
            }
        }
        return new GameLook(paths, problem, problem ?? RaceProblem(paths), wanted);
    }

    /// <summary>What is missing to start a race; null when everything is in place.</summary>
    public static string? RaceProblem(RecompPaths paths)
    {
        if (Problem(paths) is { } problem)
        {
            return problem;
        }
        return paths.Bundle is null || !File.Exists(Path.Combine(paths.Bundle, "mod.bin"))
            ? paths.Installed ? NoOnlinePartInstalled : paths.PlayerCopy ? NoOnlinePartInACopy : NoOnlinePart
            : null;
    }

    /// <summary>Why no race is started from this match file; null when the file is there.</summary>
    public static string? MatchFileProblem(string matchFile) =>
        File.Exists(matchFile) ? MatchStatPackProblem(matchFile) : "Match file missing";

    /// <summary>Why a match file's stat pack is not one this launcher hands to the game (<c>match.setup.stats</c>;
    /// <see cref="Online.Protocol.StatPack.FromWire"/>: no pack at all, or a value the game refuses; the game's
    /// own ranges, and nothing narrower); null when it has none or the pack is one. A file that is no JSON is the
    /// game's to refuse, as before.</summary>
    private static string? MatchStatPackProblem(string matchFile)
    {
        try
        {
            if (new FileInfo(matchFile).Length > 1 << 20)
            {
                return null;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(matchFile));
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object || !doc.RootElement.TryGetProperty("match", out var match) ||
                match.ValueKind != System.Text.Json.JsonValueKind.Object || !match.TryGetProperty("setup", out var setup) ||
                setup.ValueKind != System.Text.Json.JsonValueKind.Object || !setup.TryGetProperty("stats", out var stats) ||
                stats.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                return null;
            }
            string? Text(string member) => stats.ValueKind == System.Text.Json.JsonValueKind.Object && stats.TryGetProperty(member, out var v) &&
                                           v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            return Online.Protocol.StatPack.FromWire(new Online.Protocol.StatPackWire(Text("name")!, Text("fingerprint")!, Text("values")!)).Refusal is { } why
                ? "The match's stat pack is not one this launcher takes: " + why
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the mod bundle is of the native form: it has a patch list (<c>patches.json</c>).</summary>
    private static bool NativeBundle(RecompPaths paths) => paths.Bundle is { } folder && File.Exists(Path.Combine(folder, "patches.json"));

    /// <summary>What is missing to play a replay, or why this one is not played; null when everything is in place:
    /// the game and its online part, the file, a whole replay of this game in this format
    /// (<see cref="ReplayFile.Problem"/>), recorded with this game's mod image or one that is the same race
    /// (<see cref="RecordedWithThisImage"/>; a replay that names no image is played).</summary>
    public static string? PlaybackProblem(RecompPaths paths, string replayFile)
    {
        if (RaceProblem(paths) is { } problem)
        {
            return problem;
        }
        if (!File.Exists(replayFile))
        {
            return "Replay file missing";
        }
        if (ReplayFile.Problem(replayFile) is { } broken)
        {
            return broken;
        }
        return RecordedWithThisImage(paths, replayFile) == false ? OtherImage : null;
    }

    /// <summary>What a player is told of a replay recorded with a mod image that no row of the table names as the
    /// same race as this game's (<see cref="RecordedWithThisImage"/>): it is not played.</summary>
    public const string OtherImage =
        "Replay of another version of the game";

    /// <summary>A replay's file name: the time and the match. The match's id comes from the coordinator, so only
    /// what a file name may hold of it is used (<see cref="FileNamePart"/>): the name never leaves its folder.</summary>
    public static string ReplayFileName(DateTimeOffset time, string matchId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{time:yyyyMMdd-HHmmss}-{FileNamePart(matchId)}.fzrp");

    /// <summary>A match's id as a part of a file name: its letters, digits, '-' and '_' (what the coordinator makes
    /// ids of), every other character as '_', 40 characters at most; "match" for none. An id is a server's word: one
    /// with a path in it ("..\..\x") named a file outside the replay folder, which the game then wrote.</summary>
    public static string FileNamePart(string? matchId)
    {
        var part = new string([.. (matchId ?? "").Take(40).Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);
        return part.Length == 0 ? "match" : part;
    }

    /// <summary>Where a race from a match file is recorded when nobody says: the replay folder, named by the time
    /// and the match (<see cref="ReplayFileName"/>), so that every race leaves its
    /// replay where the Replays page lists them. Null: there is no replay folder, or the match file names no
    /// match.</summary>
    public static string? ReplayFileFor(string? replayFolder, string matchFile, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(replayFolder))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(matchFile));
            return doc.RootElement.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object &&
                   match.TryGetProperty("matchId", out var id) && id.ValueKind == JsonValueKind.String &&
                   id.GetString() is { Length: > 0 } name && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? Path.Combine(replayFolder, ReplayFileName(now, name))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>What this player's game is made of, for the coordinator (<see cref="Online.Protocol.Hello.Build"/>):
    /// the SHA-1 of the online part's image, of the native module (the two a replay's header names) and of the game
    /// program that runs them (the runner: the emulated hardware and the host side of online play),
    /// "image/module/runner". Two games race each other only when all three are equal: the
    /// image is the race's own code, the module the game's code as compiled, and the runner computes the rest. Null
    /// when one of the files is missing (no race can be started then).</summary>
    public static string? Build(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var image = paths.Bundle is null ? null : Path.Combine(paths.Bundle, "mod.bin");
        if (image is null || !File.Exists(image) || !File.Exists(paths.Module) || !File.Exists(paths.Runner))
        {
            return null;
        }
        try
        {
            return $"{Sha1(image)}/{Sha1(paths.Module)}/{Sha1(paths.Runner)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly Dictionary<(string Path, long Length, DateTime Written), string> Hashes = [];

    /// <summary>A file's SHA-1 in lower-case hexadecimal (an identity, as the replay format names files; not a
    /// protection), kept for the file as it is now: the module is 150 MB.</summary>
    private static string Sha1(string path)
    {
        var info = new FileInfo(path);
        var key = (info.FullName, info.Length, info.LastWriteTimeUtc);
        lock (Hashes)
        {
            if (Hashes.TryGetValue(key, out var known))
            {
                return known;
            }
        }
        using var stream = File.OpenRead(path);
#pragma warning disable CA5350
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();
#pragma warning restore CA5350
        lock (Hashes)
        {
            Hashes[key] = hash;
        }
        return hash;
    }

    /// <summary>The table of mod images that are the same race, staged beside the bundle's image
    /// (<c>port/image/same-race.json</c>): rows of two SHA-1s, <c>recorded</c> and <c>plays_on</c>.</summary>
    public const string SameRaceFile = "same-race.json";

    /// <summary>Whether a replay was recorded with the online part this game has (the mod image of
    /// <see cref="RecompPaths.Bundle"/>, by its SHA-1, which a replay's header names), or with one that is the same
    /// race. A replay plays back as recorded only when the game simulates the race as the recording game did; with
    /// another image it may differ from some step on. Images are the same race only
    /// by the table beside the bundle's image (<see cref="SameRaceFile"/>): each of its rows is a run in which a
    /// recording made with the one played back on the other with no step differing (the fork's image and the
    /// native form's, built from the same routines, are two files). An image in no row is another image.
    /// Null: the replay does not say, or there is no bundle.</summary>
    public static bool? RecordedWithThisImage(RecompPaths paths, string replayFile)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var image = paths.Bundle is null ? null : Path.Combine(paths.Bundle, "mod.bin");
        if (image is null || !File.Exists(image) || ReplayFile.ModImage(replayFile) is not { } recorded)
        {
            return null;
        }
        // SHA-1 because that is what the replay format names the image by (an identity, not a protection)
        var ours = Sha1(image);
        return ours.Equals(recorded, StringComparison.OrdinalIgnoreCase) ||
            SameRace(Path.Combine(paths.Bundle!, SameRaceFile), recorded, ours);
    }

    /// <summary>Whether the table names the pair. A table that is missing or cannot be read names nothing.</summary>
    private static bool SameRace(string table, string recorded, string ours)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(table));
            if (!doc.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object &&
                    row.TryGetProperty("recorded", out var from) && from.ValueKind == JsonValueKind.String &&
                    row.TryGetProperty("plays_on", out var on) && on.ValueKind == JsonValueKind.String &&
                    recorded.Equals(from.GetString(), StringComparison.OrdinalIgnoreCase) &&
                    ours.Equals(on.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
