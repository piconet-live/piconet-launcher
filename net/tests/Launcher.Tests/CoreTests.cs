using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

public sealed class NameTests
{
    [Theory]
    [InlineData("Tom & Jerry", "TOM & JERRY")]
    [InlineData("xX_Speedy_Xx", "XXSPEEDYXX")]
    [InlineData("  spaced   out  ", "SPACED OUT")]
    [InlineData("Ünïcode Ñame", "NCODE AME")]
    [InlineData("A very long player name here", "A VERY LONG PLAY")]
    [InlineData("Dr. Stewart-2", "DR. STEWART-2")]
    public void TheShownNameIsWhatTheGameDraws(string typed, string shown) => Assert.Equal(shown, PlayerNames.Shown(typed));

    [Theory]
    [InlineData("ANA", true)]
    [InlineData("Tom & Jerry", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData("ABCDEFGHIJKLMNOPQ", false)]
    public void TheCoordinatorAcceptsOnlyWhatTheGameDraws(string name, bool valid) =>
        Assert.Equal(valid, PlayerNames.IsValid(name));
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SettingsRoundTrip()
    {
        var store = new SettingsStore(Path.Combine(_dir, "sub", "settings.json"));
        var settings = new LauncherSettings
        {
            Racer = new RacerProfile("ANA", 7, new Rgb(1, 2, 3)),
            DiscPath = "C:/games/gx.iso",
            OtherSounds = OtherSounds.Off,
            RecordReplays = false,
        };
        store.Save(settings);
        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void MissingOrBrokenSettingsGiveTheDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(new LauncherSettings(), new SettingsStore(path).Load());
        File.WriteAllText(path, "{ not json");
        // the defaults, with no server in place of the defaults' official one (NoServerTests)
        Assert.Equal(new LauncherSettings { NoServer = true }, new SettingsStore(path).Load());
        Assert.True(new LauncherSettings().RecordReplays);              // record by default
        Assert.Equal(OtherSounds.Quieter, new LauncherSettings().OtherSounds);
    }

    [Fact]
    public void EachMachineKeepsItsOwnLookAndTheyRoundTrip()
    {
        var racer = new RacerProfile("ANA", 6, new Rgb(1, 2, 3), Setting: 80, StockColor: null);
        Assert.Equal(new MachineLook(6, null, new Rgb(1, 2, 3), 80), racer.LookOf(6));   // saved before per-machine looks
        Assert.Equal(new MachineLook(7, Setting: 80), racer.LookOf(7));                  // never set up: its own colour
        racer = racer.WithLook(new MachineLook(7, StockColor: 2, Setting: 30)).WithLook(new MachineLook(6, 1));
        Assert.Equal(new MachineLook(7, 2, null, 30), racer.LookOf(7));
        Assert.Equal(new MachineLook(6, 1), racer.LookOf(6));
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new LauncherSettings { Racer = racer });
        Assert.Equal(racer, store.Load().Racer);
    }

    [Fact]
    public void ChoosingAnotherMachineKeepsEveryMachinesLook()
    {
        // the older single look (machine 6, painted) stays with machine 6; machine 7 keeps its garage look
        var racer = new RacerProfile("ANA", 6, new Rgb(1, 2, 3), Setting: 80, StockColor: null)
            .WithLook(new MachineLook(7, StockColor: 2, Setting: 30));
        var moved = racer.WithMachine(7);
        Assert.Equal(7, moved.Machine);
        Assert.Equal(new MachineLook(6, null, new Rgb(1, 2, 3), 80), moved.LookOf(6));
        Assert.Equal(new MachineLook(7, 2, null, 30), moved.LookOf(7));
        var again = moved.WithMachine(9);                       // never set up: its own colour, still after the move
        Assert.Equal(new MachineLook(9, Setting: 80), again.LookOf(9));
        Assert.Equal(new MachineLook(7, 2, null, 30), again.LookOf(7));
        Assert.Equal(again.LookOf(6), racer.LookOf(6));
        Assert.Same(racer, racer.WithMachine(6));
    }

    [Fact]
    public void TheServerIsBlankByDefaultAndMeansTheOfficialOne()
    {
        Assert.Null(new LauncherSettings().Server);
        Assert.Equal("wss://piconet-coordinator.fly.dev/ws", new LauncherSettings().QuickPlayServer);
        Assert.Equal("wss://piconet-coordinator.fly.dev/ws", new LauncherSettings { Server = "  " }.QuickPlayServer);
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new LauncherSettings { RecordReplays = false });
        Assert.DoesNotContain("quickPlayServer", File.ReadAllText(store.Path), StringComparison.Ordinal);
        // the official address, saved by launchers that stored the default, loads as blank
        store.Save(new LauncherSettings { Server = "wss://piconet-coordinator.fly.dev/ws", RecordReplays = false });
        Assert.Equal(new LauncherSettings { RecordReplays = false }, store.Load());
        store.Save(new LauncherSettings { Server = "ws://192.168.1.20:47810/ws" });      // another coordinator stays
        Assert.Equal("ws://192.168.1.20:47810/ws", store.Load().QuickPlayServer);
    }

    /// <summary>The default from before the service went online was this computer's own address, and a file that
    /// still holds it was read as "the official server": a local address became the live service without a word.
    /// It is a server of the player's own now, as the file says (the dot says offline until they change it).</summary>
    [Fact]
    public void TheOldLocalDefaultServerIsNotTheOfficialOne()
    {
        const string old = "ws://127.0.0.1:47810/ws";
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, $$"""{"server": "{{old}}", "recordReplays": false}""");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Null(store.Notice);
        Assert.Equal(old, loaded.Server);                                      // what the Server box shows
        Assert.Equal(old, loaded.QuickPlayServer);
        Assert.NotEqual(LauncherSettings.OnlineServer, loaded.QuickPlayServer);
        Assert.False(loaded.NoServer);
        Assert.Equal(new Uri("http://127.0.0.1:47810/health"), ServerHealth.HealthUri(loaded.QuickPlayServer!));   // whom the dot asks
        store.Save(loaded);
        Assert.Equal(old, new SettingsStore(path).Load().QuickPlayServer);
    }
}

