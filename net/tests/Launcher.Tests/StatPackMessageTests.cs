using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A race's stat pack on its way through the launcher (handbook/stat-packs.md): it arrives in the
/// server's match start, is checked there (<see cref="ServerMessages"/>), and is handed to the game in the match
/// file as it came. A match start whose pack is none is no match start: the game would refuse the race. And the
/// name of a server's pack, which the lobby's state carries for a label.</summary>
public sealed class StatPackMessageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stat-pack-message-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string LobbyWith(string member) => FakeCoordinator.Lobby[..^1] + member + "}";

    /// <summary>A server with a stat pack names it in the lobby's state, and the session holds it:
    /// <c>QuickPlaySession.Lobby.StatPack.Name</c> is what a label shows (null <c>StatPack</c>: the original stats).
    /// A server without one, or one from before the member, leaves it null.</summary>
    [Fact]
    public async Task TheSessionsLobbyNamesTheServersStatPack()
    {
        var cancel = TestContext.Current.CancellationToken;
        var named = LobbyWith($$""","statPack":{"name":"GX Made Up 1.0","fingerprint":"{{Pack.Fingerprint}}"}""");
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, named);
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => { });
        await session.JoinAsync(server.Address, cancel: cancel);
        while (session.Lobby is null)
        {
            await Task.Delay(10, cancel);
        }

        var lobby = Assert.IsType<LobbyState>(session.Lobby);
        Assert.Equal(new StatPackName("GX Made Up 1.0", Pack.Fingerprint), lobby.StatPack);
        Assert.Null(Assert.IsType<LobbyState>(ServerMessages.Checked(FakeCoordinator.Lobby)).StatPack);
    }

    /// <summary>The pack's author stands beside its name on the lobby's line ("GX Unleashed · AKC12"): a server's
    /// word, shown only when it is a text an author can be; without one the label is the name, and a lobby on the
    /// original stats has no label.</summary>
    [Fact]
    public void TheLobbysLabelIsThePacksNameAndItsAuthor()
    {
        static LobbyState Read(string member) => Assert.IsType<LobbyState>(ServerMessages.Checked(LobbyWith(member)));
        var pack = $"\"name\":\"GX Made Up 1.0\",\"fingerprint\":\"{Pack.Fingerprint}\"";

        Assert.Equal("GX Made Up 1.0 · A Maker", LobbyScreens.PackLabel(Read($",\"statPack\":{{{pack},\"author\":\"A Maker\"}}").StatPack));
        Assert.Equal("GX Made Up 1.0", LobbyScreens.PackLabel(Read($",\"statPack\":{{{pack}}}").StatPack));
        Assert.Equal("", LobbyScreens.PackLabel(Read("").StatPack));
        foreach (var author in new[] { "\"\"", "\" x\"", "\"a name\\u202Ethat turns\"", "\"" + new string('n', 41) + "\"", "7" })
        {
            var read = ServerMessages.Checked(LobbyWith($",\"statPack\":{{{pack},\"author\":{author}}}"));
            // (an author that is no text at all is no lobby message; one that is no author's name is left out)
            Assert.True(read is null || LobbyScreens.PackLabel(Assert.IsType<LobbyState>(read).StatPack) == "GX Made Up 1.0");
        }
    }

    /// <summary>The server sends its pack whole after the welcome, for the Garage. The session holds it for a lobby
    /// whose state names that very pack by its fingerprint, and for no other: a lobby on the original stats, a
    /// lobby naming another pack, a pack this launcher does not take.</summary>
    [Fact]
    public async Task TheSessionHoldsTheServersPackForALobbyThatNamesIt()
    {
        var cancel = TestContext.Current.CancellationToken;
        string Values(StatPackWire wire) => ControlProtocol.Encode(new StatPackValues(wire));
        string Named(string print) => LobbyWith($$""","statPack":{"name":"Made up","fingerprint":"{{print}}","author":"A Maker"}""");
        async Task<StatPack?> Held(params string[] messages)
        {
            await using var server = new FakeCoordinator([FakeCoordinator.Welcome, .. messages]);
            await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => { });
            await session.JoinAsync(server.Address, cancel: cancel);
            while (session.Lobby is null)
            {
                await Task.Delay(10, cancel);
            }
            return session.LobbyStatPack;
        }

        var held = await Held(Values(Pack.ToWire()), Named(Pack.Fingerprint));
        Assert.Equal((Pack.Fingerprint, 2), (held!.Fingerprint, held.Entries.Count));
        Assert.Null(await Held(Values(Pack.ToWire()), FakeCoordinator.Lobby));                      // a lobby on the original stats
        Assert.Null(await Held(Values(Pack.ToWire()), Named(new string('0', 40))));                 // a lobby naming another pack
        Assert.Null(await Held(Named(Pack.Fingerprint)));                                           // no pack was sent
        Assert.Null(await Held(Values(Pack.ToWire() with { Values = "BgQAAAAB" }), Named(Pack.Fingerprint)));   // one this launcher does not take
        // the shape check: three texts, the values no longer than a whole car data's
        Assert.NotNull(ServerMessages.Checked(Values(Pack.ToWire())));
        Assert.Null(ServerMessages.Checked("""{"type":"statPack"}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"statPack","pack":{"name":"p","fingerprint":"x"}}"""));
        Assert.Null(ServerMessages.Checked(Values(Pack.ToWire() with { Values = new string('A', 40_841) })));
    }

    /// <summary>The name is a server's word that a label shows: only a name a pack can have, with a fingerprint that
    /// is one. Anything else is no pack named, and the rest of the lobby is taken as it came.</summary>
    [Theory]
    [InlineData(""","statPack":{"name":"","fingerprint":"8472faeb0e46573e5c279e752ecf65c50452d8ee"}""")]
    [InlineData(""","statPack":{"name":"a name\u202Ethat turns","fingerprint":"8472faeb0e46573e5c279e752ecf65c50452d8ee"}""")]
    [InlineData(""","statPack":{"name":"a name of more than forty characters, far more","fingerprint":"8472faeb0e46573e5c279e752ecf65c50452d8ee"}""")]
    [InlineData(""","statPack":{"name":"p","fingerprint":"8472FAEB0E46573E5C279E752ECF65C50452D8EE"}""")]
    [InlineData(""","statPack":{"name":"p","fingerprint":"8472"}""")]
    [InlineData(""","statPack":{"name":"p"}""")]
    [InlineData(""","statPack":{}""")]
    [InlineData(""","statPack":null""")]
    public void AStatPackNameThatIsNoneIsNotShown(string member)
    {
        var lobby = Assert.IsType<LobbyState>(ServerMessages.Checked(LobbyWith(member)));

        Assert.Null(lobby.StatPack);
        Assert.Equal(("l1", "ANA"), (lobby.LobbyId, lobby.Players[0].Name));
    }

    private static readonly StatPack Pack = StatPack.Of("Made up", [StatEntry.Of(6, 0x04, 2520), new(6, 0x48, 0x03010000)]).Pack!;

    private static string Start(StatPackWire? stats) => ControlProtocol.Encode(new MatchStart("m7", new RelayEndpoint("r", "127.0.0.1", 47811),
        "1f2e", 0, new RaceSetup(8, 3, [new SeatSetup(0, "p1", "ANA", 6, null)], 1, 2, Stats: stats)));

    [Fact]
    public void AMatchStartsPackReachesTheGameAsItCame()
    {
        var start = Assert.IsType<MatchStart>(ServerMessages.Checked(Start(Pack.ToWire())));

        Assert.Equal(Pack.ToWire(), start.Setup.Stats);
        var stats = JsonNode.Parse(MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Off)))!["match"]!["setup"]!["stats"]!;
        Assert.Equal((Pack.Name, Pack.Fingerprint, Pack.ToWire().Values),
            ((string?)stats["name"], (string?)stats["fingerprint"], (string?)stats["values"]));
        // and a race without one has no such member
        var plain = Assert.IsType<MatchStart>(ServerMessages.Checked(Start(null)));
        Assert.Null(plain.Setup.Stats);
        Assert.Null(JsonNode.Parse(MatchFile.Build(plain, new RaceOptions("127.0.0.1", OtherSounds.Off)))!["match"]!["setup"]!["stats"]);
    }

    /// <summary>A match start with a stat pack the game would refuse: no game is started, the player is told why in
    /// a sentence and is back in the lobby. A pack that is none, and one with a denormal or a value below its floor.</summary>
    [Theory]
    [InlineData("AAAAAA==", "The pack's values are 4 bytes, which is not whole entries of 6.")]
    [InlineData("BgQAAAAB", "machine 6 Weight is the bits 00000001, which is no number a pack may hold (a denormal, -0, an infinity or none at all).")]
    [InlineData("BhDCSAAA", "machine 6 Grip1 is -50, and must be at least -45.")]
    public async Task AMatchStartWithAStatPackThisLauncherDoesNotTakeStartsNoGameAndSaysWhy(string values, string why)
    {
        var cancel = TestContext.Current.CancellationToken;
#pragma warning disable CA5350   // the fingerprint is a name, not a guard
        var print = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([1, .. Convert.FromBase64String(values)]));
