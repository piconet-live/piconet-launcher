namespace Launcher.App;

// A race the player's game left while it ran, and going back into it.
public sealed partial class AppState
{
    /// <summary>The game's exit code; null when it cannot be read.</summary>
    private static int? ExitCodeOf(System.Diagnostics.Process game)
    {
        try
        {
            return game.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether a race's game that exited with <paramref name="exitCode"/> leaves its race kept for Rejoin
    /// (the race server is not told the race is over): the race ran in it (<paramref name="raceRan"/>), and it ended
    /// other than by its own stop. Kept: closed while its seat raced
    /// (<see cref="Launcher.Core.RecompLaunch.LeftRacingExitCode"/>), a crash, or its process ended from outside (any
    /// other code). Not kept: 0, its own stop (leaving the results, a desync; closed after finishing, a knock-out,
    /// in the results); and a game started again (<paramref name="startedAgain"/>) that found its race over
    /// (<see cref="Launcher.Core.RecompLaunch.RaceOverExitCode"/>), whose player is back in the lobby with no
    /// error.</summary>
    public static bool KeepsTheRace(int? exitCode, bool raceRan, bool startedAgain) =>
        raceRan && exitCode is int code && code != 0 && !FoundTheRaceOver(code, startedAgain);

    /// <summary>The game was started again into a race that is over (the race server said so): it left by itself.</summary>
    private static bool FoundTheRaceOver(int? exitCode, bool startedAgain) =>
        startedAgain && exitCode == Launcher.Core.RecompLaunch.RaceOverExitCode;

    /// <summary>The player starts their game again into the race it left (<see cref="Launcher.Core.QuickPlaySession.Rejoin"/>).</summary>
    public void Rejoin()
    {
        if (!Racing)
        {
            Session?.Rejoin();
        }
        Changed?.Invoke();
    }
}