public sealed class DiscTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A made-up disc image with this game id (<see cref="SyntheticDisc"/>): the disc is told by what a
    /// GameCube image holds, not by its name and first six bytes.</summary>
    private string Image(string name, string id) => new SyntheticDisc { Id = id }.Write(_dir, name);

    private string Bytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void OnlyTheUsDiscIsAccepted()
    {
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(Image("gx.iso", "GFZE01")));
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(Image("gx.gcm", "GFZE01")));
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(Image("gx.bin", "GFZE01")));       // whatever the file is called
        Assert.Null(DiscCheck.Problem(Path.Combine(_dir, "gx.iso")));
        Assert.Equal(DiscStatus.WrongGame, DiscCheck.Check(Image("jp.iso", "GFZJ01")));
        Assert.Equal("This is the game's disc for Japan (GFZJ01). The launcher needs F-Zero GX (USA), GFZE01.", DiscCheck.Problem(Path.Combine(_dir, "jp.iso")));
        Assert.Equal(DiscStatus.WrongGame, DiscCheck.Check(new SyntheticDisc { Revision = 1 }.Write(_dir, "revised.iso")));
        // a compressed image by its first bytes, and a file that has the game's six letters and nothing of a disc
        Assert.Equal(DiscStatus.Unsupported, DiscCheck.Check(Bytes("gx.rvz.iso", [.. "RVZ\u0001"u8, .. new byte[5000]])));
        Assert.Equal(DiscStatus.Unsupported, DiscCheck.Check(Bytes("six.iso", [.. "GFZE01"u8, .. new byte[64]])));
        Assert.Equal(DiscStatus.Unsupported, DiscCheck.Check(Bytes("cut.iso", new SyntheticDisc().Build()[..0x300])));
        Assert.Equal(DiscStatus.Missing, DiscCheck.Check(Path.Combine(_dir, "none.iso")));
        Assert.Equal(DiscStatus.Missing, DiscCheck.Check(null));
    }
}

public sealed class RaceTests
{
    private static MatchStart Start() => new("m7", new RelayEndpoint("lan", "", 47811), "abc123", 1,
        new RaceSetup(21, 2,
            [new SeatSetup(0, "p1", "ANA", 1, new Rgb(165, 33, 0)), new SeatSetup(1, "p2", "BEN", 0, new Rgb(255, 102, 0)),
             new SeatSetup(2, null, "CPU", 3, null)],
            0x1234, 0x5678));

    [Fact]
    public void TheMatchFileCarriesTheMatchAndThisPlayersOptions()
    {
        var file = JsonNode.Parse(MatchFile.Build(Start(), new RaceOptions("10.0.0.5", OtherSounds.Off)))!;
        Assert.Equal("m7", (string)file["match"]!["matchId"]!);
        Assert.Equal(1, (int)file["match"]!["seat"]!);
        Assert.Equal("abc123", (string)file["match"]!["token"]!);
        Assert.Equal(255, (int)file["match"]!["setup"]!["seats"]![1]!["color"]!["r"]!);
        Assert.Null(file["match"]!["setup"]!["seats"]![2]!["color"]);
        Assert.Equal("10.0.0.5", (string)file["relayHost"]!);
        Assert.Equal(2, (int)file["delay"]!);
        Assert.Equal(6.0, (double)file["overclock"]!);
        Assert.Equal("off", (string)file["otherSounds"]!);
    }

    [Fact]
    public void AReplayIsNamedByItsTimeAndMatch() =>
        Assert.Equal("20261003-201500-m7.fzrp", RecompLaunch.ReplayFileName(new DateTimeOffset(2026, 10, 3, 20, 15, 0, TimeSpan.Zero), "m7"));
}

