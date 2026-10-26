using System.Diagnostics;

namespace Launcher.App;

/// <summary>How the launcher closes to start again: the window's own close (<see cref="Close"/>, set by the
/// application: the same as the player's, so the game's handles, the session and Discord's status are let go as
/// ever), after an update was handed to Velopack's updater or with a new start of this program.</summary>
public static class Relaunch
{
    /// <summary>Closes the launcher as the player's close does; null: no window to close (a test, a mode without one).</summary>
    public static Action? Close { get; set; }

    /// <summary>Starts this program again and closes this one (after a data move: the settings are read at a start).
    /// False, and nothing done: there is no window to close, or the program could not be started.</summary>
    public static bool AgainNow(Action<string>? note = null)
    {
        if (Close is not { } close || Environment.ProcessPath is not { } program ||
            string.Equals(Path.GetFileNameWithoutExtension(program), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            // with this start's own command line (a data folder named with --data stays the one)
            var start = new ProcessStartInfo(program) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            foreach (var word in Environment.GetCommandLineArgs().Skip(1))
            {
                start.ArgumentList.Add(word);
            }
            using var started = Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            note?.Invoke($"the launcher could not start again ({e.GetType().Name}: {e.Message})");
            return false;
        }
        close();
        return true;
    }

    /// <summary>The ready update put in: Velopack's updater waits for this launcher to close, puts it in and starts the
    /// new version (<see cref="Updates.RestartToUpdate"/>). False, and nothing done: no update ready, a game runs, or
    /// no window to close.</summary>
    public static bool ToUpdate(Updates updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (Close is not { } close || !updates.RestartToUpdate())
        {
            return false;
        }
        close();
        return true;
    }
}
