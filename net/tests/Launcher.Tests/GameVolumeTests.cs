using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The game's volume (the game is loud), from the setting to where the game reads it; and the launcher's own tones.</summary>
public sealed class GameVolumeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "game-volume-" + Guid.NewGuid().ToString("N"));

    public GameVolumeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>The player's game volume (the Settings page's slider) is the emulator's own volume in the game's
    /// settings, where the runner leaves it to the user folder: set at every start, every other line kept.</summary>
    [Fact]
    public void TheGamesVolumeIsWrittenIntoItsSettingsAtAStart()
    {
        Assert.Equal(["[DSP]", "Volume = 40"], RecompLaunch.WithVolume([], 40));
        Assert.Equal(["[Core]", "A = 1", "[DSP]", "Volume = 0", "Backend = Cubeb", "[Display]", "Fullscreen = True"],
            RecompLaunch.WithVolume(["[Core]", "A = 1", "[DSP]", "Volume = 35", "Backend = Cubeb", "[Display]", "Fullscreen = True"], 0));
        Assert.Equal(["[dsp]", "Volume = 100", "Backend = Cubeb"], RecompLaunch.WithVolume(["[dsp]", "Backend = Cubeb"], 250));
        Assert.Equal(["[DSP]", "Volume = 7", "Muted = False", "Volume = 7", "[Core]", "Volume = 3"],
            RecompLaunch.WithVolume(["[DSP]", "volume=90", "Muted = False", "Volume = 10", "[Core]", "Volume = 3"], 7));
        // a file with the section twice (the emulator reads the later one): every one is set
        Assert.Equal(["[DSP]", "Volume = 20", "[Core]", "A = 1", "[DSP]", "Volume = 20", "Backend = Cubeb", "[dsp]", "Volume = 20"],
            RecompLaunch.WithVolume(["[DSP]", "Volume = 100", "[Core]", "A = 1", "[DSP]", "Volume = 80", "Backend = Cubeb", "[dsp]"], 20));

        var user = Path.Combine(_folder, "volume-user");
        RecompLaunch.PrepareUserFolder(user, volume: 55);
        var ini = Path.Combine(user, "Config", "Dolphin.ini");
        Assert.Equal("55", RecompLaunch.Setting(File.ReadAllLines(ini), "DSP", "Volume"));
        Assert.Equal("Cubeb", RecompLaunch.Setting(File.ReadAllLines(ini), "DSP", "Backend"));
        Assert.Equal("6.0", RecompLaunch.Setting(File.ReadAllLines(ini), "Core", "Overclock"));
        RecompLaunch.PrepareUserFolder(user);                                      // (the Controller page's: the volume is left)
        Assert.Equal("55", RecompLaunch.Setting(File.ReadAllLines(ini), "DSP", "Volume"));
        var written = File.GetLastWriteTimeUtc(ini);
        RecompLaunch.PrepareUserFolder(user, volume: 55);                          // the same again: nothing is written
        Assert.Equal(written, File.GetLastWriteTimeUtc(ini));
        RecompLaunch.PrepareUserFolder(user, volume: 100);
        Assert.Equal("100", RecompLaunch.Setting(File.ReadAllLines(ini), "DSP", "Volume"));
        Assert.True(RecompLaunch.HasVolume(user, 100));
        Assert.False(RecompLaunch.HasVolume(user, 30));
    }

    /// <summary>The game's settings cannot be written (a read-only file on Windows; here a folder that takes no new
    /// file): a start with another volume does not fail on it. The file keeps the volume it had, and the launcher
    /// can tell (and says so in its left column).</summary>
    [Fact]
    public void ASettingsFileThatCannotBeWrittenKeepsItsVolumeAndStopsNoStart()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the folder is closed by its mode");
        var user = Path.Combine(_folder, "closed-user");
        RecompLaunch.PrepareUserFolder(user, volume: 80);
        var config = Path.Combine(user, "Config");
        var ini = Path.Combine(config, "Dolphin.ini");
        var before = File.ReadAllBytes(ini);
        Close(config, true);
        try
        {
            RecompLaunch.PrepareUserFolder(user, volume: 30);                      // no exception
            Assert.Equal(before, File.ReadAllBytes(ini));
            Assert.Empty(Directory.GetFiles(config, "*" + Online.Protocol.AtomicFile.Suffix));
            Assert.False(RecompLaunch.HasVolume(user, 30));
            Assert.True(RecompLaunch.HasVolume(user, 80));
        }
        finally
        {
            Close(config, false);
        }
    }

    /// <summary>The launcher's three tones are short and quiet, at the volume set, and nothing at none.</summary>
    [Theory]
    [InlineData(LobbyMoment.CourseVote)]
    [InlineData(LobbyMoment.Machines)]
    [InlineData(LobbyMoment.RaceStart)]
    public void ATonesSamplesAreShortQuietAndAsLoudAsSet(LobbyMoment moment)
    {
        var full = LobbyTones.Samples(moment, 100);
        Assert.InRange(full.Length / (double)LobbyTones.Rate, 0.2, 0.8);                    // under a second
        Assert.InRange(full.Max(s => Math.Abs((int)s)), 10400, 10500);                      // 0.32 of the loudest there is
        Assert.Equal((0, 0), (full[0], full[^1]));                                           // begins and ends at rest: no click
        var half = LobbyTones.Samples(moment, 50);
        Assert.InRange(half.Max(s => Math.Abs((int)s)), 5200, 5250);
        Assert.All(LobbyTones.Samples(moment, 0), s => Assert.Equal(0, s));
        Assert.Equal(full, LobbyTones.Samples(moment, 400));                                 // (a volume beyond the scale is its top)

        var wave = LobbyTones.Wave(moment, 100);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(wave, 8, 8));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.Equal((1, 1, LobbyTones.Rate, 16), (BitConverter.ToInt16(wave, 20), BitConverter.ToInt16(wave, 22), BitConverter.ToInt32(wave, 24), BitConverter.ToInt16(wave, 34)));
        Assert.Equal(full.Length * 2, BitConverter.ToInt32(wave, 40));
        Assert.Equal(full[100], BitConverter.ToInt16(wave, 44 + 200));
    }

    [Fact]
    public void TheThreeTonesDifferAndAMutedLauncherIsNotHeard()
    {
        Assert.Equal(3, new[] { LobbyMoment.CourseVote, LobbyMoment.Machines, LobbyMoment.RaceStart }.Select(m => LobbyTones.Notes(m).Count).Distinct().Count());
        Assert.True(LobbyTones.Heard(new LauncherSettings()));
        Assert.False(LobbyTones.Heard(new LauncherSettings { SoundMuted = true }));
        Assert.False(LobbyTones.Heard(new LauncherSettings { SoundVolume = 0 }));
    }

    /// <summary>A folder that takes no new file (and so no write of a file in it, which goes beside the file first),
    /// or takes them again.</summary>
    private static void Close(string folder, bool closed)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, closed ? UnixFileMode.UserRead | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