public sealed class LanTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void ServersAppearWhenHeardAndDropOffWhenSilent()
    {
        var clock = new Clock();
        using var browser = new LanBrowser(clock, port: 0);
        var changes = 0;
        browser.Changed += () => changes++;
        browser.Heard(new LanAnnouncement(LanAnnouncement.ServiceName, "Den", 47810, ControlProtocol.Version), IPAddress.Parse("192.168.1.20"));
        var s = Assert.Single(browser.Servers);
        Assert.Equal("Den", s.Name);
        Assert.Equal(new Uri("ws://192.168.1.20:47810/ws"), s.Coordinator);
        Assert.Equal(1, changes);
        clock.Now += LanBrowser.Expiry;
        Assert.Empty(browser.Servers);
    }

    [Fact]
    public void AuroraKeysAreLeftOutAtTheirDefaultsAndWhenNotOffered()
    {
        var chosen = new LauncherSettings { PictureEngine = "aurora", AuroraSize = "3", AuroraEdges = 4, AuroraTextures = 16, AuroraColourblind = "tritanopia", AuroraHighRefresh = 240, RayTracing = true };
        Assert.Equal(("aurora", "3", 4, 16, "tritanopia", 240, true), chosen.AuroraLaunchKeys(offered: true));
        // a build that does not offer it, and the default engine: nothing a runner from before the keys would refuse
        Assert.Equal((null, null, 0, 0, null, 0, false), chosen.AuroraLaunchKeys(offered: false));
        Assert.Equal((null, null, 0, 0, null, 0, false), new LauncherSettings().AuroraLaunchKeys(offered: true));
        Assert.Equal(("aurora", null, 0, 0, null, 0, false), new LauncherSettings { PictureEngine = "aurora" }.AuroraLaunchKeys(offered: true));
        // an older file's "native" is Auto now: the key left out, the same picture
        Assert.Equal(("aurora", null, 0, 0, null, 0, false), new LauncherSettings { PictureEngine = "aurora", AuroraSize = "native" }.AuroraLaunchKeys(offered: true));
        // a file edited by hand never stops the game from starting
        var edited = chosen with { AuroraSize = "9", AuroraEdges = 3, AuroraTextures = 99, AuroraColourblind = "green", AuroraHighRefresh = 77, RayTracing = false };
        Assert.Equal(("aurora", null, 0, 0, null, 0, false), edited.AuroraLaunchKeys(offered: true));
    }

    [Fact]
    public void RayTracingIsOffByDefaultAndWrittenOnlyOnACardThatCan()
    {
        Assert.False(new LauncherSettings().RayTracing);
        var on = new LauncherSettings { PictureEngine = "aurora", RayTracing = true };
        Assert.True(on.AuroraLaunchKeys(offered: true, cardRayTraces: true).RayTracing);
        // a card the check found cannot: left out, whatever the file says
        Assert.False(on.AuroraLaunchKeys(offered: true, cardRayTraces: false).RayTracing);
        // with the standard engine, rays are no setting at all
        Assert.False((on with { PictureEngine = "dolphin" }).AuroraLaunchKeys(offered: true).RayTracing);
    }

    [Fact]
    public void RayTracingIsKeptInTheSettingsFile()
    {
        var dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
        try
        {
            var data = Path.Combine(dir, "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{\"noServer\": true}");   // (a file from before the setting)
            var state = Launcher.App.AppState.Load(data);
            try
            {
                Assert.False(state.Settings.RayTracing);
                state.Update(s => s with { RayTracing = true });
            }
            finally
            {
                state.Shutdown();
            }
            Assert.Contains("\"rayTracing\": true", File.ReadAllText(Path.Combine(data, "settings.json")), StringComparison.Ordinal);
            var again = Launcher.App.AppState.Load(data);
            Assert.True(again.Settings.RayTracing);   // kept
            again.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ARayTracingCardIsOneWithDirectX12RayTracingTierOnePointOne()
    {
        Assert.Equal(RayTracingCard.Yes, RayTracingCard.FromTier(11));
        Assert.True(RayTracingCard.FromTier(12).Can);
        // DXR 1.0 has no inline ray queries, which Aurora's shaders use; none; no Direct3D 12 device at all
        foreach (var tier in new[] { 10, 0, -1 })
        {
            Assert.Equal(new RayTracingCard(false, RayTracingCard.NoCard), RayTracingCard.FromTier(tier));
        }
        Assert.Null(RayTracingCard.Yes.Reason);
    }

    [Fact]
    public async Task TheCardIsCheckedOnceAndNotWindowsCannot()
    {
        var first = await RayTracingCard.Check();
        Assert.Same(RayTracingCard.Check(), RayTracingCard.Check());
        Assert.Same(first, RayTracingCard.Known);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(new RayTracingCard(false, RayTracingCard.NoWindows), first);
        }
        else
        {
            Assert.True(first.Can || first.Reason == RayTracingCard.NoCard);
        }
    }

    [Fact]
    public void HudKeysAreLeftOutWhenNotOffered()
    {
        // an older settings file with the modern HUD on: a build that does not offer it writes neither key
        var modern = new LauncherSettings { ModernHud = true, HudMeter = "rev" };
        Assert.Equal((true, "rev"), modern.HudLaunchKeys(offered: true));
        Assert.Equal((false, null), modern.HudLaunchKeys(offered: false));
        Assert.Equal((false, null), new LauncherSettings().HudLaunchKeys(offered: true));
    }
}
