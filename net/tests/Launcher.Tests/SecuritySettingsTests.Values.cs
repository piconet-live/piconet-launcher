using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;
/// <summary>What a file that was read holds: a value that is not sound goes back to its default, and no path or
/// address in it is an error for the page that uses it.</summary>
public sealed partial class SecuritySettingsTests
{
    [Fact]
    public void WhatIsNotSoundGoesBackToItsDefaultAndTheRestStays()
    {
        var store = Stored("""
            {
              "racer": {"name": null, "machine": 99, "color": null, "setting": 1000, "stockColor": 9,
                        "looks": [null, {"machine": 3, "stockColor": 2, "setting": 40}, {"machine": 41},
                                  {"machine": 5, "stockColor": 7, "color": {"r": 1, "g": 2, "b": 3}, "setting": -1},
                                  {"machine": 8, "stockColor": null, "color": null},
                                  {"machine": 3, "stockColor": 1, "setting": 60}]},
              "discPath": "C:/games/gx.iso",
              "replayFolder": "C:/replays\u0000/x",
              "server": "http://example.com/ws",
              "otherSounds": 7,
              "replayView": -1,
              "recordReplays": false,
              "recomp": {"runner": "C:/game/run.exe", "module": "a\nb"},
              "host": {"name": null, "racers": 500, "laps": 0, "courseSelect": 9, "fixedCourse": 2, "cpuFill": -3,
                       "cpuLevel": 4, "allowedMachines": [3, 3, -1, 41, 7], "machineSetting": 101, "votingMinimum": 0,
                       "password": "pw", "controlPort": 70000, "relayPort": 0, "pingLimitMs": 5}
            }
            """);

        var s = store.Load();

        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        // the server's address was among what could not be read: said with the rest (NoServerTests)
        Assert.Equal(SettingsStore.RepairedNotice + " · " + SettingsStore.ServerLostNotice, store.Notice);
        Assert.True(File.Exists(store.DamagedPath));
        var standard = RacerProfile.Default;
        Assert.Equal(standard.Name, s.Racer.Name);
        Assert.Equal(standard.Machine, s.Racer.Machine);
        Assert.Equal(standard.Color, s.Racer.Color);
        Assert.Equal(100, s.Racer.Setting);
        Assert.Equal(0, s.Racer.StockColor);
        // one look for a machine (the last), each of a machine there is, in a colour there is
        Assert.Equal(
            new[] { new MachineLook(5, null, new Rgb(1, 2, 3), 100), new MachineLook(8, 0, null, 100), new MachineLook(3, 1, null, 60) },
            s.Racer.Looks);
        Assert.Equal("C:/games/gx.iso", s.DiscPath);
        Assert.Null(s.ReplayFolder);
        Assert.Null(s.Server);
        Assert.True(s.NoServer);
        Assert.Equal(OtherSounds.Quieter, s.OtherSounds);
        Assert.False(s.RecordReplays);
        Assert.Equal(new RecompSettings(Runner: "C:/game/run.exe"), s.Recomp);
        // sound now: checking it again changes nothing
        Assert.Equal((s, false), SettingsCheck.Sound(s));
    }

    [Theory]
    [InlineData("{\"racer\": null}")]
    [InlineData("{\"racer\": {\"name\": \"\", \"machine\": 1}}")]
    [InlineData("{\"racer\": {\"name\": \"!!!\", \"machine\": 1}}")]
    [InlineData("{\"racer\": {\"name\": \"A\", \"machine\": -1}}")]
    public void ANullWhereThePagesReadAValueIsADefault(string text)
    {
        var store = Stored(text);

        var s = store.Load();

        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        Assert.NotNull(s.Racer);
        Assert.NotNull(s.Racer.Color);
        Assert.True(PlayerNames.Shown(s.Racer.Name).Length > 0);
        Assert.InRange(s.Racer.Machine, 0, 29);
        Assert.Equal((s, false), SettingsCheck.Sound(s));
    }

