using System.Text.Json;

namespace Launcher.Core;

/// <summary>A scripted Quick Play visit without the window and without a person (<see cref="QuickPlayScript"/>), as a
/// launch file's <c>"quickPlay"</c> asks for it.</summary>
/// <param name="Server">The coordinator (<c>"server"</c>). A visit names it, or its file is refused: the settings'
/// server is the official service's in a new data folder, and a test must never reach that one by default.</param>
/// <param name="Races">How many races the visit stays for (<c>"races"</c>).</param>
/// <param name="StartWith">The players in the lobby before it votes to start (<c>"startWith"</c>).</param>
/// <param name="VoteCourse">The course it votes for when the vote offers it (<c>"voteCourse"</c>); null: the vote's
/// first option.</param>
/// <param name="Watch">It only waits in the lobby (<c>"watch"</c>).</param>
/// <param name="Log">A file for the visit's lines, one line of JSON a step (<c>"log"</c>); null: standard output.</param>
/// <param name="TimeLimitSeconds">The longest the whole visit may take (<c>"timeLimitSeconds"</c>).</param>
/// <param name="Custom">The visit goes to a custom lobby and not Quick Play (<c>"custom"</c>).</param>
/// <param name="AfterLeave">What it presses when its game was closed in the race (<c>"afterLeave"</c>): "rejoin" or
/// "watch", as the lobby page offers them; null: nothing.</param>
public sealed record QuickPlayVisit(string Server, int Races = 1, int StartWith = 2, int? VoteCourse = null,
    bool Watch = false, string? Log = null, int TimeLimitSeconds = 600, QuickPlayCustom? Custom = null,
    string? AfterLeave = null);

/// <summary>A scripted visit's custom lobby (a launch file's <c>"quickPlay"."custom"</c>): the calls the custom lobby
/// pages' buttons make.</summary>
/// <param name="Create">It makes a lobby of this name, as its host (<c>"create"</c>).</param>
/// <param name="Join">It joins the lobby of this name when the list shows it (<c>"join"</c>).</param>
/// <param name="ModeFile">The host's mode file, as Choose file (<c>"modeFile"</c>).</param>
/// <param name="Values">The mode's parameters, in the package's order (<c>"values"</c>).</param>
/// <param name="Teams">The lobby's teams (<c>"teams"</c>).</param>
/// <param name="Team">The team this player takes (<c>"team"</c>).</param>
/// <param name="Rules">More of the lobby's rules, by name (<c>"rules"</c>).</param>
public sealed record QuickPlayCustom(string? Create = null, string? Join = null, string? ModeFile = null,
    IReadOnlyList<int>? Values = null, int? Teams = null, int? Team = null, IReadOnlyDictionary<string, int>? Rules = null);

/// <summary>
/// The test settings of one start of the launcher: a launch file's <c>"debug"</c> object (<see cref="LaunchConfig"/>).
/// Players and tests run the same build: nothing here is on unless a launch file names it,
/// and a start without a launch file has <see cref="Off"/>.
/// </summary>
public sealed record DebugSettings
{
    /// <summary>Every test setting off: a player's start.</summary>
    public static DebugSettings Off { get; } = new();

    /// <summary>The game window's title, and the launcher window's (<c>"title"</c>: a title that names the test); null:
    /// their own.</summary>
    public string? Title { get; init; }

    /// <summary>More arguments for the runner of every game this launcher starts (<c>"runnerArguments"</c>).</summary>
    public IReadOnlyList<string> RunnerArguments { get; init; } = [];

    /// <summary>Where the launcher's window opens, in the screen's pixels (<c>"windowAt"</c>: [X, Y]); it then opens
    /// without taking the keyboard from whoever is at the computer. Null: where the system puts it.</summary>
    public (int X, int Y)? WindowAt { get; init; }

    /// <summary>The tab the window opens on (<c>"windowPage"</c>: play, garage, replays or settings).</summary>
    public string? WindowPage { get; init; }

    /// <summary>A scripted Quick Play visit instead of the window (<c>"quickPlay"</c>).</summary>
    public QuickPlayVisit? QuickPlay { get; init; }