#pragma warning restore CA5350
        var start = Start(new StatPackWire("Made up", print, values)).Replace("\"matchId\":\"m7\"", "\"matchId\":\"bad\"", StringComparison.Ordinal);
        // (after the refusal the lobby goes on: its next state, with a second member, and a course vote)
        var next = FakeCoordinator.Lobby.Replace("\"players\":[", "\"players\":[{\"id\":\"p9\",\"name\":\"BEA\",\"machine\":3,\"color\":{\"r\":1,\"g\":2,\"b\":3}},", StringComparison.Ordinal);
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, start, next,
            """{"type":"courseVote","matchId":"m8","options":[1,2,3],"durationMs":15000}""");
        var (started, logged) = (0, new List<string>());
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => started++);
        session.Refused += logged.Add;
        await session.JoinAsync(server.Address, cancel: cancel);
        // what is waited for is what is asserted: the status, which is the problem's sentence the moment the problem
        // is set (one word written), and still is after the lobby's next state and the next vote have come
        var said = "Stat pack not taken · " + why;
        while (session.Status != said)
        {
            await Task.Delay(10, cancel);
        }
        Assert.Equal(QuickPlaySession.StatPackNotTaken + why, session.StatPackProblem);
        while (session.CourseVote is null)
        {
            Assert.Equal(said, session.Status);
            await Task.Delay(10, cancel);
        }

        Assert.Equal(2, session.Lobby!.Players.Count);
        Assert.Equal((said, said), (session.Status, session.StatPackProblem));
        Assert.Equal((0, null), (started, session.Race));
        Assert.Contains("carries a stat pack this launcher does not take", Assert.Single(logged), StringComparison.Ordinal);
        // out of the lobby the problem is gone, and the status is the queue's again
        await session.LeaveAsync();
        Assert.Equal((null, "Left the queue"), (session.StatPackProblem, session.Status));
    }

    /// <summary>A launcher takes every pack its game takes (a later pack must need a server
    /// change only): a match start whose pack holds a value beyond what has been tried, here a Grip1 of 9.5 and one
    /// of 45, the game's own most, starts the game with the pack in the match file as it came.</summary>
    [Theory]
    [InlineData("BhBBGAAA")]
    [InlineData("BhBCNAAA")]
    public async Task AMatchStartWithAPackTheGameTakesStartsTheGameWhateverHasBeenTried(string values)
    {
        var cancel = TestContext.Current.CancellationToken;
#pragma warning disable CA5350   // the fingerprint is a name, not a guard
        var print = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([1, .. Convert.FromBase64String(values)]));