    /// <summary>The review's case: a choice this launcher does not know (a newer launcher's, a slip of the hand)
    /// made the whole file unreadable, so every setting went back to its default and the server was lost. It is
    /// that one setting's default now, said as any other repair; a field it does not know is passed over as
    /// before.</summary>
    [Theory]
    [InlineData("\"otherSounds\": \"muted\"")]
    [InlineData("\"otherSounds\": \"loudest\", \"futureField\": {\"a\": 1}")]
    [InlineData("\"otherSounds\": null")]
    [InlineData("\"otherSounds\": true")]
    [InlineData("\"otherSounds\": {\"level\": 3}")]
    [InlineData("\"otherSounds\": 1e99")]
    [InlineData("\"settingsTab\": \"cinema\"")]
    [InlineData("\"settingsTab\": [\"game\"]")]
    public void AChoiceThisLauncherDoesNotKnowIsThatOneSettingsDefault(string odd)
    {
        var store = Stored("{\"racer\": {\"name\": \"ANA\", \"machine\": 3, \"color\": {\"r\": 1, \"g\": 2, \"b\": 3}}, "
            + "\"server\": \"ws://10.0.0.5:47810/ws\", \"recordReplays\": false, " + odd + "}");

        var s = store.Load();

        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        Assert.Equal(SettingsStore.RepairedNotice, store.Notice);
        var standard = new LauncherSettings();
        Assert.Equal(standard with
        {
            Racer = new RacerProfile("ANA", 3, new Rgb(1, 2, 3)), Server = "ws://10.0.0.5:47810/ws", RecordReplays = false,
        }, s);
        // and the known choices are read and written by their names as before
        store.Save(s with { OtherSounds = OtherSounds.Off });
        Assert.Contains("\"otherSounds\": \"off\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        var again = Stored(File.ReadAllText(SettingsFile).Replace("\"off\"", "\"OFF\"", StringComparison.Ordinal)).Load();
        Assert.Equal(OtherSounds.Off, again.OtherSounds);
    }

    /// <summary>A file of a launcher from before 0.3.0 keeps the Replays page's old choice ("replayView"): every
    /// replay plays as the race now, and the field is passed over with nothing said.</summary>
    [Fact]
    public void TheRetiredReplayViewIsPassedOver()
    {
        var store = Stored("{\"otherSounds\": \"off\", \"replayView\": \"replay\"}");
        var s = store.Load();
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal(OtherSounds.Off, s.OtherSounds);
    }

    /// <summary>Older settings files may carry the host's rules of a launcher that hosted servers ("host"). Such
    /// a file loads as any sound file does: nothing is said, no copy is made, every other setting is
    /// kept, whatever the host part holds; and the launcher's next save leaves the part out.</summary>
    [Theory]
    [InlineData("""{"name": "Friday", "racers": 12, "laps": 5, "courseSelect": "fixed", "fixedCourse": 36, "cpuFill": "never", "cpuLevel": 0, "boostFromStart": true, "restore": false, "allowedMachines": [7, 3], "machineSetting": null, "votingMinimum": 2, "announce": true, "password": "pw", "controlPort": 47810, "relayPort": 47811, "pingLimitMs": 150}""")]
    [InlineData("null")]
    [InlineData("""{"name": "   ", "racers": 500, "courseSelect": "tournament", "controlPort": 70000}""")]
    [InlineData("""{"allowedMachines": [1e99]}""")]
    [InlineData("\"yes\"")]
    public void AnOlderFilesHostSettingsArePassedOverAndTheRestIsKept(string host)
    {
        var store = Stored("{\"racer\": {\"name\": \"ANA\", \"machine\": 3, \"color\": {\"r\": 1, \"g\": 2, \"b\": 3}}, "
            + "\"server\": \"ws://10.0.0.5:47810/ws\", \"recordReplays\": false, \"host\": " + host + ", \"otherSounds\": \"off\"}");

        var s = store.Load();

        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Null(store.Notice);
        Assert.False(File.Exists(store.DamagedPath));
        Assert.Equal(new LauncherSettings
        {
            Racer = new RacerProfile("ANA", 3, new Rgb(1, 2, 3)), Server = "ws://10.0.0.5:47810/ws", RecordReplays = false,
            OtherSounds = OtherSounds.Off,
        }, s);
        store.Save(s);
        Assert.DoesNotContain("\"host\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        Assert.Equal(s, new SettingsStore(SettingsFile).Load());
    }

    [Fact]
    public void TextsLongerThanATextBoxHoldsAreNotKept()
    {
        var path = new string('p', SettingsCheck.MostPath + 1);
        var (s, repaired) = SettingsCheck.Sound(new LauncherSettings
        {
            Racer = RacerProfile.Default with { Name = new string('N', SettingsCheck.MostName + 1) },
            DiscPath = path,
            UserFolder = path,
            ReplayFolder = path,
            Server = "ws://" + new string('h', SettingsCheck.MostServer) + "/ws",
            Recomp = new RecompSettings(path, path, path, path, path),
        });

        Assert.True(repaired);
        Assert.Equal(new LauncherSettings { Recomp = new RecompSettings(), NoServer = true }, s);
    }

    [Fact]
    public void SoundSettingsAreReadAsStoredAndNothingIsSaid()
    {
        var store = new SettingsStore(SettingsFile);
        Assert.Equal(new LauncherSettings(), store.Load());   // no file yet
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        var settings = new LauncherSettings
        {
            Racer = new RacerProfile("ANA", 29, new Rgb(1, 2, 3), 0, null, [new MachineLook(29, null, new Rgb(1, 2, 3), 0), new MachineLook(0, 3)]),
            DiscPath = "C:\\Games\\F-Zero GX (USA).iso",
            ReplayFolder = new string('r', SettingsCheck.MostPath),
            Server = "wss://example.com:8443/ws",
            OtherSounds = OtherSounds.Off,
            Recomp = new RecompSettings("run.exe"),
        };
        store.Save(settings);

        var read = store.Load();

        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Null(store.Notice);
        Assert.False(File.Exists(store.DamagedPath));
        Assert.Equal(settings, read);
    }

    /// <summary>Every machine of the game is a racer's machine and a look's: Dark Schneider (30) and the ten
    /// from the arcade game (31 to 40) with the thirty. A file of a launcher that knew thirty reads as it did.</summary>
    [Fact]
    public void All41MachinesAreKeptAndAnOlderFileReadsAsItDid()
    {
        var store = new SettingsStore(SettingsFile);
        var settings = new LauncherSettings
        {
            Racer = new RacerProfile("ANA", 40, new Rgb(1, 2, 3), 100, 0, [new MachineLook(30, 2), new MachineLook(40, 1, null, 60)]),
        };
        store.Save(settings);

        var read = store.Load();

        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal(settings.Racer, read.Racer);

        Assert.Equal(MachineSort.ByNumber, read.GarageSort);

        File.WriteAllText(SettingsFile, """{"racer": {"name": "BEN", "machine": 29, "color": {"r": 1, "g": 2, "b": 3}, "looks": [{"machine": 6, "stockColor": 1}]}, "host": {"allowedMachines": [5, 6]}}""");
        var older = store.Load();
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal((29, new MachineLook(6, 1)), (older.Racer.Machine, older.Racer.LookOf(6)));
        Assert.Equal(MachineSort.ByNumber, older.GarageSort);          // a file from before the Garage had an order
    }

    /// <summary>The marked machines, the heart's filter, the launcher's own sounds and the game's volume are kept;
    /// a file from before them reads as it did, with their defaults; what is not sound goes back to its default and
    /// the rest of the file stays.</summary>
    [Fact]
    public void FavouritesAndVolumesAreKeptAndAnOlderFileReadsAsItDid()
    {
        var store = new SettingsStore(SettingsFile);
        File.WriteAllText(SettingsFile, """{"recordReplays": false, "racer": {"name": "BEN", "machine": 29, "color": {"r": 1, "g": 2, "b": 3}}}""");
        var older = store.Load();
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal((null, false, 50, false, 100),
            (older.FavouriteMachines, older.FavouritesOnly, older.SoundVolume, older.SoundMuted, older.GameVolume));

        store.Save(older with { FavouriteMachines = [6, 40, 0], FavouritesOnly = true, SoundVolume = 20, SoundMuted = true, GameVolume = 35 });
        var text = File.ReadAllText(SettingsFile);
        Assert.Contains("\"gameVolume\": 35", text, StringComparison.Ordinal);
        Assert.Contains("\"soundMuted\": true", text, StringComparison.Ordinal);
        var read = store.Load();
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal([6, 40, 0], read.FavouriteMachines!);
        Assert.Equal((true, 20, true, 35, "BEN"), (read.FavouritesOnly, read.SoundVolume, read.SoundMuted, read.GameVolume, read.Racer.Name));

        File.WriteAllText(SettingsFile, """{"recordReplays": false, "favouriteMachines": [3, 41, 3, -1, 7], "soundVolume": 101, "gameVolume": -5}""");
        var mended = store.Load();
        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        Assert.Equal([3, 7], mended.FavouriteMachines!);
        Assert.Equal((50, 100, false), (mended.SoundVolume, mended.GameVolume, mended.RecordReplays));
    }

    /// <summary>The Garage's order is kept by the stat's name; a stat this launcher does not know, or no order at
    /// all, is the game's order, and the rest of the file stays.</summary>
    [Fact]
    public void TheGaragesOrderIsKeptAndOneThatIsNoneIsByNumber()
    {
        var store = new SettingsStore(SettingsFile);
        store.Save(new LauncherSettings { GarageSort = new MachineSort(MachineStat.BoostStrength, Descending: true) });
        Assert.Contains("\"by\": \"boostStrength\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        Assert.Equal(new MachineSort(MachineStat.BoostStrength, Descending: true), store.Load().GarageSort);
        Assert.Equal(SettingsRead.Fine, store.LastRead);

        foreach (var order in new[] { """{"by": "speediness", "descending": true}""", """{"by": 77}""", "null" })
        {
            File.WriteAllText(SettingsFile, $$"""{"recordReplays": false, "garageSort": {{order}}}""");
            var read = store.Load();
            Assert.Equal(MachineSort.ByNumber, read.GarageSort);
            Assert.False(read.RecordReplays);
            Assert.Equal(SettingsRead.Repaired, store.LastRead);
        }
    }

    [Fact]
    public void ASettingsFileThatCannotBeWrittenIsNotAnError()
    {
        // the data folder's place is taken by a file, and the settings' own place by a folder
        var taken = Path.Combine(_dir, "taken");
        File.WriteAllText(taken, "");
        Assert.False(new SettingsStore(Path.Combine(taken, "settings.json")).TrySave(new LauncherSettings()));
        Directory.CreateDirectory(SettingsFile);
        var store = new SettingsStore(SettingsFile);
        Assert.Equal(new LauncherSettings(), store.Load());
        Assert.False(store.TrySave(new LauncherSettings()));
    }

    /// <summary>The paths in the settings are typed into the Settings page's boxes: one that is no path, or a
    /// place that cannot be read or made, is no error for the pages that use it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    [InlineData("a\0b.iso")]
    [InlineData("no/such/folder/gx.iso")]
    public void APathThatIsNoneIsNoError(string path)
    {
        Assert.NotEqual(DiscStatus.Ok, DiscCheck.Check(path));
        Assert.Empty(ReplayFile.List(path));
        Assert.False(Replays.TryReveal(Path.Combine(path, "\0")));
    }

    [Fact]
    public void AReplayFolderThatIsNotThePlayersToReadHasNoReplays()
    {
        if (OperatingSystem.IsWindows())
        {
            return;   // a folder's rights are taken away here the Unix way
        }
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "closed")).FullName;
        File.WriteAllText(Path.Combine(folder, "race.fzrp"), "");
        File.SetUnixFileMode(folder, UnixFileMode.None);
        try
        {
            Assert.Empty(ReplayFile.List(folder));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void AFolderThatCannotBeMadeIsNotOpened()
    {
        var taken = Path.Combine(_dir, "taken");
        File.WriteAllText(taken, "");

        Assert.False(DolphinTools.TryOpenFolder(Path.Combine(taken, "replays")));
        Assert.False(DolphinTools.TryOpenFolder("a\0b"));
        Assert.False(DolphinTools.TryOpenFolder(null));
        Assert.False(DolphinTools.TryOpenFolder("  "));
        Assert.Empty(ReplayFile.List(taken));
    }

    [Theory]
    [InlineData("ws://127.0.0.1:47810/ws", true)]
    [InlineData("wss://example.com/ws", true)]
    [InlineData("  ws://192.168.1.20:47810/ws ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("example.com", false)]
    [InlineData("http://example.com/ws", false)]
    [InlineData("https://example.com/ws", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("ws://", false)]
    [InlineData("ws:///ws", false)]
    [InlineData("ws://exa\nmple.com/ws", false)]
    public void OnlyAWebSocketAddressIsAServer(string? text, bool server) =>
        Assert.Equal(server, SettingsCheck.IsServer(text));

    /// <summary>A sound file with bytes of it changed, cut off or doubled, some thousands of ways: every one loads
    /// without an error, and what loads is sound.</summary>
    [Fact]
    public void ADamagedFileNeverThrowsAndAlwaysGivesSoundSettings()
    {
        var store = new SettingsStore(SettingsFile);
        store.Save(new LauncherSettings
        {
            Racer = new RacerProfile("ANA", 6, new Rgb(1, 2, 3), 100, 0, [new MachineLook(6, 2), new MachineLook(1, null, new Rgb(9, 9, 9), 30)]),
            DiscPath = "C:/games/gx.iso",
            Server = "ws://192.168.1.20:47810/ws",
            Recomp = new RecompSettings("run.exe"),
        });
        var sound = File.ReadAllBytes(SettingsFile);
        var random = new Random(20261006);
        byte[] swaps = [.. "{}[]\":,0123456789-ntfe \\"u8];
        for (var round = 0; round < 4000; round++)
        {
            var bytes = sound.ToList();
            for (var change = random.Next(1, 6); change > 0; change--)
            {
                var at = random.Next(bytes.Count);
                switch (random.Next(5))
                {
                    case 0: bytes[at] = swaps[random.Next(swaps.Length)]; break;
                    case 1: bytes.RemoveAt(at); break;
                    case 2: bytes.Insert(at, swaps[random.Next(swaps.Length)]); break;
                    case 3: bytes.RemoveRange(at, bytes.Count - at); break;
                    default: bytes.InsertRange(at, Encoding.UTF8.GetBytes("99999999999")); break;
                }
                if (bytes.Count == 0)
                {
                    break;
                }
            }
            File.WriteAllBytes(SettingsFile, [.. bytes]);

            var read = store.Load();

            Assert.Equal((read, false), SettingsCheck.Sound(read));
        }
    }
}
