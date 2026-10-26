namespace Launcher.App;

// The player's name, changed.
public sealed partial class AppState
{
    /// <summary>The racer's name is this from now on: saved, and the session sees to it that the server the launcher
    /// is connected to has it (<see cref="Launcher.Core.QuickPlaySession.NameChanged"/>), so that the lobby, the chat
    /// and the next race show it at once. (Sent with the hello only, everybody else would see the old name until the
    /// player left Quick Play and joined again.)</summary>
    public void Rename(string name)
    {
        Update(s => s with { Racer = s.Racer with { Name = name } });
        Session?.NameChanged();
    }
}
