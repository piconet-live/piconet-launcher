using Launcher.Core;

namespace Launcher.App;

public sealed partial class AppState
{
    /// <summary>The stat packs a practice can run on: the launcher's own pack files (<see cref="PracticePacks"/>),
    /// read once.</summary>
    public IReadOnlyList<Online.Protocol.StatPack> PracticePackList => _practicePacks ??= PracticePacks.Read(PracticePacks.Folder);

    private IReadOnlyList<Online.Protocol.StatPack>? _practicePacks;

    /// <summary>Starts the game on a practice (Play's Practice, START PRACTICE): its match file
    /// (<see cref="PracticeMatch"/>) in the data folder, raced as a match file is (<see cref="PlayMatch"/>), recorded
    /// in the replay folder only while "Record practice" is on (<see cref="LauncherSettings.RecordPractice"/>),
    /// remembered in the settings; a problem string otherwise.</summary>
    public string? StartPractice(PracticeSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (Racing)
        {
            return GameOpenText;
        }
        var racer = Settings.Racer;
        var file = Path.Combine(dataFolder, "practice-match.json");
        var pack = setup.StatPack is { } named ? PracticePackList.FirstOrDefault(p => p.Name == named) : null;
        if (setup.StatPack is not null && pack is null)
        {
            return "Stat pack not found";
        }
        var mode = PracticeMode.Start(Path.Combine(AppContext.BaseDirectory, "quickplay"), setup.Restore, out var problem);
        if (mode is null)
        {
            return problem;
        }
        // the page opens on this practice next time (its course and rules)
        // (the machine is the Garage's at each start: the custom machine is not kept with the set-up)
        var kept = setup.Kept() with { Custom = null };
        if (Settings.Practice != kept)
        {
            Update(s => s with { Practice = kept });
        }
        PracticeMatch.Write(file, setup, racer.Name, racer.LookOf(setup.Machine), DateTimeOffset.Now, pack, mode);
        return PlayMatch(file, run: GameRun.Practice, course: setup.Kept().Course, record: Settings.RecordPractice);
    }
}
