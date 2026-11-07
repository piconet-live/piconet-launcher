using Online.Protocol;

namespace Launcher.Core;

/// <summary>Play's Practice as a game mode: the protocol's <c>practice.fzmode</c>, which the build puts in the quickplay
/// folder beside the program, compiled by the one compiler. Its parameters are the page's Practice rows, as a custom
/// lobby's mode's are its host's (a parameter added to the file is a row more); its words, rewind, pause, save and
/// the rest, are its own. A rule's word it states (the laps, the mines, the stats...) is the mode's, as in a lobby:
/// the page's row of that rule then goes (<see cref="States"/>), and the mode's value is raced (<see cref="Over"/>).</summary>
public static class PracticeMode
{
    /// <summary>The mode file's name.</summary>
    public const string FileName = "practice.fzmode";

    /// <summary>The game's steps a second: a time word's unit.</summary>
    private const int Steps = 60;

    /// <summary>The mode compiled from the folder's file; null, with <paramref name="problem"/>, when the file is
    /// missing or does not compile.</summary>
    public static ModePackage? Read(string folder, out string? problem)
    {
        var path = Path.Combine(folder, FileName);
        problem = null;
        if (!File.Exists(path))
        {
            problem = "Practice mode file not found";
            return null;
        }
        var compiled = ModeCompiler.Compile(File.ReadAllText(path));
        if (!compiled.Succeeded)
        {
            problem = "Practice mode file has an error";
            return null;
        }
        return compiled.Mode!;
    }

    /// <summary>The parameters' values for a set-up, in the mode's order: each the set-up's by the parameter's name
    /// when the parameter takes it, else its default. A settings file written before the rows were the mode's names
    /// its laps and Restore by the rules: the parameter that sets the laps, and the one labelled Restore, take them.</summary>
    public static IReadOnlyList<int> Values(ModePackage mode, PracticeSetup? setup)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return [.. mode.Params.Select(p => Saved(p, setup) is { } v && p.Fits(v) ? v : p.Default)];
    }

    private static int? Saved(ModeParam p, PracticeSetup? setup) =>
        setup?.Params is { } saved && saved.TryGetValue(p.Name, out var value) ? value
        : p.Rule == HostRules.Laps.Name ? setup?.OldLaps
        : p.Label == HostRules.Restore.Label && setup?.OldRestore is { } on ? on ? 1 : 0
        : null;

    /// <summary>Whether the mode writes the rule's setup word (its own or the coordinator's): the rule is the mode's,
    /// and the page shows no row of it.</summary>
    public static bool States(ModePackage mode, HostRule rule)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(rule);
        return rule.SetupWord is { } word && mode.Setups.Select(s => s.Id).Concat(mode.LobbyWords.Select(w => w.Id))
            .Any(id => ModeWords.Setups.First(w => w.Id == id).Name == word);
    }

    /// <summary>The set-up as it is raced under the mode: a rule the mode states is the mode's on the parameters'
    /// values, as the coordinator lays a mode over a lobby (the boost from the start, the mines, the stats; the
    /// player's machine setting brought into the mode's range).</summary>
    public static PracticeSetup Over(ModePackage mode, PracticeSetup setup)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(setup);
        var terms = ModeTerms.Of(mode, Values(mode, setup));
        bool Owns(HostRule rule) => States(mode, rule);
        return setup with
        {
            BoostFromStart = Owns(HostRules.BoostStart) ? terms.BoostStart == true : setup.BoostFromStart,
            MineRegenMs = Owns(HostRules.MinesRespawn) ? terms.MinesRespawn ?? 0 : setup.MineRegenMs,
            MachineSetting = Owns(HostRules.MachineSetting) && terms.Setting is { } range ? range.Clamp(setup.MachineSetting) : setup.MachineSetting,
            StatPack = Owns(HostRules.StatPack) ? terms.Pack is { Length: > 0 } pack ? pack : null : setup.StatPack,
        };
    }

    /// <summary>The laps raced on the course under the parameters' values (0: unlimited); the game's own where the mode
    /// states none.</summary>
    public static int Laps(ModePackage mode, IReadOnlyList<int> values, int course)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return ModeTerms.Of(mode, values).Laps(course) ?? ModeTerms.GameLaps;
    }

    /// <summary>How far back the rewind reaches, in seconds (its word on the parameters' values); null: the mode has none.</summary>
    public static int? RewindSeconds(ModePackage mode, IReadOnlyList<int> values) =>
        Word(mode, values, ModeSetup.Rewind) is { } steps ? steps / Steps : null;

    /// <summary>A setup word's first value on the parameters' values; null: the mode does not write it.</summary>
    private static int? Word(ModePackage mode, IReadOnlyList<int> values, ModeSetup id)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(values);
        return mode.Setups.FirstOrDefault(s => s.Id == id)?.Arguments is [var first, ..]
            ? first.FromParam ? values[first.Value] : first.Value
            : null;
    }
}