    /// <summary>The game's AI drives this player's seat in every race, which then ends by itself
    /// (<c>"autopilot"</c>; handed on to the game as its launch file's <c>"debug.autopilot"</c>).</summary>
    public bool Autopilot { get; init; }

    /// <summary>A round trip added to every packet between this player's game and the relay (<c>"testLagMs"</c>;
    /// handed on to the game as its launch file's <c>"debug.testLagMs"</c>).</summary>
    public int TestLagMs { get; init; }

    /// <summary>More test settings of the game, handed on as they are into the <c>"debug"</c> part of the launch file
    /// of every game this launcher starts (<c>"game"</c>: an object, kept as its text; <see cref="GameDebug"/>). The
    /// game's reader is their judge: a member it does not know stops the game's start.</summary>
    public string? Game { get; init; }

    /// <summary>How long a race's game may take from its start to the race running, in place of the launcher's own
    /// limit (<c>"startLimitSeconds"</c>); 0: the launcher's.</summary>
    public int StartLimitSeconds { get; init; }

    /// <summary>An error nothing catches, raised on this route of the handler of last resort (<c>"fault"</c>: window,
    /// task, thread, process or first), two seconds after the window opened.</summary>
    public string? Fault { get; init; }

    /// <summary>With <see cref="Fault"/>: raised at once and without the window (<c>"faultWithoutWindow"</c>).</summary>
    public bool FaultWithoutWindow { get; init; }

    /// <summary>A folder the 3D views write their checks to: a log line a second and their pictures
    /// (<c>"gpuLog"</c>).</summary>
    public string? GpuLog { get; init; }

    /// <summary>With <see cref="GpuLog"/>: the machine views turn steadily, to measure what a turning machine costs
    /// (<c>"gpuTurn"</c>).</summary>
    public bool GpuTurn { get; init; }

    /// <summary>Where the time of loading machines goes, a line per load in <c>load-trace.log</c> in the data folder
    /// (<see cref="LoadTrace"/>; <c>"loadTrace"</c>).</summary>
    public bool LoadTrace { get; init; }

    /// <summary>The Controller page and the check before a start never ask for the GameCube adapter, so a test's
    /// launcher takes it from nobody (<c>"leaveAdapter"</c>).</summary>
    public bool LeaveAdapter { get; init; }

    /// <summary>A setting here reaches a race: one handed on to the game (<see cref="Title"/>,
    /// <see cref="RunnerArguments"/>, and <see cref="Autopilot"/>, <see cref="TestLagMs"/>, <see cref="Game"/>: the
    /// game's own "debug" part, <see cref="GameDebug"/>), or one of the launcher's that acts in a race
    /// (<see cref="QuickPlay"/>: nobody is at the controls; <see cref="StartLimitSeconds"/>). The launcher says so in
    /// its hello (<see cref="Online.Protocol.Hello.Debug"/>), and a server that takes no test settings refuses it.
    /// The settings of the launcher's own window and checks (<see cref="WindowAt"/>, <see cref="WindowPage"/>,
    /// <see cref="Fault"/>, <see cref="GpuLog"/>, <see cref="GpuTurn"/>, <see cref="LoadTrace"/>, <see cref="LeaveAdapter"/>) reach no race. A setting named with the
    /// value it has anyway (<c>"autopilot": false</c>, an empty <c>"game"</c>) is not on.</summary>
    public bool ReachesARace => Title is not null || RunnerArguments.Count > 0 || QuickPlay is not null
        || StartLimitSeconds > 0 || GameDebug.From(this).Part() is not null;
}

/// <summary>
/// A launch file, given to the launcher as <c>--config FILE</c>: the one place a start's test settings are named
/// (one build for players and for tests, the test settings in a file instead of scattered
/// options and variables). Plain JSON with two members: <c>"about"</c>, a line that says what the file is for, and
/// <c>"debug"</c>, the test settings (<see cref="DebugSettings"/>). A member this reader does not know is refused,
/// with its name: a setting that was mistyped or has gone must not pass as "off".
/// </summary>
/// <param name="About">What the file is for, in its author's words.</param>
/// <param name="Debug">Its test settings.</param>
public sealed record LaunchConfig(string About, DebugSettings Debug)
{
    /// <summary>A launch file is a few lines: one longer than this is not read.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The pages a window can open on.</summary>
    public static readonly IReadOnlyList<string> Pages = ["play", "garage", "replays", "settings"];

