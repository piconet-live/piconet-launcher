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
/// <see cref="Played"/> and <see cref="Sounded"/> still say what would have. One sound plays at a time (the system's
/// PlaySound): a sound cuts the one before it short.
/// </summary>
public static partial class Attention
{
    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;
    private const uint Memory = 0x0004;
    private const uint FlashTray = 0x0002;
    private const uint FlashUntilInFront = 0x000C;

    private static readonly Lock Gate = new();
    private static IntPtr _playing;   // the wave the system is playing from: kept until the next one, or the end

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
        if (!LobbyTones.Heard(settings))
        {
            return;
        }
        Played?.Invoke(moment, settings.SoundVolume);
        Sound(LauncherSounds.Of(moment), settings);
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
        Sounded?.Invoke(sound, settings.SoundVolume);
        if (!OperatingSystem.IsWindows() || LauncherSounds.Wave(sound, settings.SoundVolume, Game) is not { } wave)
        {
            return;
        }
        lock (Gate)
        {
            // (the system plays from this memory after the call returns: it is freed when the next sound replaces it)
            var memory = Marshal.AllocHGlobal(wave.Length);
            Marshal.Copy(wave, 0, memory, wave.Length);
            _ = PlaySoundW(IntPtr.Zero, IntPtr.Zero, 0);   // ends the sound before, so that its memory can go
            var before = _playing;
            _playing = memory;
            if (before != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(before);
            }
            _ = PlaySoundW(memory, IntPtr.Zero, Async | NoDefault | Memory);
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

    [LibraryImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
