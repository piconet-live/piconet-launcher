using System.Security.Cryptography;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The launcher's sounds from the game's menu bank (<see cref="GcaxBank"/>, <see cref="GameSounds"/>): DSP ADPCM
/// decoded as the console does, past 32 bits too; a sound found by its id with its program's pitch and volume; a
/// bank cut short or of other bytes is no sound and raises nothing; the sounds kept in the data folder's cache and
/// read back from it; each at the launcher's tones' level; the listening set's recommended mix (the game's vote,
/// machines and start, our draw); without a disc, or without one of its sounds, the launcher's own tones for the
/// moments, the draw's own blip and chime still, and nothing for the navigation. On your disc (skipped elsewhere), three of the game's sounds decode to
/// what a Python decoder gave: their lengths and hashes only, no game data.
/// </summary>
public sealed class GameSoundsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DspAdpcmIsTheNibbleScaledAndPredictedFromTheTwoBefore()
    {
        // predictor 0 with no coefficients: each sample is its nibble times the scale (2^3)
        var plain = MadeUpSoundBank.Plain(32000, 3, 1, 2, -1, -8, 7, 0, 3);
        Assert.Equal([8, 16, -8, -64, 56, 0, 24], DspAdpcm.Decode(plain.Frames, new short[16], plain.Samples + 1, 100));

        // predictor 1: 1.0 times the sample before (2048 in 11 bits of fraction), so the nibbles add up
        byte[] frame = [0x10, 0x11, 0x1F, 0, 0, 0, 0, 0];
        short[] coefficients = [0, 0, 2048, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal([1, 2, 3, 2], DspAdpcm.Decode(frame, coefficients, 5, 100));

        // the prediction passes 32 bits (both coefficients near 2.0 and the samples before at their top): the sample
        // is the top, not a sum that wrapped round to the bottom
        byte[] loud = [0x0D, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x14, 0x70, 0, 0, 0, 0, 0, 0];
        short[] near2 = [0, 0, 32767, 32767, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal([32767, 32767], DspAdpcm.Decode(loud, near2, 3, 100));
        Assert.Equal(32767, DspAdpcm.Decode(loud, near2, 18, 100)[14]);

        // never more than asked for, nor past the data
        Assert.Equal(3, DspAdpcm.Decode(plain.Frames, new short[16], plain.Samples + 1, 3).Length);
        Assert.Equal(14, DspAdpcm.Decode(plain.Frames, new short[16], 1000, 100).Length);
    }

    [Theory]
    [InlineData(5, 35, 44086)]      // a 44.1 kHz wave: its own rate, to a hundredth of a semitone
    [InlineData(-7, 35, 22043)]     // 22.05 kHz
    [InlineData(-12, 0, 16000)]     // 16 kHz
    [InlineData(-19, 35, 11021)]    // 11.025 kHz
    [InlineData(8, 35, 52427)]      // A9010600: three semitones up
    public void TheGamePlaysAVoiceAtItsKeyAndFineAgainst32kHz(int key, int fine, int rate) =>
        Assert.Equal(rate, GcaxBank.PlayedRate(key, fine));

    [Fact]
    public void ASoundIsFoundByItsGroupAndRecordWithItsProgramsPitchAndVolume()
    {
        var bank = MadeUpSoundBank.Make(
            new Dictionary<ushort, (int, int)[]> { [0xA900] = [(0, 50)], [0xA901] = [(0, 90), (1, 110)] },
            [(0, 0, 5, 35), (1, 1, -12, 0)],
            [MadeUpSoundBank.Plain(44100, 0, 1, 2, 3), MadeUpSoundBank.Plain(16000, 4, [.. Enumerable.Range(0, 30).Select(i => (i % 16) - 8)])]);

        var tick = GcaxBank.Sound(bank, 0xA9010100);
        Assert.NotNull(tick);
        Assert.Equal((16000, 16000, 110, 30), (tick.Rate, tick.OwnRate, tick.Volume, tick.Samples.Length));
        Assert.Equal([.. Enumerable.Range(0, 30).Select(i => (short)(((i % 16) - 8) * 16))], tick.Samples);
        var first = GcaxBank.Sound(bank, 0xA9000000);
        Assert.Equal((44086, 44100, 50), (first!.Rate, first.OwnRate, first.Volume));
        Assert.Equal([1, 2, 3], first.Samples);

        Assert.Null(GcaxBank.Sound(bank, 0xA9010200));   // no such record
        Assert.Null(GcaxBank.Sound(bank, 0xA9030000));   // no such group
    }

    [Fact]
    public void ABankCutShortOrOfOtherBytesIsNoSoundAndRaisesNothing()
    {
        var bank = MadeUpSoundBank.Chosen();
        Assert.NotNull(GameSounds.FromBank(bank));
        for (var length = 0; length < bank.Length; length++)
        {
            _ = GameSounds.FromBank(bank.AsSpan(0, length));
        }
        var random = new Random(7);
        for (var round = 0; round < 2000; round++)
        {
            var changed = (byte[])bank.Clone();
            for (var k = 0; k < 4; k++)
            {
                changed[random.Next(changed.Length)] = (byte)random.Next(256);
            }
            _ = GameSounds.FromBank(changed);
        }
        Assert.Null(GameSounds.FromBank("not a sound bank"u8));
        Assert.Null(GameSounds.Load(null, _dir));
    }

    [Fact]
    public void TheSoundsAreKeptInTheDataFolderAndReadBackFromIt()
    {
        var bank = MadeUpSoundBank.Chosen();
        var key = GameSounds.Key(bank);
        Assert.Matches("^[0-9a-f]{16}$", key);
        var folder = GameSounds.Folder(_dir, key);
        Assert.Equal(Path.Combine(_dir, "cache", "sounds", key), folder);
        Assert.Equal(Path.Combine(folder, "A9010B00.wav"), GameSounds.File(folder, 0xA9010B00));
        Assert.NotEqual(key, GameSounds.Key(bank.AsSpan(1)));

        var decoded = GameSounds.Load(bank, _dir);
        Assert.NotNull(decoded);
        Assert.Equal(GameSounds.Ids.Values.Distinct().Count(), Directory.GetFiles(folder, "*.wav").Length);   // (a wave per id: the confirm is the machine stage's)
        Assert.Empty(Directory.GetFiles(folder, "*" + Online.Protocol.AtomicFile.Suffix));
        foreach (var (sound, id) in GameSounds.Ids)
        {
            var kept = WaveFile.Read(File.ReadAllBytes(GameSounds.File(folder, id)));
            Assert.Equal(decoded.Of(sound)!.Rate, kept!.Value.Rate);
            Assert.Equal(decoded.Of(sound)!.Samples, kept.Value.Samples);
        }
        Assert.Equal(16000, decoded.Of(LauncherSound.Machines)!.Rate);
        Assert.Equal(44086, decoded.Of(LauncherSound.CourseVote)!.Rate);

        // read back from the cache, not decoded again: a kept file changed is what the next load gives
        File.WriteAllBytes(GameSounds.File(folder, 0xA9010B00), WaveFile.Make([1, 2, 3], 8000));
        var again = GameSounds.Load(bank, _dir);
        Assert.Equal(8000, again!.Of(LauncherSound.CourseVote)!.Rate);
        Assert.Equal([1, 2, 3], again.Of(LauncherSound.CourseVote)!.Samples);

        // a file that does not read whole, or is gone: decoded again and written anew
        File.WriteAllBytes(GameSounds.File(folder, 0xA9010B00), [1, 2, 3]);
        Assert.Null(GameSounds.Kept(folder));
        var anew = GameSounds.Load(bank, _dir);
        Assert.Equal(decoded.Of(LauncherSound.CourseVote)!.Samples, anew!.Of(LauncherSound.CourseVote)!.Samples);
        Assert.NotNull(GameSounds.Kept(folder));
        File.Delete(GameSounds.File(folder, 0xA9010100));
        Assert.Null(GameSounds.Kept(folder));
        Assert.Equal(decoded.Of(LauncherSound.Machines)!.Samples, GameSounds.Load(bank, _dir)!.Of(LauncherSound.Machines)!.Samples);
        Assert.True(File.Exists(GameSounds.File(folder, 0xA9010100)));

        // no data folder: decoded, kept nowhere
        Assert.NotNull(GameSounds.Load(bank, null));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(40)]
    public void AGameSoundIsAtTheTonesLevelAndTheVolumeSet(int volume)
    {
        var sounds = GameSounds.FromBank(MadeUpSoundBank.Chosen())!;
        foreach (var sound in GameSounds.Ids.Keys)
        {
            var wave = WaveFile.Read(sounds.Wave(sound, volume))!.Value;
            Assert.Equal(sounds.Of(sound)!.Rate, wave.Rate);
            Assert.Equal((int)Math.Round(short.MaxValue * LobbyTones.Peak * volume / 100), wave.Samples.Max(s => Math.Abs((int)s)));
            Assert.Equal(wave.Samples, WaveFile.Read(LauncherSounds.Wave(sound, volume, sounds))!.Value.Samples);
        }
        Assert.Null(sounds.Wave(LauncherSound.UrgentNotice, volume));
    }

    /// <summary>The mix the listening set recommends with two
    /// picks kept: the game's short bleeps (A-vote-candidate-2) as the vote opens, the menus' confirm at
    /// the machine stage and for a machine confirmed (the "bip-beeep" was a whistle), its riser kept for the race
    /// start's moment (which no longer sounds in a lobby); and the draw ours, B2-draw (<see cref="DrawSounds"/>), the
    /// same with the game's sounds or without them.</summary>
    [Fact]
    public void TheMixIsTheGamesVoteConfirmAndStartAndOurOwnDraw()
    {
        Assert.Equal(0xA9010B00u, GameSounds.Ids[LauncherSound.CourseVote]);
        Assert.Equal(0xA9010100u, GameSounds.Ids[LauncherSound.Machines]);
        Assert.Equal(0xA9010100u, GameSounds.Ids[LauncherSound.Confirm]);
        Assert.Equal(0xA9011700u, GameSounds.Ids[LauncherSound.RaceStart]);
        Assert.False(GameSounds.Ids.ContainsKey(LauncherSound.DrawHop));
        Assert.False(GameSounds.Ids.ContainsKey(LauncherSound.CourseDrawn));
        Assert.False(GameSounds.Ids.ContainsKey(LauncherSound.UrgentNotice));

        var game = GameSounds.FromBank(MadeUpSoundBank.Chosen())!;
        foreach (var moment in new[] { LobbyMoment.CourseVote, LobbyMoment.Machines, LobbyMoment.RaceStart })
        {
            var sound = LauncherSounds.Of(moment);
            Assert.Equal(game.Wave(sound, 70), LauncherSounds.Wave(sound, 70, game));
            Assert.NotEqual(LobbyTones.Wave(moment, 70), LauncherSounds.Wave(sound, 70, game));
        }
        foreach (var sound in new[] { LauncherSound.DrawHop, LauncherSound.CourseDrawn })
        {
            Assert.Equal(DrawSounds.Wave(sound, 70), LauncherSounds.Wave(sound, 70, game));
            Assert.Equal(DrawSounds.Wave(sound, 70), LauncherSounds.Wave(sound, 70, null));
        }
        Assert.NotEqual(DrawSounds.Wave(LauncherSound.DrawHop, 70), DrawSounds.Wave(LauncherSound.CourseDrawn, 70));
        foreach (var sound in Enum.GetValues<LauncherSound>().Except([LauncherSound.DrawHop, LauncherSound.CourseDrawn]))
        {
            Assert.Null(DrawSounds.Wave(sound, 70));
        }
    }

    /// <summary>The draw's chime is as loud as every launcher sound at the volume set, its blip quieter by B2-draw's
    /// own measure (its hops' loudest are 0.22 to 0.33 of its chime's); both short, at 44.1 kHz, the same every time, silent at 0.</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(40)]
    [InlineData(0)]
    public void TheDrawsBlipAndChimeAreAtTheTonesLevelAndTheVolumeSet(int volume)
    {
        var chime = WaveFile.Read(DrawSounds.Wave(LauncherSound.CourseDrawn, volume))!.Value;
        var hop = WaveFile.Read(DrawSounds.Wave(LauncherSound.DrawHop, volume))!.Value;
        Assert.Equal((DrawSounds.Rate, DrawSounds.Rate), (chime.Rate, hop.Rate));
        var chimeTop = chime.Samples.Max(s => Math.Abs((int)s));
        var hopTop = hop.Samples.Max(s => Math.Abs((int)s));
        Assert.Equal((int)Math.Round(short.MaxValue * LobbyTones.Peak * volume / 100), chimeTop);
        if (volume > 0)
        {
            Assert.InRange(hopTop / (double)chimeTop, 0.22, 0.34);
        }
        else
        {
            Assert.Equal(0, hopTop);
        }
        Assert.Equal((int)(0.045 * DrawSounds.Rate), hop.Samples.Length);   // a blip under a hop's shortest gap
        Assert.InRange(chime.Samples.Length / (double)DrawSounds.Rate, 0.6, 1.2);
        Assert.Equal(0, hop.Samples[^1]);
        Assert.Equal(0, chime.Samples[^1]);   // both end at silence: no click
        Assert.Equal(DrawSounds.Samples(LauncherSound.DrawHop, volume), hop.Samples);
        Assert.Equal(DrawSounds.Samples(LauncherSound.CourseDrawn, volume), chime.Samples);
    }

    /// <summary>A disc whose bank lacks one of the chosen sounds (the cache's file missing and not decodable): that
    /// moment falls back to the launcher's own tone, the others keep the game's.</summary>
    [Fact]
    public void AMomentWhoseGameSoundIsMissingFallsBackToItsTone()
    {
        var game = GameSounds.Load(MadeUpSoundBank.Chosen(riser: false), _dir)!;   // no riser (A9011700)
        Assert.Null(game.Of(LauncherSound.RaceStart));
        Assert.Equal(LobbyTones.Wave(LobbyMoment.RaceStart, 60), LauncherSounds.Wave(LauncherSound.RaceStart, 60, game));
        Assert.Equal(game.Wave(LauncherSound.CourseVote, 60), LauncherSounds.Wave(LauncherSound.CourseVote, 60, game));
        Assert.NotNull(game.Of(LauncherSound.Machines));

        // and its cache is whole: read back at the next start, not decoded again (the missing one an empty wave)
        var bank = MadeUpSoundBank.Chosen(riser: false);
        var kept = GameSounds.Kept(GameSounds.Folder(_dir, GameSounds.Key(bank)));
        Assert.NotNull(kept);
        Assert.Null(kept.Of(LauncherSound.RaceStart));
        Assert.Equal(game.Of(LauncherSound.CourseVote)!.Samples, kept.Of(LauncherSound.CourseVote)!.Samples);
    }

    [Fact]
    public void WithoutTheDiscTheMomentsAreTheLaunchersOwnTonesAndTheNavigationIsSilent()
    {
        foreach (var moment in Enum.GetValues<LobbyMoment>())
        {
            Assert.Equal(LobbyTones.Wave(moment, 50), LauncherSounds.Wave(LauncherSounds.Of(moment), 50, null));
        }
        foreach (var sound in new[] { LauncherSound.Cursor, LauncherSound.Back })
        {
            Assert.Null(LauncherSounds.Wave(sound, 50, null));
        }
        // the urgent notice keeps its blip with the game's sounds too
        Assert.Equal(LobbyTones.Wave(LobbyMoment.UrgentNotice, 50), LauncherSounds.Wave(LauncherSound.UrgentNotice, 50, GameSounds.FromBank(MadeUpSoundBank.Chosen())));
    }

    [Fact]
    public void TheNavigationIsHeardOnlyWhenSwitchedOnAndNothingWhenMuted()
    {
        var settings = new LauncherSettings();
        Assert.False(settings.NavigationSounds);
        Assert.True(LauncherSounds.Heard(LauncherSound.DrawHop, settings));
        Assert.False(LauncherSounds.Heard(LauncherSound.Cursor, settings));
        Assert.False(LauncherSounds.Heard(LauncherSound.Back, settings));
        settings = settings with { NavigationSounds = true };
        Assert.True(LauncherSounds.Heard(LauncherSound.Cursor, settings));
        Assert.True(LauncherSounds.Heard(LauncherSound.Back, settings));
        foreach (var sound in Enum.GetValues<LauncherSound>())
        {
            Assert.False(LauncherSounds.Heard(sound, settings with { SoundMuted = true }));
            Assert.False(LauncherSounds.Heard(sound, settings with { SoundVolume = 0 }));
        }
    }

    /// <summary>The tick, the bleep and the riser on your disc: their sample counts and the first 32 hexadecimal
    /// characters of a SHA-256 over their samples (16 bit, low byte first), as a Python decoder decoded them.</summary>
    [Theory]
    [InlineData(0xA9010700u, 44100, 3264, "6bf4269e74ec8e53df9157f503752589")]
    [InlineData(0xA9010E00u, 16000, 4412, "c7b5cd478f395cb91396457a1cd0f9af")]
    [InlineData(0xA9011700u, 22050, 27664, "553fa168e36d508842d094bd3c087a05")]
    public void OnARealDiscTheGamesSoundsDecodeAsThePythonDecoderDid(uint id, int rate, int count, string hash)
    {
        var path = RealDiscStatsTests.PlayersDisc();
        Assert.SkipWhen(path is null || !File.Exists(path), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(path);
        var bank = art!.SoundBank();
        Assert.NotNull(bank);
        var sound = GcaxBank.Sound(bank, id);
        Assert.NotNull(sound);
        Assert.Equal((rate, count), (sound.OwnRate, sound.Samples.Length));
        var bytes = new byte[sound.Samples.Length * 2];
        Buffer.BlockCopy(sound.Samples, 0, bytes, 0, bytes.Length);   // (the machine's order: low byte first)
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]);
        // the game plays it at its own rate, to a hundredth of a semitone
        Assert.InRange(12 * Math.Log2((double)sound.Rate / sound.OwnRate), -0.01, 0.01);
        // and every sound the launcher takes is on the disc
        Assert.All(GameSounds.Ids.Keys, s => Assert.NotNull(GameSounds.FromBank(bank)!.Of(s)));
    }
}
