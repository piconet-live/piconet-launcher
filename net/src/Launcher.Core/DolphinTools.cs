using System.Diagnostics;

namespace Launcher.Core;

/// <summary>
/// What is left of the retired Dolphin fork's settings area: a folder opened in the system's file browser (the
/// replay folder, the log's). Controllers are set on the Controller page (<see cref="ControllerHelper"/>), for the
/// recompiled game.
/// </summary>
public static class DolphinTools
{
    /// <summary>Opens a folder in the system's file browser.</summary>
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }

    /// <summary><see cref="OpenFolder"/> for a button: false when the folder cannot be made or shown (a path that
    /// is none, a place that is not the player's to write, no file browser), and the launcher goes on.</summary>
    public static bool TryOpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }
        try
        {
            OpenFolder(folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or InvalidOperationException or System.ComponentModel.Win32Exception
                                      or System.Security.SecurityException)
        {
            return false;
        }
    }
}
