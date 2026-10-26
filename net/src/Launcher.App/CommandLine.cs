using Launcher.Core;

namespace Launcher.App;

/// <summary>What one start of the launcher does.</summary>
public enum StartMode
{
    /// <summary>The launcher's window.</summary>
    Window,

    /// <summary><c>--play-offline</c>: the game for offline play, without the window.</summary>
    PlayOffline,

    /// <summary><c>--play-controller-setup</c>: the game on its own controller options, without the window; it
    /// closes when the player leaves them (the Controller page's "Set up in the game").</summary>
    PlayControllerSetup,

    /// <summary><c>--play-match FILE</c>: the game on the race of a match file.</summary>
    PlayMatch,

    /// <summary><c>--play-replay FILE</c>: the game playing a replay back.</summary>
    PlayReplay,

    /// <summary><c>--list-replays</c>: the replay folder's replays as text.</summary>
    ListReplays,

    /// <summary><c>--prepare-disc IMAGE</c>: the game's files prepared from a disc image.</summary>
    PrepareDisc,

    /// <summary><c>--import-stat-pack CARDATA</c>: a stat pack file made from a changed car data file.</summary>
    ImportStatPack,

    /// <summary>A launch file's scripted Quick Play visit (<see cref="DebugSettings.QuickPlay"/>).</summary>
    QuickPlay,

    /// <summary>A launch file's error raised without the window (<see cref="DebugSettings.FaultWithoutWindow"/>).</summary>
    Fault,
}

/// <summary>
/// The launcher's command line, read here and nowhere else; the launcher reads no environment variable. Every option
/// has its row in the registry (<c>switches.tsv</c> beside this file), and one that is not listed here is refused with
/// its name: an option that has gone must not open the window in a script's hands.
/// <para>The modes, one a start; none: the window. <c>--play-offline</c>; <c>--play-controller-setup</c> (the game's
/// own controller options, closed when the player leaves them); <c>--play-match FILE</c> with
/// <c>--replay FILE</c> (where the race is recorded; without it, in the replay folder); <c>--play-replay FILE</c>
/// (as the race); <c>--list-replays</c>;
/// <c>--prepare-disc IMAGE</c> with <c>--bundle FOLDER</c> (another mod bundle than the launcher's);
/// <c>--import-stat-pack CARDATA</c> with <c>--pack-name TEXT</c> (the pack's name), <c>--pack-author TEXT</c> (who made
/// it, for its <c>author</c> line), <c>--main-module FILE</c> (a modified <c>enemy_line/line__.bin</c>: its allowed code
/// constants go into the pack too), <c>--disc IMAGE</c> (without it, the
/// settings' disc), <c>--out FILE</c> (without it, beside the car data file), <c>--overwrite</c> (a file already there
/// is replaced), <c>--any-disc</c> (the image is not held to the pinned disc) and <c>--report</c> (every difference
/// is listed with what the game's ranges say of it, and nothing is written).</para>
/// <para>With the window: <c>--dev-service</c>, the launcher's dev mode on the stand-in service in its own process
/// (<see cref="Launcher.Core.StandInService"/>; never a player's start).</para>
/// <para>For all of them: <c>--data FOLDER</c>, another data folder than the player's (or than a player copy's
/// own: <see cref="Install"/>), and <c>--config FILE</c>, a
/// launch file with the start's test settings (<see cref="LaunchConfig"/>). Everything that is for tests only is
/// named in that file, and without one every test setting is off.</para>
/// </summary>
public sealed record CommandLine
{
    /// <summary>The exit status of a start whose command line or launch file was refused (the system's "usage").</summary>
    public const int RefusedStatus = 64;

    public StartMode Mode { get; init; }

    /// <summary>The data folder given with <c>--data</c>; null: the player's, or a player copy's own.</summary>
    public string? DataFolder { get; init; }

    /// <summary>The launch file's test settings; without <c>--config</c>, all off.</summary>
    public DebugSettings Debug { get; init; } = DebugSettings.Off;

    /// <summary>The mode's file: the match file, the replay, the disc image or the changed car data file.</summary>
    public string? File { get; init; }

    /// <summary><c>--dev-service</c>: the window on the stand-in service (dev mode), not on a server.</summary>
    public bool DevService { get; init; }

    /// <summary><c>--replay</c>: where a match's race is recorded.</summary>
    public string? ReplayFile { get; init; }

    /// <summary><c>--bundle</c>: the mod bundle a disc is prepared for.</summary>
    public string? Bundle { get; init; }

    /// <summary><c>--disc</c>, <c>--pack-name</c>, <c>--pack-author</c>, <c>--out</c>: a stat pack's import: the disc
    /// image the changed car data is compared with, the pack's name, who made it, and the file it is written to.</summary>
    public string? Disc { get; init; }

