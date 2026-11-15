using System.Runtime.InteropServices;
using Avalonia.Controls;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// How the launcher makes itself noticed at a lobby's three moments (<see cref="LobbyMoment"/>: the course vote
/// opens, the machine stage opens, the player's race starts), and at a server's notice of the
/// two highest tiers (the flash for a warning, the flash and its own tone for an urgent one): a short quiet sound
/// (<see cref="LauncherSound"/>: the game's own menu sound from the player's disc, <see cref="Game"/>, the draw's
/// made in code, <see cref="DrawSounds"/>, else the launcher's own tone, <see cref="LobbyTones"/>) at the volume set
/// in Settings, unless muted there, and, when the launcher's window is not in front, its button on the taskbar
/// flashing until the window is. The draw's blips and
/// the navigation's cursor and back sound through here too (<see cref="Sound(LauncherSound, LauncherSettings)"/>),
/// without a flash. Windows only: elsewhere (the tests' Linux) nothing sounds and nothing flashes, and
/// <see cref="Played"/> and <see cref="Sounded"/> still say what would have. One sound plays at a time, on one stream
/// of the system kept running while sounds come (<see cref="SoundLine"/> on <see cref="WaveOutStream"/>): a sound cuts
/// the one before it short, however many come in a row.
/// </summary>
public static partial class Attention
{
    private const uint FlashTray = 0x0002;
    private const uint FlashUntilInFront = 0x000C;

    private static readonly Lock Gate = new();

    /// <summary>The launcher's one stream of sounds, made with its first sound (on Windows only).</summary>
    private static readonly Lazy<SoundLine> Line = new(() => new SoundLine(new WaveOutStream()));

    /// <summary>Each sound's wave at each volume, made once and kept (a few kilobytes each): a sound plays at once,
    /// not after its wave is made again (a navigation sound came late, or seemed not to come, when it was).</summary>
    private static readonly Dictionary<(LauncherSound, int), byte[]> Made = [];
    private static GameSounds? _madeFor;

    private static GameSounds? _game;

    /// <summary>The game's menu sounds of the disc set (<see cref="Theme.GameSkin"/> decodes them when the disc's
    /// art is read); null: none (no disc, or its bank cannot be read), and the launcher's own tones sound.</summary>
    public static GameSounds? Game
    {
        get => Volatile.Read(ref _game);
        set => Volatile.Write(ref _game, value);
    }

    /// <summary>Every lobby moment's sound asked for, with its volume: what a test reads (no sound is made beside the tests).</summary>
    public static event Action<LobbyMoment, int>? Played;

    /// <summary>Every sound asked for and heard (a moment's too), with its volume: what a test reads.</summary>
    public static event Action<LauncherSound, int>? Sounded;

    /// <summary>Every flash asked for, with its window (whether the window was in front or not): what a test reads.</summary>
    public static event Action<Window>? Flashed;

    /// <summary>The moment's sound, at the settings' volume; nothing when the launcher's sounds are muted or at 0.</summary>
    public static void Sound(LobbyMoment moment, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var sound = LauncherSounds.Of(moment);
        if (!LauncherSounds.Heard(sound, settings))
        {
            return;
        }
        Played?.Invoke(moment, LauncherSounds.Volume(sound, settings));
        Sound(sound, settings);
    }

    /// <summary>The sound at the settings' volume: the game's when the disc gave it, else a moment's own tone, else
    /// none (<see cref="LauncherSounds"/>); nothing when it is not heard with these settings
    /// (<see cref="LauncherSounds.Heard"/>: muted, at 0, or a navigation sound with those off).</summary>
    public static void Sound(LauncherSound sound, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!LauncherSounds.Heard(sound, settings))
        {
            return;
        }
        var volume = LauncherSounds.Volume(sound, settings);   // (the launcher's volume times its kind's)
        Sounded?.Invoke(sound, volume);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        lock (Gate)
        {
            if (!ReferenceEquals(_madeFor, Game))
            {
                Made.Clear();   // (another disc's sounds)
                _madeFor = Game;
            }
            var key = (sound, volume);
            if (!Made.TryGetValue(key, out var wave))
            {
                if (LauncherSounds.Wave(sound, volume, Game) is not { } made)
                {
                    return;
                }
                wave = made;
                Made[key] = wave;
            }
            _ = Line.Value.Play(wave);   // (a sound playing gives way to it)
        }
    }

    /// <summary>The window's taskbar button flashes until the window is in front; nothing when it is in front now.</summary>
    public static void Flash(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Flashed?.Invoke(window);
        if (window.IsActive || !OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = FlashTray | FlashUntilInFront };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
