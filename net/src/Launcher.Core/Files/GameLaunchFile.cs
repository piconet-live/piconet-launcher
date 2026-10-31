using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Core;

/// <summary>
/// The test settings the launcher hands on to the game it starts: the <c>"debug"</c> part of the game's launch file
/// (<see cref="GameLaunchFile"/>). They come from the launcher's own launch file (<see cref="DebugSettings"/>:
/// <c>"autopilot"</c>, <c>"testLagMs"</c> and <c>"game"</c>) and from nowhere else: a player's launcher has none, and
/// the game it starts then has no <c>"debug"</c> part, which leaves every test setting of the game off.
/// </summary>
/// <param name="Autopilot">The game's AI drives this player's seat (the game's <c>"autopilot"</c>).</param>
/// <param name="TestLagMs">A round trip added to every packet between the game and the relay (<c>"testLagMs"</c>).</param>
/// <param name="More">More members of the game's <c>"debug"</c> part, as the text of a JSON object
/// (<c>{"rollbackLog": "rb.csv"}</c>); the game's reader is their judge and refuses one it does not know.</param>
public sealed record GameDebug(bool Autopilot = false, int TestLagMs = 0, string? More = null)
{
    /// <summary>Every test setting off: a player's game.</summary>
    public static GameDebug Off { get; } = new();

    /// <summary>What a launcher's launch file hands on to its games.</summary>
    public static GameDebug From(DebugSettings debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        return new GameDebug(debug.Autopilot, debug.TestLagMs, debug.Game);
    }

    /// <summary>The game's <c>"debug"</c> part; null when nothing is on (the file then has none).</summary>
    public JsonObject? Part() => Launch()?.ToJson();

    /// <summary>The part as the launch file's writer takes it (protocol/launch.toml); null when nothing is on.</summary>
    internal LaunchDebug? Launch()
    {
        var part = new LaunchDebug(More is null ? null : JsonNode.Parse(More) as JsonObject, Autopilot, TestLagMs);
        return part.ToJson().Count == 0 ? null : part;
    }
}

/// <summary>
/// The launcher's button on the Windows taskbar, which the window of a game it starts joins as a second window of
/// one program (the launch file's <c>"taskbar"</c>). Only a launcher with a window of its own has one to give.
/// </summary>
/// <param name="Id">The launcher's application id, as it gave it to Windows for itself.</param>
/// <param name="Program">The launcher's program file: what a pinned button starts, and whose icon it shows.</param>
/// <param name="Name">What a pinned button is called.</param>
public sealed record TaskbarGroup(string Id, string Program, string Name)
{
    /// <summary>The most bytes of an id, a program file's path and a name the game's reader takes.</summary>
    public const int MostIdBytes = LaunchFormat.TaskbarIdMostBytes, MostProgramBytes = LaunchFormat.TaskbarProgramMostBytes,
        MostNameBytes = LaunchFormat.TaskbarNameMostBytes;

    /// <summary>Whether the game's reader takes it (port/core/src/files_launch.cpp): an id of letters, digits,
    /// '.', '-' and '_'; a program file of this computer by its whole path (a drive letter: no network path),
    /// ending in .exe; a path and a name of one line each, without a quotation mark. The game also refuses a
    /// program file that is not there.</summary>
    public bool Taken =>
        Id.Length is >= 1 and <= MostIdBytes && Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') &&
        OneLine(Program, MostProgramBytes) && OneLine(Name, MostNameBytes) &&
        Program.Length >= 8 && char.IsAsciiLetter(Program[0]) && Program[1] == ':' && Program[2] == '\\' &&
        Program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool OneLine(string text, int most) =>
        text.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(text) <= most && !text.Any(c => c < ' ' || c == '\u007F' || c == '"');
}