#pragma warning restore CA5350
        var wire = new StatPackWire("Made up", print, values);
        Assert.Single(StatPack.FromWire(wire).Pack!.Untried);
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, Start(wire));
        var started = new List<string>();
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"),
            race => started.Add(File.ReadAllText(race.MatchFile)));
        await session.JoinAsync(server.Address, cancel: cancel);
        while (started.Count == 0)
        {
            await Task.Delay(10, cancel);
        }

        Assert.Null(session.StatPackProblem);
        var stats = JsonNode.Parse(Assert.Single(started))!["match"]!["setup"]!["stats"]!;
        Assert.Equal((print, values), ((string?)stats["fingerprint"], (string?)stats["values"]));
        Assert.Null(RecompLaunch.MatchFileProblem(session.Race!.MatchFile));   // and --play-match takes the same file
    }

    /// <summary>The shape check of a server's messages lets a match start through whatever its pack is: the session
    /// judges the pack, so that the player is told. One whose pack is no text at all is no message.</summary>
    [Fact]
    public void TheShapeCheckLeavesAMatchStartsStatPackToTheSession()
    {
        var wire = Pack.ToWire();
        Assert.NotNull(ServerMessages.Checked(Start(wire with { Fingerprint = new string('0', 40) })));
        Assert.NotNull(ServerMessages.Checked(Start(new StatPackWire(null!, null!, null!))));
        Assert.Null(ServerMessages.Checked(Start(wire).Replace("\"values\":\"", "\"values\":7,\"more\":\"", StringComparison.Ordinal)));
    }

    /// <summary>A match file given to the launcher by hand (<c>--play-match</c>) is held to the same rule: no game is
    /// started on a stat pack this launcher does not take.</summary>
    [Fact]
    public void AMatchFileWithAStatPackThisLauncherDoesNotTakeIsNotPlayed()
    {
        string File(string name, StatPackWire? stats)
        {
            var path = Path.Combine(_dir, name);
            System.IO.File.WriteAllText(path, MatchFile.Build(new MatchStart("m7", new RelayEndpoint("r", "127.0.0.1", 47811), "1f2e", 0,
                new RaceSetup(8, 3, [new SeatSetup(0, "p1", "ANA", 6, null)], 1, 2, Stats: stats)), new RaceOptions("127.0.0.1", OtherSounds.Off)));
            return path;
        }
        var wire = Pack.ToWire();

        Assert.Null(RecompLaunch.MatchFileProblem(File("good.json", wire)));
        Assert.Null(RecompLaunch.MatchFileProblem(File("none.json", null)));
        Assert.Equal("The match's stat pack is not one this launcher takes: machine 6 Weight is the bits 00000001, which is no number a pack may hold " +
                     "(a denormal, -0, an infinity or none at all).", RecompLaunch.MatchFileProblem(File("denormal.json", wire with { Values = "BgQAAAAB" })));
        Assert.StartsWith("The match's stat pack is not one this launcher takes: machine 6 Grip1 is -50, and must be at least -45.",
            RecompLaunch.MatchFileProblem(File("negative.json", wire with { Values = "BhDCSAAA" })), StringComparison.Ordinal);
        Assert.Equal("Match file missing", RecompLaunch.MatchFileProblem(Path.Combine(_dir, "nowhere.json")));
    }

    /// <summary>This launcher says in its hello that it hands a stat pack to its game; a hello without the word is
    /// the text it always was (a launcher from before stat packs sends that).</summary>
    [Fact]
    public void TheHelloSaysThisLauncherKnowsStatPacks()
    {
        Assert.Contains("\"statPacks\":true", ControlProtocol.Encode(new Hello("ANA", ControlProtocol.Version, StatPacks: true)), StringComparison.Ordinal);
        Assert.DoesNotContain("statPacks", ControlProtocol.Encode(new Hello("ANA", ControlProtocol.Version)), StringComparison.Ordinal);
        Assert.False(Assert.IsType<Hello>(ControlProtocol.DecodeClient("""{"type":"hello","name":"ANA","version":3}""")).StatPacks);
    }
}