    public string? Name { get; init; }

    public string? Author { get; init; }

    public string? Out { get; init; }

    /// <summary><c>--main-module</c>: a stat pack's import takes the allowed code constants of this modified main
    /// module too.</summary>
    public string? Module { get; init; }

    /// <summary><c>--any-disc</c>, <c>--overwrite</c>: a stat pack's import takes an image that is not the pinned
    /// disc; replaces a file that is at the output's place.</summary>
    public bool AnyDisc { get; init; }

    public bool Overwrite { get; init; }

    /// <summary><c>--report</c>: a stat pack's import lists what the changed file holds and writes nothing.</summary>
    public bool Report { get; init; }

    /// <summary>One of the options of a stat pack's import (<c>--import-stat-pack</c>), taken into the line.</summary>
    private static CommandLine ImportOption(CommandLine line, string name, string? value) => name switch
    {
        "--disc" => line with { Disc = value },
        "--pack-name" => line with { Name = value },
        "--pack-author" => line with { Author = value },
        "--out" => line with { Out = value },
        "--main-module" => line with { Module = value },
        "--any-disc" => line with { AnyDisc = true },
        "--report" => line with { Report = true },
        _ => line with { Overwrite = true },
    };

    /// <summary>The command line as a start, or the sentence it is refused with.</summary>
    public static (CommandLine? Line, string? Refusal) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var line = new CommandLine();
        var modes = new List<string>();
        string? config = null;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            string? value = null;
            if (name is "--data" or "--config" or "--play-match" or "--replay" or "--play-replay"
                or "--prepare-disc" or "--bundle" or "--import-stat-pack" or "--disc" or "--pack-name" or "--pack-author" or "--out" or "--main-module")
            {
                if (++i == args.Count)
                {
                    return (null, $"{name} needs a value.");
                }
                value = args[i];
            }
            switch (name)
            {
                case "--data":
                    line = line with { DataFolder = value };
                    break;
                case "--config":
                    config = value;
                    break;
                case "--play-offline":
                    line = line with { Mode = StartMode.PlayOffline };
                    break;
                case "--play-controller-setup":
                    line = line with { Mode = StartMode.PlayControllerSetup };
                    break;
                case "--play-match":
                    line = line with { Mode = StartMode.PlayMatch, File = value };
                    break;
                case "--play-replay":
                    line = line with { Mode = StartMode.PlayReplay, File = value };
                    break;
                case "--list-replays":
                    line = line with { Mode = StartMode.ListReplays };
                    break;
                case "--dev-service":
                    line = line with { DevService = true };
                    break;
                case "--prepare-disc":
                    line = line with { Mode = StartMode.PrepareDisc, File = value };
                    break;
                case "--replay":
                    line = line with { ReplayFile = value };
                    break;
                case "--bundle":
                    line = line with { Bundle = value };
                    break;
                case "--import-stat-pack":
                    line = line with { Mode = StartMode.ImportStatPack, File = value };
                    break;
                case "--disc" or "--pack-name" or "--pack-author" or "--out" or "--main-module" or "--any-disc" or "--overwrite" or "--report":
                    line = ImportOption(line, name, value);
                    break;
                default:
                    return (null, $"{name} is not an option of the launcher. Test settings are named in a launch file (--config FILE).");
            }
            if (name is "--play-offline" or "--play-controller-setup" or "--play-match" or "--play-replay" or "--list-replays"
                or "--prepare-disc" or "--import-stat-pack")
            {
                modes.Add(name);
            }
        }
        if (config is not null)
        {
            var (file, problem) = LaunchConfig.Read(config);
            if (file is null)
            {
                return (null, problem);
            }
            line = line with { Debug = file.Debug };
            if (file.Debug.QuickPlay is not null)
            {
                modes.Add("the launch file's quickPlay");
                line = line with { Mode = StartMode.QuickPlay };
            }
            if (file.Debug is { Fault: not null, FaultWithoutWindow: true })
            {
                modes.Add("the launch file's faultWithoutWindow");
                line = line with { Mode = StartMode.Fault };
            }
        }
        return Finished(line, modes);
    }

    /// <summary>The line read whole: one mode at most, and the dev mode only with the window.</summary>
    private static (CommandLine? Line, string? Refusal) Finished(CommandLine line, List<string> modes) =>
        modes.Count > 1 ? (null, $"One mode a start: {string.Join(" and ", modes)} were both asked for.")
        : line.DevService && line.Mode != StartMode.Window ? (null, "--dev-service is for the window: it starts no game and no mode without it.")
        : (line, null);
}
