using Online.Protocol;

namespace Launcher.Core;

/// <summary>Play's Practice as a game mode: the protocol's <c>practice.fzmode</c>, which the build puts in the quickplay
/// folder beside the program, compiled by the one compiler. Its rewind, pause, save and countdown are its words; the
/// page's Restore and Laps rows are its parameters. Play's Time Trial is another mode file (<c>time_trial.fzmode</c>)
/// started the same way, its Laps row its one parameter.</summary>
public static class PracticeMode
{
    /// <summary>The mode file's name.</summary>
    public const string FileName = "practice.fzmode";

    /// <summary>The time trial's mode file.</summary>
    public const string TimeTrialFile = "time_trial.fzmode";

    /// <summary>The time trial's fewest and most laps: its <c>lap_count</c> parameter's range.</summary>
    public const int TimeTrialFewestLaps = 1, TimeTrialMostLaps = 9;

    private const string RestoreParam = "restore_on";

    private const string LapsParam = "lap_count";

    /// <summary>The match's <c>setup.mode</c> for a practice; null, with <paramref name="problem"/>, when the file is
    /// missing or does not compile.</summary>
    public static ModeStart? Start(string folder, bool restore, int laps, out string? problem) =>
        Start(folder, FileName, restore, laps, out problem);

    /// <summary>The match's <c>setup.mode</c> for the mode file <paramref name="file"/> raced alone; null, with
    /// <paramref name="problem"/>, when the file is missing or does not compile.</summary>
    public static ModeStart? Start(string folder, string file, bool restore, int laps, out string? problem)
    {
        var path = Path.Combine(folder, file);
        problem = null;
        if (!File.Exists(path))
        {
            problem = "Mode file not found";
            return null;
        }
        var compiled = ModeCompiler.Compile(File.ReadAllText(path));
        if (!compiled.Succeeded)
        {
            problem = "Mode file has an error";
            return null;
        }
        var mode = compiled.Mode!;
        return mode.Start([.. mode.Params.Select(p => p.Name switch
        {
            RestoreParam => restore ? 1 : 0,
            LapsParam => laps,
            _ => p.Default,
        })]);
    }
}
