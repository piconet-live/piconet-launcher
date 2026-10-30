using System.Globalization;
using System.Text.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The player's replays (protocol/replay.toml, the format's one definition; formats.md section 1). The file's parts
/// are read by the generated reader (<see cref="ReplayParts"/>, protocol/generated/ReplayFile.g.cs); this judges them:
/// what the list shows, and why a file is not played. Shareable: the file carries everything its
/// playback needs; the fork plays it (Core.FzgxPlay) as the race (Core.FzgxPlayMode race).
/// </summary>
public static class ReplayFile
{
    public static IReadOnlyList<ReplayInfo> List(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return [];
        }
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*.fzrp").Select(Read).OfType<ReplayInfo>().OrderByDescending(r => r.Recorded)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or System.Security.SecurityException)
        {
            // a folder that cannot be listed (not the player's to read, gone while it was read, a path that is none)
            // has no replays to show
            return [];
        }
    }

    /// <summary>A replay's header, or null when the file is not a replay of a version this game plays.</summary>
    public static ReplayInfo? Read(string path) => Listed(path, ReplayParts.Read(path));

    /// <summary>What the list shows of a file: a replay of this format whose header and frames' block are whole and
    /// whose header names a match. (The frames' mark, the game and the steps are <see cref="Problem"/>'s.)</summary>
    private static ReplayInfo? Listed(string path, ReplayParts file)
    {
        if (!file.Marked || !ReplayFormat.Plays(file.Version) || file.Header is null || !file.Frames || file.Failed)
        {
            return null;
        }
        try
        {
            var (json, results) = (file.Header, file.Results);
            var steps = file.Compact ? file.Steps : file.StepBytes / file.StepSize;

            using var doc = JsonDocument.Parse(json);
            var h = doc.RootElement;
            var match = h.GetProperty(ReplayFormat.Header.Match);
            var setup = match.GetProperty("setup");
            var seat = h.TryGetProperty(ReplayFormat.Header.Seat, out var s) ? s.GetInt32() : 0;
            var players = new List<string>();
            var racers = new Dictionary<int, ReplayRacer>();
            var you = "";
            foreach (var seatSetup in setup.GetProperty("seats").EnumerateArray())
            {
                var isPlayer = seatSetup.TryGetProperty("playerId", out var id) && id.ValueKind == JsonValueKind.String;
                var name = seatSetup.GetProperty("name").GetString() ?? "";
                var number = seatSetup.GetProperty("seat").GetInt32();
                racers[number] = new ReplayRacer(number, name, seatSetup.GetProperty("machine").GetInt32(), Color(seatSetup), isPlayer,
                    Custom(seatSetup));
                if (isPlayer)
                {
                    players.Add(name);
                    if (number == seat)
                    {
                        you = name;
                    }
                }
            }
            var recorded = h.TryGetProperty(ReplayFormat.Header.Recorded, out var r) &&
                           DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
                ? t
                : new DateTimeOffset(File.GetLastWriteTimeUtc(path));
            var (pack, packProblem) = PackOf(file.RaceVersion, setup);
            return new ReplayInfo(path, recorded, setup.GetProperty("course").GetInt32(), setup.GetProperty("laps").GetInt32(),
                players, you, TimeSpan.FromSeconds(steps / 60.0), racers.GetValueOrDefault(seat),
                results is { } block ? Results(block, racers) : null, pack, packProblem,
                h.TryGetProperty(ReplayFormat.Header.Practice, out var practice) && practice.ValueKind == JsonValueKind.True);
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>The stat pack of a replay's race (its header's <c>match.setup.stats</c>): its name, and why the game
    /// would not play the file for it. Format 3 is a race with a pack and format 2 one without: the game refuses a
    /// file where the two disagree, and a pack that is none or holds a value outside its ranges, which the launcher
    /// judges by the same rows (<see cref="StatPack.FromWire"/>). So the list says it, and no game is started for it.</summary>
    private static (string? Name, string? Problem) PackOf(int version, JsonElement setup)
    {
        if (!setup.TryGetProperty("stats", out var stats) || stats.ValueKind == JsonValueKind.Null)
        {
            return (null, version == StatsFormatVersion ? "This replay says it was raced on a stat pack and holds none: the game does not play it." : null);
        }
        string? Text(string member) =>
            stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(member, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var (pack, refusal) = StatPack.FromWire(new StatPackWire(Text("name")!, Text("fingerprint")!, Text("values")!));
        if (pack is null)
        {
            return (null, "This replay's stat pack is not one this launcher takes: " + refusal);
        }
        return (pack.Name, version is StatsFormatVersion or CustomFormatVersion or MinesFormatVersion or SeatsFormatVersion or GridFormatVersion ? null
            : "This replay holds a stat pack in the format of races without one: the game does not play it.");
    }

    /// <summary>What reading a file that is no sound replay throws. A replay is a file somebody sent, and the list
    /// is read when the launcher starts: every way its header, its lengths or its results can be wrong ends here
    /// as "not a replay", never as an exception in the window. Beside the file
    /// and JSON errors: a member that is missing or of another kind (KeyNotFound, InvalidOperation: also text that is
    /// not UTF-8), a number that does not fit (Format, Overflow) and a time that is none (ArgumentOutOfRange).</summary>
    private static bool Unreadable(Exception e) =>
        e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or EndOfStreamException
            or UnauthorizedAccessException or FormatException or OverflowException or ArgumentException or NotSupportedException;

    /// <summary>The replay format this game writes and plays.</summary>
    public const int FormatVersion = ReplayFormat.VersionPlain;

    /// <summary>The compact file a recording becomes at its race's end (0.5.2 on): the recording of its own version
    /// (<see cref="ReplayParts.RaceVersion"/>), its header, inputs and checksums compressed (xz). The list reads its
    /// header (through SharpCompress), its steps' count and its results; only the game reads its steps. A launcher
    /// from before 0.5.2 says it is a replay of another version of the game.</summary>
    public const int CompactFormatVersion = ReplayFormat.CompactVersion;

    /// <summary>The format of a race that ran on a stat pack (its header's match holds the pack): the same file
    /// under another number, so that a game program from before stat packs refuses it and does not play the race
    /// on the disc's own car data.</summary>
    public const int StatsFormatVersion = ReplayFormat.VersionStats;

    /// <summary>The format of a race where a seat raced a custom machine (a seat of its header's match names one):
    /// the same file under another number, so that a game program from before custom machines refuses it.</summary>
    public const int CustomFormatVersion = ReplayFormat.VersionCustom;

    /// <summary>The format of a race whose mines came back (its header's match names the host's rule,
    /// <c>rules.mineRegenMs</c>): the same file under another number, so that a game program from before the rule
    /// refuses it and does not play the race with the mines gone. A file of another format with the rule in it, or of
    /// this one without, is refused by the game.</summary>
    public const int MinesFormatVersion = ReplayFormat.VersionMines;

    /// <summary>The format of a race where a seat raced a pack of its own (its header's match names
    /// <c>setup.packs</c> or a seat's <c>pack</c>; the protocol's handbook/stat-pack-constants.md): the same file under another number,
    /// whatever else the race holds, so that a game program from before refuses it.</summary>
    public const int SeatsFormatVersion = ReplayFormat.VersionSeats;

    /// <summary>The format of a race that started on the coordinator's grid (its header's match names
    /// <c>setup.grid</c>: the players ahead of the CPUs, in reverse of the lobby's last finish): the same file under
    /// another number, whatever else the race holds, so that a game program from before refuses it and does not play
    /// the race from the game's own grid.</summary>
    public const int GridFormatVersion = ReplayFormat.VersionGrid;

    /// <summary>The steps after which a mine of the replay's race came back, as the game reads its header's rule
    /// (the race block's word, <see cref="RaceWords"/>: the protocol's rules.toml); 0 when the header names none, as
    /// every recording made before the rule.</summary>
    public static int MineRegenSteps(JsonElement header) =>
        header.TryGetProperty(ReplayFormat.Header.Match, out var match) && match.ValueKind == JsonValueKind.Object &&
        match.TryGetProperty("setup", out var setup) && setup.ValueKind == JsonValueKind.Object &&
        setup.TryGetProperty("rules", out var rules)
            ? (int)RaceWords.Word(HostRules.MinesReturn, rules)
            : 0;

    public const string MinesDisagree = "This replay's mine rule and its format disagree: the game does not play it.";

    public const string NotAReplay = "Not a replay of this game";

    public const string Incomplete = "Replay file incomplete";

    /// <summary>Why a replay file cannot be played, in the player's words; null when the file is whole and this
    /// game's: a stat pack the game or this launcher would not take (<see cref="ReplayInfo.StatPackProblem"/>),
    /// another format version (another version of the game program wrote it), another game than
    /// <see cref="DiscCheck.GameId"/> (a header that names none is the fork's, which ran only this one), or a file
    /// that ends early (a header, a lineup or a step cut off, or no step at all: every file the game closed ends on
    /// a whole step, a race cut short included).</summary>
    public static string? Problem(string path)
    {
        var file = ReplayParts.Read(path);
        if (!file.Marked)
        {
            return NotAReplay;
        }
        if (!ReplayFormat.Plays(file.Version))
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Replay of another version of the game · format {file.Version}");
        }
        if (file.Compact && file.RaceVersion == 0)
        {
            // a compact file of a recording whose version this game does not play
            return string.Create(CultureInfo.InvariantCulture,
                $"Replay of another version of the game · format {file.Version}");
        }
        if (file.HeaderLength > ReplayFormat.MaxJson)
        {
            return NotAReplay;
        }
        if (file.Header is null || !file.Frames)
        {
            // (a compact file whose header does not read, or cut inside its parts: damaged, or not one at all)
            return file.Failed ? NotAReplay : Incomplete;
        }
        if (!file.FramesMarked)
        {
            return NotAReplay;
        }
        try
        {
            using var doc = JsonDocument.Parse(file.Header);
            if (doc.RootElement.TryGetProperty(ReplayFormat.Header.Game, out var game) && game.ValueKind == JsonValueKind.String &&
                game.GetString() is { } id && id != DiscCheck.GameId)
            {
                return $"Replay of another version of F-Zero GX · {id}";
            }
            if (file.RaceVersion is not (SeatsFormatVersion or GridFormatVersion) && (file.RaceVersion == MinesFormatVersion) != (MineRegenSteps(doc.RootElement) != 0))
            {
                return MinesDisagree;
            }
        }
        catch (Exception e) when (Unreadable(e))
        {
            return NotAReplay;
        }
        if (file.Failed)
        {
            return NotAReplay;
        }
        // (no step at all is a file cut short too: every file the game closed holds one)
        if (!file.Whole || file.Steps == 0)
        {
            return Incomplete;
        }
        return Listed(path, file) is not { } listed ? NotAReplay : listed.StatPackProblem;
    }

    /// <summary>What recorded a replay, from its header: the engine that ran the game (<c>engine</c>: the recompiled
    /// game's native code, or the emulator's JIT) and the native module by its SHA-1 (<c>module_sha1</c>); null
    /// where the header does not say (the fork's replays say neither).</summary>
    public static (string? Engine, string? Module) RecordedBy(string path)
    {
        // (of any format version: only the header's place is the same in all)
        if (ReplayParts.Read(path) is not { Marked: true, Header: { } json })
        {
            return (null, null);
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? Text(string key) =>
                doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Text(ReplayFormat.Header.Engine), Text(ReplayFormat.Header.ModuleSha1));
        }
        catch (Exception e) when (Unreadable(e))
        {
            return (null, null);
        }
    }

    /// <summary>The mod image a replay was recorded with: the SHA-1 its header names (<c>mod_sha1</c>, hex), or null
    /// when the file is not a replay or does not say. Playback is the recorded race only when the game playing it
    /// simulates as the recording game did (<see cref="RecompLaunch.RecordedWithThisImage"/>).</summary>
    public static string? ModImage(string path)
    {
        if (ReplayParts.Read(path) is not { Marked: true, Header: { } json } parts || !ReplayFormat.Plays(parts.Version))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(ReplayFormat.Header.ModSha1, out var sha) && sha.ValueKind == JsonValueKind.String &&
                   sha.GetString() is { Length: 40 } text
                ? text
                : null;
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    private static List<RaceResult> Results(byte[] json, Dictionary<int, ReplayRacer> racers)
    {
        var list = new List<(int Rank, RaceResult Result)>();
        foreach (var e in ReplayFormat.ParseResults(json))
        {
            var racer = racers.GetValueOrDefault(e.Seat) ?? new ReplayRacer(e.Seat, "CPU", 0, null, false);
            var time = e.TimeMs is { } t ? TimeSpan.FromMilliseconds(t) : (TimeSpan?)null;
            var best = e.BestLapMs > 0 ? TimeSpan.FromMilliseconds(e.BestLapMs) : (TimeSpan?)null;
            list.Add((e.Rank, new RaceResult(racer, 0, e.Finished, e.Out, time, best)));
        }
        // the game's ranks put the knocked-out racers among the rest by distance; the list puts them last
        return [.. list.OrderBy(x => x.Result.Out).ThenBy(x => x.Rank).Select((x, i) => x.Result with { Place = i + 1 })];
    }

    private static Online.Protocol.Rgb? Color(JsonElement seat) =>
        seat.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Object
            ? new Online.Protocol.Rgb((byte)c.GetProperty("r").GetInt32(), (byte)c.GetProperty("g").GetInt32(), (byte)c.GetProperty("b").GetInt32())
            : null;

    /// <summary>A seat's custom machine (its <c>custom</c>), when it is one the game can build; else null.</summary>
    private static CustomMachine? Custom(JsonElement seat)
    {
        if (!seat.TryGetProperty("custom", out var c) || c.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        try
        {
            return CustomMachine.Checked(c.Deserialize<CustomMachine>(ControlProtocol.Json));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Deletes a replay file (false if it could not).</summary>
    public static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
