using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A practice's match file (Play's Practice; the game's <c>"match.practice"</c>, protocol/formats.md section 2):
/// one seat, the player's, on the set-up's course, machine (a custom machine as the seat's <c>custom</c>), setting
/// and rules, under Practice's game mode on the set-up's parameters (its laps among them), with no relay and no CPU
/// racer. The
/// game races it alone in its own Practice mode, with the rewind, and records the run kept.
/// </summary>
public static class PracticeMatch
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes the match file (<see cref="Build"/>) at <paramref name="path"/>.</summary>
    public static void Write(string path, PracticeSetup setup, string name, MachineLook? look, DateTimeOffset now,
                             StatPack? pack = null, ModePackage? mode = null, BoostTrail? trail = null) =>
        File.WriteAllText(path, Build(setup, name, look, now, pack, mode, trail));

    /// <summary>The match file's text.</summary>
    /// <param name="name">The racer's name as the game shows it.</param>
    /// <param name="look">The machine's look from the garage: a stock colour or a colour; null: its own.</param>
    /// <param name="pack">The stat pack the set-up names (<see cref="PracticePacks"/>), carried whole as a match
    /// start carries one (<c>setup.stats</c>); null: the disc's own stats.</param>
    /// <param name="mode">Practice's game mode (<see cref="PracticeMode"/>), on the set-up's parameters
    /// (<see cref="PracticeMode.Values"/>), the rules it states its own (<see cref="PracticeMode.Over"/>), and the laps
    /// its; null: none, the game's own words for a practice and its 3 laps (old launch files and tests).</param>
    public static string Build(PracticeSetup setup, string name, MachineLook? look, DateTimeOffset now, StatPack? pack = null, ModePackage? mode = null, BoostTrail? trail = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(name);
        var s = mode is null ? setup.Kept() : PracticeMode.Over(mode, setup.Kept());
        var values = mode is null ? null : PracticeMode.Values(mode, s);
        var seat = new JsonObject
        {
            ["seat"] = 0,
            ["machine"] = s.Machine,
            ["setting"] = s.MachineSetting,
            ["playerId"] = "practice",
            ["name"] = name,
        };
        if (s.Custom is { } custom)
        {
            // raced in place of the stock machine, as an online seat races one (protocol/formats.md section 2)
            seat["custom"] = JsonSerializer.SerializeToNode(custom, ControlProtocol.Json);
        }
        if (trail is { IsStock: false })
        {
            seat["trail"] = JsonSerializer.SerializeToNode(trail, ControlProtocol.Json);
        }
        if (look is { StockColor: { } stock })
        {
            seat["stockColor"] = stock;
        }
        else if (look is { Color: { } c })
        {
            seat["color"] = new JsonObject { ["r"] = c.R, ["g"] = c.G, ["b"] = c.B };
        }
        var set = new JsonObject
        {
            ["course"] = s.Course,
            ["laps"] = mode is null ? ModeTerms.GameLaps : PracticeMode.Laps(mode, values!, s.Course),
            ["seats"] = new JsonArray(seat),
        };
        // the race's rules as a match start carries them (Online.Protocol RaceRules; the game's RulesIntoRace,
        // port/core/src/files_match.cpp): boosting on the first lap, the mines' return (Restore is the mode's)
        set["rules"] = JsonSerializer.SerializeToNode(s.Rules, ControlProtocol.Json);
        if (mode is not null)
        {
            set["mode"] = JsonSerializer.SerializeToNode(mode.Start(values), ControlProtocol.Json);
        }
        if (pack is not null)
        {
            var wire = pack.ToWire();
            set["stats"] = new JsonObject { ["name"] = wire.Name, ["fingerprint"] = wire.Fingerprint, ["values"] = wire.Values };
        }
        var match = new JsonObject
        {
            ["matchId"] = "practice-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            ["setup"] = set,
        };
        return new JsonObject { ["match"] = match, ["practice"] = true }.ToJsonString(Indented);
    }
}
