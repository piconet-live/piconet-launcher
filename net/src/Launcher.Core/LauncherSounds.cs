namespace Launcher.Core;

/// <summary>Every sound the launcher makes: the lobby's moments (<see cref="LobbyMoment"/>), the draw's blip on every
/// hop and its chime on the course drawn, and the navigation's cursor and back (Settings, "Navigation sounds").</summary>
public enum LauncherSound
{
    CourseVote,
    DrawHop,
    CourseDrawn,
    Machines,
    RaceStart,
    UrgentNotice,
    Cursor,
    Back,

    /// <summary>A machine confirmed: USE THIS MACHINE, Save racing the build, READY in a lobby (the
    /// same whoosh). The game's confirm, the machine stage's own sound.</summary>
    Confirm,
}

/// <summary>Which sound the launcher plays, and whether: the draw's own, made in code (<see cref="DrawSounds"/>,
/// with or without a disc); the game's when the disc gave it (<see cref="GameSounds"/>: the vote, the machines, the
/// start, the navigation); else the launcher's own tone for a lobby moment or an urgent notice
/// (<see cref="LobbyTones"/>), else none (the navigation has no tone of its own).</summary>
public static class LauncherSounds
{
    /// <summary>A lobby moment's sound.</summary>
    public static LauncherSound Of(LobbyMoment moment) => moment switch
    {
        LobbyMoment.CourseVote => LauncherSound.CourseVote,
        LobbyMoment.Machines => LauncherSound.Machines,
        LobbyMoment.RaceStart => LauncherSound.RaceStart,
        _ => LauncherSound.UrgentNotice,
    };

    /// <summary>The lobby moment a sound stands for, whose tone is its fallback; null for the draw and the navigation.</summary>
    public static LobbyMoment? Moment(LauncherSound sound) => sound switch
    {
        LauncherSound.CourseVote => LobbyMoment.CourseVote,
        LauncherSound.Machines or LauncherSound.Confirm => LobbyMoment.Machines,
        LauncherSound.RaceStart => LobbyMoment.RaceStart,
        LauncherSound.UrgentNotice => LobbyMoment.UrgentNotice,
        _ => null,
    };

    /// <summary>Whether the sound is heard with these settings: not muted, louder than nothing, and for the
    /// navigation's two only with "Navigation sounds" on (off by default: minimal sounds).</summary>
    public static bool Heard(LauncherSound sound, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return LobbyTones.Heard(settings) && (sound is not (LauncherSound.Cursor or LauncherSound.Back) || settings.NavigationSounds);
    }

    /// <summary>The wave file to play at a volume of 0 to 100: the draw's own sound, else the game's, else the
    /// moment's tone; null: none.</summary>
    public static byte[]? Wave(LauncherSound sound, int volume, GameSounds? game) =>
        DrawSounds.Wave(sound, volume)
        ?? game?.Wave(sound, volume)
        ?? (Moment(sound) is { } moment ? LobbyTones.Wave(moment, volume) : null);
}