/// <summary>
/// What the launcher gives the window of a game it starts: its title (the taskbar's; a launch file's own names a
/// test), and for the strip at the window's top, which the game draws in the launcher's look, the name shown there
/// (the launcher's: the game has no name of its own compiled in) and whether the frame rate is shown (the player's
/// setting, <see cref="LauncherSettings.ShowFrameRate"/>).
/// </summary>
/// <param name="Title">The window's title, the runner's <c>--title</c>.</param>
/// <param name="Name">The name in the strip (the launch file's <c>"windowName"</c>); null: none.</param>
/// <param name="ShowFrameRate">The strip shows the frame rate (<c>"showFrameRate"</c>).</param>
/// <param name="NamePicture">The name as the launcher's own title bar draws it, in the game's lettering from the
/// player's disc (<c>"windowNamePicture"</c>: the game draws it in place of the name's text); null: the text.</param>
/// <param name="Taskbar">The launcher's taskbar button, which the game's window joins (<c>"taskbar"</c>); null: the
/// game's window has a button of its own.</param>
/// <param name="Present">When the game hands a picture to the screen, where it is one of the two ways kept for
/// comparison (<see cref="LauncherSettings.GamePresent"/>): "field" or "copy" (<c>"present"</c>). Null: the game's
/// own way, a console's moment, and the member is not written.</param>
/// <param name="VerticalSync">The game's pictures wait for the display's refresh (the player's setting,
/// <see cref="LauncherSettings.VerticalSync"/>; <c>"verticalSync"</c>, written only when it is on).</param>
/// <param name="FullScreen">What the game's full screen is (the player's setting,
/// <see cref="LauncherSettings.GameFullScreen"/>; <c>"fullScreen"</c>: "borderless" or "exclusive"). Null, or
/// neither: not written, and the game's full screen is the borderless window.</param>
/// <param name="Board">The launcher's status board for this game (<see cref="GameBoard.Path"/>; <c>"board"</c>): the
/// channel by which the launcher sees the game run and stand still, gets the race, and asks it to stop. Null: none.</param>
/// <param name="RefreshRate">Pictures a second where frames are drawn between two steps (the player's setting,
/// <see cref="LauncherSettings.GameRefreshRate"/>; <c>"refreshRate"</c>: "120", "240" or "display"). Null, or none
/// of them: not written, and the game draws its own picture a step.</param>
/// <param name="Rumble">The controllers may rumble (the player's setting, <see cref="LauncherSettings.Rumble"/>;
/// <c>"rumble": false</c>, written only when off: a runner from before the key refuses it).</param>
/// <param name="Camera">The chase camera view a race starts in, 1 to 3 or 4 first person (the player's last,
/// <see cref="LauncherSettings.StartCamera"/>; <c>match.camera</c>, written only in a race's file and only with a
/// view: a runner from before the key refuses it). 0: the game's own.</param>
public sealed record GameWindow(string Title, string? Name = null, bool ShowFrameRate = false, Picture? NamePicture = null,
    TaskbarGroup? Taskbar = null, string? Present = null, bool VerticalSync = false, string? FullScreen = null,
    string? Board = null, string? RefreshRate = null, bool Rumble = true, int Camera = 0)
{
    /// <summary>The largest name picture the game's reader takes.</summary>
    public const int MostPictureWidth = LaunchFormat.WindowNamePictureWidthHigh, MostPictureHeight = LaunchFormat.WindowNamePictureHeightHigh;

    /// <summary>The launch file with the window's members.</summary>
    internal LaunchFile Into(LaunchFile file) => file with
    {
        WindowName = string.IsNullOrWhiteSpace(Name) ? null : Name,
        // a picture the game would refuse (too large, or not its own size) is left out: the name's text is drawn
        WindowNamePicture = NamePicture is { Width: >= 1 and <= MostPictureWidth, Height: >= 1 and <= MostPictureHeight } picture &&
                            picture.Rgba.Length == picture.Width * picture.Height * 4
            ? new LaunchNamePicture(picture.Width, picture.Height, Convert.ToBase64String(picture.Rgba))
            : null,
        ShowFrameRate = ShowFrameRate,
        // written only when off: the game's default is on, and a runner from before the key (0.3.0) refuses a file with it
        Rumble = Rumble,
        // the game's own ways are left out: the console's moment, 60 a second
        Present = Present is LauncherSettings.PresentAsBefore or LauncherSettings.PresentAtTheCopy ? Present : null,
        VerticalSync = VerticalSync,
        FullScreen = FullScreen is LauncherSettings.FullScreenBorderless or LauncherSettings.FullScreenExclusive ? FullScreen : null,
        RefreshRate = RefreshRate is LauncherSettings.Refresh120 or LauncherSettings.Refresh240 or LauncherSettings.RefreshAsTheDisplay
            ? RefreshRate
            : null,
        // a group the game would refuse is left out, as the picture is: the game's window then has its own button
        Taskbar = Taskbar is { Taken: true } group ? new LaunchTaskbar(group.Id, group.Program, group.Name) : null,
        Board = Board,
        // a race's only (a playback starts in the game's own view), and only a view the game takes
        Match = file.Match is { } match && Camera is >= LaunchFormat.MatchCameraLow and <= LaunchFormat.MatchCameraHigh
            ? match with { Camera = Camera }
            : file.Match,
    };
}

