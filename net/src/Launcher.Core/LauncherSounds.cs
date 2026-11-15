namespace Launcher.Core;

/// <summary>Every sound the launcher makes: the lobby's moments (<see cref="LobbyMoment"/>), the draw's blip on every
/// hop and its chime on the course drawn, and the navigation's cursor and back (the pad's, with Settings' "Controller navigation").</summary>
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

/// <summary>The launcher's sounds by kind, each with its own volume in the settings: the menu's (the cursor, back,
/// confirm), the notifications (the lobby's moments, an urgent notice), and the others (the course draw's).</summary>
public enum SoundKind
{
    Menu,
    Notice,
    Other,
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

    /// <summary>Whether the sound is heard with these settings: not muted, louder than nothing (its kind's volume
    /// too: <see cref="Volume"/>), and for the navigation's two only while the pad works the pages
    /// (<see cref="LauncherSettings.PadNavigation"/>, off by default: minimal sounds).</summary>
    public static bool Heard(LauncherSound sound, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Volume(sound, settings) > 0 && (sound is not (LauncherSound.Cursor or LauncherSound.Back) || settings.PadNavigation);
    }

    /// <summary>The kind a sound is of: the menu's, a notification, or another.</summary>
    public static SoundKind KindOf(LauncherSound sound) => sound switch
    {
        LauncherSound.Cursor or LauncherSound.Back or LauncherSound.Confirm => SoundKind.Menu,
        LauncherSound.DrawHop or LauncherSound.CourseDrawn => SoundKind.Other,
        _ => SoundKind.Notice,
    };

    /// <summary>How loud the sound plays, 0 to 100: the launcher's volume times its kind's share; 0 when muted.</summary>
    public static int Volume(LauncherSound sound, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!LobbyTones.Heard(settings))
        {
            return 0;
        }
        var share = KindOf(sound) switch
        {
            SoundKind.Menu => settings.MenuSoundVolume,
            SoundKind.Notice => settings.NoticeSoundVolume,
            _ => settings.OtherSoundVolume,
        };
        return (int)Math.Round(settings.SoundVolume * Math.Clamp(share, 0, 100) / 100.0);
    }

    /// <summary>The wave file to play at a volume of 0 to 100: the draw's own sound, else the game's, else the
    /// moment's tone; null: none.</summary>
    public static byte[]? Wave(LauncherSound sound, int volume, GameSounds? game) =>
        DrawSounds.Wave(sound, volume)
        ?? game?.Wave(sound, volume)
        ?? (Moment(sound) is { } moment ? LobbyTones.Wave(moment, volume) : null);
}