    /// <summary>The routes an error can be raised on.</summary>
    public static readonly IReadOnlyList<string> FaultRoutes = ["window", "task", "thread", "process", "first"];

    /// <summary>Reads a launch file: the file, or the sentence it is refused with. Never raises.</summary>
    public static (LaunchConfig? Config, string? Problem) Read(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return (null, $"The launch file is not there: {path}");
            }
            if (new FileInfo(path).Length > MaxBytes)
            {
                return (null, $"The launch file is longer than {MaxBytes / 1024} KiB and was not read: {path}");
            }
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, $"The launch file could not be read ({e.Message}): {path}");
        }
        var (config, problem) = Parse(bytes);
        return (config, problem is null ? null : $"The launch file is refused: {problem} ({path})");
    }

    /// <summary>A launch file's bytes as its settings, or what is wrong with them.</summary>
    public static (LaunchConfig? Config, string? Problem) Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            // plain JSON: no comments, no trailing commas (the reader's defaults)
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            string? about = null;
            var debug = DebugSettings.Off;
            foreach (var member in Members(document.RootElement, "the file"))
            {
                switch (member.Name)
                {
                    case "about":
                        about = Text(member);
                        break;
                    case "debug":
                        debug = DebugOf(member);
                        break;
                    default:
                        throw Unknown(member, "");
                }
            }
            return about is { Length: > 0 }
                ? (new LaunchConfig(about, debug), null)
                : (null, "it has no \"about\" line that says what it is for");
        }
        catch (JsonException e)
        {
            return (null, $"it is not plain JSON ({e.Message})");
        }
        catch (FormatException e)
        {
            return (null, e.Message);
        }
        catch (InvalidOperationException)
        {
            // the reader raises on such a text only when it is read as text, and with this, not with a JsonException
            return (null, "a text or a name in it is no text (bytes that are not UTF-8, or half a character)");
        }
    }

    private static DebugSettings DebugOf(JsonProperty debug)
    {
        var d = DebugSettings.Off;
        foreach (var member in Members(debug.Value, "\"debug\""))
        {
            d = member.Name switch
            {
                "title" => d with { Title = Text(member) },
                "runnerArguments" => d with { RunnerArguments = Texts(member) },
                "windowAt" => d with { WindowAt = Place(member) },
                "windowPage" => d with { WindowPage = OneOf(member, Pages) },
                "quickPlay" => d with { QuickPlay = VisitOf(member) },
                "autopilot" => d with { Autopilot = YesNo(member) },
                "testLagMs" => d with { TestLagMs = Whole(member, 0, 10_000) },
                "game" => d with { Game = ObjectText(member) },
                "startLimitSeconds" => d with { StartLimitSeconds = Whole(member, 0, 3600) },
                "fault" => d with { Fault = OneOf(member, FaultRoutes) },
                "faultWithoutWindow" => d with { FaultWithoutWindow = YesNo(member) },
                "gpuLog" => d with { GpuLog = Text(member) },
                "gpuTurn" => d with { GpuTurn = YesNo(member) },
                "loadTrace" => d with { LoadTrace = YesNo(member) },
                "leaveAdapter" => d with { LeaveAdapter = YesNo(member) },
                _ => throw Unknown(member, "debug."),
            };
        }
        return d;
    }

    private static QuickPlayVisit VisitOf(JsonProperty visit)
    {
        var v = new QuickPlayVisit("");
        foreach (var member in Members(visit.Value, "\"quickPlay\""))
        {
            v = member.Name switch
            {
                "server" => v with { Server = Text(member) },
                "races" => v with { Races = Whole(member, 1, 1000) },
                "startWith" => v with { StartWith = Whole(member, 1, 30) },
                "voteCourse" => v with { VoteCourse = Whole(member, 0, 255) },
                "watch" => v with { Watch = YesNo(member) },
                "log" => v with { Log = Text(member) },
                "timeLimitSeconds" => v with { TimeLimitSeconds = Whole(member, 1, 86_400) },
                "custom" => v with { Custom = CustomOf(member) },
                "afterLeave" => v with { AfterLeave = Text(member) },
                _ => throw Unknown(member, "debug.quickPlay."),
            };
        }
        return v.Server.Length > 0
            ? v
            : throw new FormatException("\"quickPlay\" does not name its \"server\": a scripted visit goes where its file says, never to the settings' server");
    }

    /// <summary>An object's members, each name once.</summary>
    private static QuickPlayCustom CustomOf(JsonProperty custom)
    {
        var c = new QuickPlayCustom();
        foreach (var member in Members(custom.Value, "\"custom\""))
        {
            c = member.Name switch
            {
                "create" => c with { Create = Text(member) },
                "join" => c with { Join = Text(member) },
                "modeFile" => c with { ModeFile = Text(member) },
                "values" => c with { Values = [.. member.Value.EnumerateArray().Select(n => n.GetInt32())] },
                "teams" => c with { Teams = Whole(member, 0, 30) },
                "team" => c with { Team = Whole(member, 0, 29) },
                "rules" => c with { Rules = member.Value.EnumerateObject().ToDictionary(r => r.Name, r => r.Value.GetInt32(), StringComparer.Ordinal) },
                _ => throw Unknown(member, "debug.quickPlay.custom."),
            };
        }
        return c;
    }

    private static List<JsonProperty> Members(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{what} is not an object");
        }
        var members = element.EnumerateObject().ToList();
        return members.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() == members.Count
            ? members
            : throw new FormatException($"{what} names a setting twice");
    }

    private static FormatException Unknown(JsonProperty member, string under) =>
        new($"\"{under}{member.Name}\" is not a setting of a launch file");

    /// <summary>An object kept as its text, for the game's launch file. It is written here once as the launcher will
    /// write it at every game's start (<see cref="GameDebug.Part"/>), so that a text in it that is none (half a
    /// character) or a member named twice is refused with the file, and does not raise when a game is started.</summary>
    private static string ObjectText(JsonProperty member)
    {
        if (member.Value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"\"{member.Name}\" is not an object");
        }
        var text = member.Value.GetRawText();
        try
        {
            _ = new GameDebug(Autopilot: true, More: text).Part()!.ToJsonString();
        }
        catch (ArgumentException)
        {
            throw new FormatException($"\"{member.Name}\" names a setting twice");
        }
        return text;
    }

    private static string Text(JsonProperty member) => member.Value.ValueKind == JsonValueKind.String
        ? member.Value.GetString()!
        : throw new FormatException($"\"{member.Name}\" is not text");

    private static List<string> Texts(JsonProperty member) =>
        member.Value.ValueKind == JsonValueKind.Array && member.Value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
            ? [.. member.Value.EnumerateArray().Select(e => e.GetString()!)]
            : throw new FormatException($"\"{member.Name}\" is not a list of texts");

    private static bool YesNo(JsonProperty member) => member.Value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FormatException($"\"{member.Name}\" is not true or false"),
    };

    private static int Whole(JsonProperty member, int least, int most) => Whole(member.Value, member.Name, least, most);

    private static int Whole(JsonElement value, string name, int least, int most) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= least && n <= most
            ? n
            : throw new FormatException($"\"{name}\" is not a whole number from {least} to {most}");

    private static (int X, int Y) Place(JsonProperty member) =>
        member.Value.ValueKind == JsonValueKind.Array && member.Value.GetArrayLength() == 2
            ? (Whole(member.Value[0], member.Name, -100_000, 100_000), Whole(member.Value[1], member.Name, -100_000, 100_000))
            : throw new FormatException($"\"{member.Name}\" is not [X, Y]");

    private static string OneOf(JsonProperty member, IReadOnlyList<string> allowed) =>
        Text(member) is var text && allowed.Contains(text)
            ? text
            : throw new FormatException($"\"{member.Name}\" is not one of {string.Join(", ", allowed)}");
}