/// <summary>
/// The game's launch file (protocol/formats.md section 2): every setting of one start of the game, which the
/// runner is given as <c>--config FILE</c> and takes from nowhere else. The launcher writes one for each game it
/// starts, into the game's user folder: a plain start (offline play), a race (its <c>"match"</c> part is the match
/// file, <see cref="MatchFile"/>, with where the race is recorded), or a replay's
/// playback. The game refuses a file with a member it does not know. Its members are described once, in
/// protocol/launch.toml, which writes the records this fills (<see cref="LaunchFile"/>, generated) and the game's reader.
/// </summary>
public static class GameLaunchFile
{
    private const int MaxMatchBytes = LaunchFormat.MaxBytes;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Offline play: a plain start.</summary>
    /// <param name="bundle">The mod bundle, when it is of the native form (its patched code runs in every start).</param>
    /// <param name="window">What the game's window shows of the launcher's; null: the file says nothing of it.</param>
    /// <param name="controllerSetup">The start opens the game's own controller options once its title stands, and
    /// the game closes when the player leaves them (the game's <c>"controllerSetup"</c>: a player's key, no test
    /// setting).</param>
    public static string Offline(string? bundle, GameDebug? debug = null, GameWindow? window = null, bool controllerSetup = false)
    {
        var about = controllerSetup ? "Piconet Launcher: the game's controller options" : "Piconet Launcher: offline play";
        return Text(new LaunchFile(about, bundle, ControllerSetup: controllerSetup), debug, window);
    }

    /// <summary>A race from a match file's text (<see cref="MatchFile.Build"/>, or a file somebody wrote).</summary>
    /// <param name="record">Where the race is recorded; null: not recorded.</param>
    /// <exception cref="FormatException">The match file is not a JSON object.</exception>
    public static string Race(string bundle, string matchFile, WidescreenMode widescreen = WidescreenMode.Game, string? record = null,
        GameDebug? debug = null, GameWindow? window = null, bool rejoin = false)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(matchFile);
        try
        {
            var match = JsonNode.Parse(matchFile) as JsonObject ?? throw new FormatException("The match file is not a JSON object.");
            var name = (match["match"] as JsonObject)?["matchId"] is JsonValue id && id.TryGetValue<string>(out var text) ? text : "?";
            // rejoin: started again into its running race
            return Text(Screen(new LaunchFile($"Piconet Launcher: a race (match {name})", bundle,
                Match: new LaunchMatch(match, record, rejoin)), widescreen), debug, window);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // the JSON reader takes a text with half a character in it and a member named twice, and raises only
            // when the text is read or the object written (InvalidOperationException, ArgumentException)
            throw new FormatException($"The match file is not JSON the game can be given ({e.Message}).", e);
        }
    }

    /// <summary>A match file's text, for <see cref="Race"/>. The game reads no launch file over 1 MiB (a match of 30
    /// seats is some 8 KiB), so a longer match file is not read here either.</summary>
    /// <exception cref="FormatException">The file is longer than that.</exception>
    public static string MatchFileText(string path)
    {
        if (new FileInfo(path).Length > MaxMatchBytes)
        {
            throw new FormatException("The match file is longer than 1 MiB and was not read.");
        }
        return File.ReadAllText(path);
    }

    /// <summary>A replay's playback, as the race itself.</summary>
    public static string Playback(string bundle, string replayFile, OtherSounds sounds = OtherSounds.Quieter,
        WidescreenMode widescreen = WidescreenMode.Game, GameDebug? debug = null, GameWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(replayFile);
        return Text(Screen(new LaunchFile("Piconet Launcher: a replay's playback", bundle,
            Replay: new LaunchReplay(replayFile, sounds.ToString().ToLower(CultureInfo.InvariantCulture))), widescreen), debug, window);
    }

    /// <summary>The picture's keys: <c>"widescreen"</c>, the game's own 16:9 mode, and with true widescreen
    /// <c>"trueWidescreen"</c> (protocol/formats.md section 2).</summary>
    private static LaunchFile Screen(LaunchFile file, WidescreenMode widescreen) =>
        file with { Widescreen = widescreen != WidescreenMode.Off, TrueWidescreen = widescreen == WidescreenMode.TrueWide };

    /// <summary>Where a game's launch file is written: in its user folder, the latest start's over the one before.</summary>
    public static string PathFor(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.UserFolder, "launch.json");
    }

    /// <summary>Writes a game's launch file; its path, for the runner's <c>--config</c>.</summary>
    public static string Write(RecompPaths paths, string text)
    {
        var path = PathFor(paths);
        Directory.CreateDirectory(paths.UserFolder);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The file's text, with the window's members and the test settings. Its <c>"lanes"</c> is 0 in
    /// every start (launch.toml): the multicore physics is not in the game, and a
    /// game refuses any other count.</summary>
    private static string Text(LaunchFile file, GameDebug? debug, GameWindow? window)
    {
        file = (window?.Into(file) ?? file) with { Debug = debug?.Launch() };
        return file.ToJson().ToJsonString(Indented);
    }
}
