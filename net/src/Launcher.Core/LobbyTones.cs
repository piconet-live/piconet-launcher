namespace Launcher.Core;

/// <summary>A note of a tone: its pitch, when it begins and how long it sounds, in seconds.</summary>
public sealed record ToneNote(double Hz, double Start, double Length);

/// <summary>
/// The launcher's own tones (minimal sounds, never obnoxious): four
/// short, quiet tones, one for each <see cref="LobbyMoment"/> (the fourth an urgent notice's), made here from a few
/// notes each. The lobby's moments sound with the game's own menu sounds (<see cref="GameSounds"/>:
/// the vote's bleeps, the machines' confirm whoosh, the start's riser), decoded from the player's disc at runtime and
/// kept in the data folder's cache, never shipped, and the draw with its own blips and chime (<see cref="DrawSounds"/>);
/// these tones are the moments' fallback when no disc is set or its sound bank (or a sound in it) cannot be read, and
/// the urgent notice's blip is always this one. No sound file is
/// in the repository or the package: a tone is worked out when it is first played (a few thousand samples), as a
/// wave file in memory for the system to play. <see cref="Peak"/> is the level of every sound the launcher makes.
/// </summary>
public static class LobbyTones
{
    /// <summary>Samples a second: mono, 16 bit.</summary>
    public const int Rate = 22050;

    /// <summary>A tone's loudest sample at full volume, of the loudest there is: quiet.</summary>
    public const double Peak = 0.32;

    /// <summary>The notes of a moment's tone: a question for the vote (E5, A5), a double tap and a step up for the
    /// machines (G5, G5, C6), a rising chord for the start (C5, E5, G5, C6); for an urgent notice a short blip, two
    /// quick high taps (B5, B5) that sound like none of the lobby's.</summary>
    public static IReadOnlyList<ToneNote> Notes(LobbyMoment moment) => moment switch
    {
        LobbyMoment.UrgentNotice => [new(987.77, 0.00, 0.07), new(987.77, 0.10, 0.09)],
        LobbyMoment.CourseVote => [new(659.26, 0.00, 0.16), new(880.00, 0.11, 0.24)],
        LobbyMoment.Machines => [new(783.99, 0.00, 0.12), new(783.99, 0.14, 0.12), new(1046.50, 0.28, 0.26)],
        _ => [new(523.25, 0.00, 0.14), new(659.26, 0.12, 0.14), new(783.99, 0.24, 0.14), new(1046.50, 0.36, 0.34)],
    };

    /// <summary>The tone's samples at a volume of 0 to 100: each note a sine with a soft second partial, a 6 ms
    /// rise and a fall to nothing, so that nothing clicks.</summary>
    public static short[] Samples(LobbyMoment moment, int volume)
    {
        var notes = Notes(moment);
        var mix = new double[(int)(Rate * (notes.Max(n => n.Start + n.Length) + 0.02))];
        foreach (var note in notes)
        {
            var first = (int)(Rate * note.Start);
            var count = (int)(Rate * note.Length);
            for (var i = 0; i < count; i++)
            {
                var t = i / (double)Rate;
                var rise = Math.Min(1, t / 0.006);
                var fall = Math.Exp(-5 * t / note.Length);
                var last = Math.Min(1, (note.Length - t) / 0.01);
                mix[first + i] += (Math.Sin(2 * Math.PI * note.Hz * t) + (0.18 * Math.Sin(4 * Math.PI * note.Hz * t))) * rise * fall * last;
            }
        }
        var top = mix.Max(Math.Abs);
        var scale = top > 0 ? short.MaxValue * Peak * (Math.Clamp(volume, 0, 100) / 100.0) / top : 0;
        return [.. mix.Select(v => (short)Math.Round(v * scale))];
    }

    /// <summary>The tone as a wave file's bytes (RIFF, PCM, mono, 16 bit).</summary>
    public static byte[] Wave(LobbyMoment moment, int volume) => WaveFile.Make(Samples(moment, volume), Rate);

    /// <summary>Whether a moment is heard with these settings: not muted, and louder than nothing.</summary>
    public static bool Heard(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings is { SoundMuted: false, SoundVolume: > 0 };
    }
}
