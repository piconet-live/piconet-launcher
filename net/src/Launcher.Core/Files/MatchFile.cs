using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>This player's side of a race, added to the coordinator's match start.</summary>
/// <param name="RelayHost">The relay's host when the match leaves it empty (the coordinator's own host).</param>
/// <param name="Delay">Input delay in frames: this player's setting, 0, 1 or 2 and never more
/// (<see cref="LauncherSettings.RaceInputDelay"/>).</param>
/// <param name="Overclock">The emulated CPU clock (rollback re-simulation needs headroom).</param>
public sealed record RaceOptions(string RelayHost, OtherSounds OtherSounds, int Delay = 2, double Overclock = 6.0)
{
    /// <summary>The match start is a spectator's (<see cref="WatchStart"/>): the game watches and sends no input.</summary>
    public bool Spectate { get; init; }
}

/// <summary>
/// The match file: this player's race as the game is given it, the <c>"match"</c> part of the game's launch file
/// (<see cref="GameLaunchFile.Race"/>; protocol/formats.md section 2): "match", the coordinator's MatchStart as
/// it came; "relayHost", "delay", "overclock", "otherSounds" (full, quieter, off). No test setting is in it: the
/// game refuses a match that names one (they are the launch file's "debug" part, <see cref="GameDebug"/>).
/// </summary>
public static class MatchFile
{
    public static string Build(MatchStart start, RaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(options);
        var file = new JsonObject
        {
            ["match"] = JsonNode.Parse(ControlProtocol.Encode(start)),
            ["relayHost"] = options.RelayHost,
            ["delay"] = options.Delay,
            ["overclock"] = options.Overclock,
            ["otherSounds"] = options.OtherSounds.ToString().ToLower(CultureInfo.InvariantCulture),
        };
        if (options.Spectate)
        {
            file["spectate"] = true;
        }
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void Write(string path, MatchStart start, RaceOptions options) =>
        File.WriteAllText(path, Build(start, options));
}
