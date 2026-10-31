using Online.Protocol;

namespace Launcher.Core;

/// <summary>Play's Practice as a game mode: the protocol's <c>practice.fzmode</c>, which the build puts in the quickplay
/// folder beside the program, compiled by the one compiler. Its rewind, pause, save and countdown are its words; the
/// page's Restore and Laps rows are its parameters.</summary>
public static class PracticeMode
{
    /// <summary>The mode file's name.</summary>
    public const string FileName = "practice.fzmode";

    private const string RestoreParam = "restore_on";

    private const string LapsParam = "lap_count";

    /// <summary>The match's <c>setup.mode</c> for a practice; null, with <paramref name="problem"/>, when the file is
    /// missing or does not compile.</summary>
    public static ModeStart? Start(string folder, bool restore, int laps, out string? problem)
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
        var mode = compiled.Mode!;
        return mode.Start([.. mode.Params.Select(p => p.Name switch
        {
            RestoreParam => restore ? 1 : 0,
            LapsParam => laps,
            _ => p.Default,
        })]);
    }
}
