using System.Globalization;
using System.Security.Cryptography;

namespace Launcher.Core;

/// <summary>A sound of the game ready to play: its samples at the rate the game plays them.</summary>
public sealed record GameSound(int Rate, short[] Samples);

/// <summary>
/// The launcher's sounds from the game's own menu bank (all of them the
/// game's), decoded from the player's disc (<see cref="GcaxBank"/>) and never shipped: no sound of the game is in the
/// repository or the package. They are decoded once per disc and kept in the data folder,
/// <c>cache/sounds/DISC/ID.wav</c> (DISC: <see cref="Key"/>, ID: <c>A9010B00</c>), as the game plays them; each is
/// brought to the launcher's own tones' level when played (<see cref="LobbyTones.Peak"/>), so nothing is louder
/// than before. Without the disc or the bank the launcher's own tones sound (<see cref="LauncherSounds"/>).
/// </summary>
public sealed class GameSounds
{
    /// <summary>The kept sounds' form: raised whenever their decoding, choice or file changes.</summary>
    public const int Recipe = 2;

    /// <summary>The menu sound bank on the disc.</summary>
    public const string BankFile = "snd/normal_se/PACK0.bin";

    /// <summary>The game's sound for each of the launcher's (a listening set's recommended mix, with two picks of
    /// its own): the vote opens to a short burst of bleeps, the machine stage opens to the menus' confirm (the
    /// "bip-beeep" is a whistle), a machine confirmed is that confirm again (the same whoosh); the race start's riser
    /// is kept here, though that moment makes no sound (no noise when the runner starts); the menus' cursor and cancel for the
    /// navigation. The draw is ours (<see cref="DrawSounds"/>: the game's tick is very high and thin), and an urgent
    /// notice keeps the launcher's own blip: nothing in the bank means "urgent".</summary>
    public static IReadOnlyDictionary<LauncherSound, uint> Ids { get; } = new Dictionary<LauncherSound, uint>
    {
        [LauncherSound.CourseVote] = 0xA9010B00,
        [LauncherSound.Machines] = 0xA9010100,
        [LauncherSound.Confirm] = 0xA9010100,
        [LauncherSound.RaceStart] = 0xA9011700,
        [LauncherSound.Cursor] = 0xA9010000,
        [LauncherSound.Back] = 0xA9010200,
    };

    private readonly Dictionary<LauncherSound, GameSound> _sounds;

    private GameSounds(Dictionary<LauncherSound, GameSound> sounds) => _sounds = sounds;

    /// <summary>The game's sound for this one; null when it has none.</summary>
    public GameSound? Of(LauncherSound sound) => _sounds.GetValueOrDefault(sound);

    /// <summary>The sound as a wave file at a volume of 0 to 100, its loudest sample at the launcher's tones' level
    /// (<see cref="LobbyTones.Peak"/>); null when the game has no sound for it.</summary>
    public byte[]? Wave(LauncherSound sound, int volume)
    {
        if (Of(sound) is not { } game)
        {
            return null;
        }
        var top = game.Samples.Length == 0 ? 0 : game.Samples.Max(s => Math.Abs((int)s));
        var scale = top > 0 ? short.MaxValue * LobbyTones.Peak * (Math.Clamp(volume, 0, 100) / 100.0) / top : 0;
        var samples = game.Samples.Select(s => (short)Math.Round(s * scale)).ToArray();
        return WaveFile.Make(samples, game.Rate);
    }

    /// <summary>The chosen sounds decoded from the bank's bytes; null when it has none of them.</summary>
    public static GameSounds? FromBank(ReadOnlySpan<byte> bank)
    {
        var sounds = new Dictionary<LauncherSound, GameSound>();
        foreach (var (sound, id) in Ids)
        {
            if (GcaxBank.Sound(bank, id) is { Samples.Length: > 0 } decoded)
            {
                sounds[sound] = new GameSound(decoded.Rate, decoded.Samples);
            }
        }
        return sounds.Count > 0 ? new GameSounds(sounds) : null;
    }

    /// <summary>The bank's key: 16 hexadecimal characters of a SHA-256 over the <see cref="Recipe"/> and its bytes.</summary>
    public static string Key(ReadOnlySpan<byte> bank)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.ASCII.GetBytes($"game sounds {Recipe}\n"));
        hash.AppendData(bank);
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    /// <summary>The folder for a bank's sounds.</summary>
    public static string Folder(string dataFolder, string key) => Path.Combine(dataFolder, "cache", "sounds", key);

    /// <summary>A sound's file in that folder: <c>A9010B00.wav</c>.</summary>
    public static string File(string folder, uint id) => Path.Combine(folder, id.ToString("X8", CultureInfo.InvariantCulture) + ".wav");

    /// <summary>The rate of the empty wave kept for a sound the bank has none of (any rate <see cref="WaveFile.Read"/>
    /// takes).</summary>
    private const int NoneRate = 8000;

    /// <summary>The largest sound file read back: 4 MiB (the longest chosen is under 200 kB).</summary>
    private const int Largest = 4 << 20;

    /// <summary>The bank's sounds: from the data folder's cache when it holds every one, else decoded from the bank and
    /// kept there (no data folder: decoded, kept nowhere). Null without a bank or with one that has none of them.</summary>
    public static GameSounds? Load(byte[]? bank, string? dataFolder)
    {
        if (bank is null)
        {
            return null;
        }
        var folder = dataFolder is null ? null : Folder(dataFolder, Key(bank));
        if (folder is not null && Kept(folder) is { } kept)
        {
            return kept;
        }
        var sounds = FromBank(bank);
        if (sounds is not null && folder is not null)
        {
            sounds.Keep(folder);
        }
        return sounds;
    }

    /// <summary>The sounds kept in the folder; null unless every chosen one is there and reads whole. An empty wave
    /// is a sound the bank has none of (<see cref="Keep"/>): left out, as <see cref="FromBank"/> leaves it out.</summary>
    public static GameSounds? Kept(string folder)
    {
        var sounds = new Dictionary<LauncherSound, GameSound>();
        foreach (var (sound, id) in Ids)
        {
            try
            {
                var info = new FileInfo(File(folder, id));
                if (!info.Exists || info.Length is < 44 or > Largest || WaveFile.Read(System.IO.File.ReadAllBytes(info.FullName)) is not { } wave)
                {
                    return null;
                }
                if (wave.Samples.Length > 0)
                {
                    sounds[sound] = new GameSound(wave.Rate, wave.Samples);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return new GameSounds(sounds);
    }

    /// <summary>Keeps every sound in the folder: each written beside its place and moved in, so that a file is whole
    /// or not there; a chosen sound the bank has none of as an empty wave, so that the cache of such a bank is whole
    /// too (without it the bank was decoded at every start). One that cannot be written is decoded again next time.</summary>
    public void Keep(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (sound, game) in _sounds)
            {
                Online.Protocol.AtomicFile.WriteAllBytes(File(folder, Ids[sound]), WaveFile.Make(game.Samples, game.Rate));
            }
            foreach (var id in Ids.Values.Distinct().Where(id => !_sounds.Keys.Any(sound => Ids[sound] == id)))
            {
                Online.Protocol.AtomicFile.WriteAllBytes(File(folder, id), WaveFile.Make([], NoneRate));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // decoded again next time
        }
    }
}

