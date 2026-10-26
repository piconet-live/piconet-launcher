using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>What a coordinator sends is checked before the launcher keeps it (<see cref="ServerMessages"/>): a
/// coordinator passes on what players send, and the screens read a message's parts on the window's thread, where an
/// exception closes the launcher.</summary>
public sealed class SecurityMessageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task Until(Func<bool> met, CancellationToken cancel)
    {
        for (var i = 0; i < 500 && !met(); i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.True(met(), "the wait ran out");
    }

    private QuickPlaySession Session(string name = "ANA") =>
        new(new LauncherSettings { Racer = new RacerProfile(name, 3, new Rgb(200, 0, 0)), RecordReplays = false }, Path.Combine(_dir, name), _ => { });

    /// <summary>A racer's launcher reports the race for the lobby's waiting players, and the server passes the
    /// racers on as they come: a seat outside the race (the race page looks the seat up in the race's seats) or a
    /// racer that is null closed every waiting player's launcher.</summary>
    [Theory]
    [InlineData("""[{"seat":-1,"state":"racing"}]""", false)]
    [InlineData("""[{"seat":30,"state":"racing"}]""", false)]
    [InlineData("""[{"seat":2147483647,"state":"racing"}]""", false)]
    [InlineData("""[null]""", false)]
    [InlineData("""[{"seat":3}]""", false)]
    [InlineData("null", false)]
    [InlineData("""[{"seat":0,"state":"racing"},{"seat":29,"state":"out"}]""", true)]
    [InlineData("[]", true)]
    public void ALiveRaceIsShownOnlyWithRacersOfTheRace(string racers, bool shown)
    {
        var message = ServerMessages.Checked("""{"type":"liveRace","matchId":"m7","snapshot":{"frame":1,"localSeat":0,"racers":""" + racers + "}}");
        Assert.Equal(shown, message is LiveRace);
        if (message is LiveRace live)
        {
            _ = RaceStandings.Places(live.Snapshot.Racers);
        }
        Assert.Null(ServerMessages.Checked("""{"type":"liveRace","matchId":"m7"}"""));
        var many = string.Join(",", Enumerable.Repeat("""{"seat":0,"state":"racing"}""", 31));
        Assert.Null(ServerMessages.Checked("""{"type":"liveRace","matchId":"m7","snapshot":{"frame":1,"localSeat":0,"racers":[""" + many + "]}}"));
    }

    private static string Live(string racers) =>
        """{"type":"liveRace","matchId":"m7","snapshot":{"frame":1,"localSeat":0,"racers":""" + racers + "}}";

    /// <summary>Any racer asked to report the race can name one seat twice, and the server passes it on: the map
    /// kept its dots by seat, and the second one closed the launcher of every waiting player. A race has each seat
    /// once.</summary>
    [Theory]
    [InlineData("""[{"seat":4,"state":"racing"},{"seat":4,"state":"racing"}]""")]
    [InlineData("""[{"seat":0,"state":"racing"},{"seat":7,"state":"out"},{"seat":0,"state":"finished"}]""")]
    [InlineData("""[{"seat":29,"state":"out"},{"seat":29,"state":"out"}]""")]
    public void ALiveRaceThatNamesASeatTwiceIsNotShown(string racers)
    {
        Assert.Null(ServerMessages.Checked(Live(racers)));
        // the same check stands before the player's own game's race (its status board: GameBoard)
        Assert.False(ServerMessages.Sound(new RaceSnapshot(1, 0, System.Text.Json.JsonSerializer.Deserialize<List<RacerLive>>(racers, ControlProtocol.Json)!)));
    }

    /// <summary>JSON carries a number too large for the type as infinity: a racer's place, speed or energy that is
    /// no number reaches the map's camera and the page's figures.</summary>
    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    [InlineData("speed")]
    [InlineData("energy")]
    [InlineData("maxEnergy")]
    [InlineData("topSpeed")]
    [InlineData("distance")]
    public void ALiveRaceWithANumberThatIsNotFiniteIsNotShown(string field)
    {
        foreach (var value in new[] { "1e39", "-1e39", "1e999" })
        {
            Assert.Null(ServerMessages.Checked(Live($$"""[{"seat":0,"state":"racing"},{"seat":1,"state":"racing","{{field}}":{{value}}}]""")));
        }
        Assert.IsType<LiveRace>(ServerMessages.Checked(Live($$"""[{"seat":0,"state":"racing"},{"seat":1,"state":"racing","{{field}}":-12345.5}]""")));
    }

    [Fact]
    public void ARacerWhoseNumbersAreNotNumbersIsNotShown()
    {
        static RaceSnapshot With(Func<RacerLive, RacerLive> change) =>
            new(1, 0, [change(new RacerLive(0, 1, 0, 0, 0, null, 900, 80, 100, 0, 1200, RacerLive.Racing, 10, 20, 30, 4000))]);
        Assert.True(ServerMessages.Sound(With(r => r)));
        foreach (var no in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.False(ServerMessages.Sound(With(r => r with { X = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { Y = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { Z = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { Speed = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { Energy = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { MaxEnergy = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { TopSpeed = no })));
            Assert.False(ServerMessages.Sound(With(r => r with { Distance = no })));
        }
    }

    /// <summary>A course vote with nothing to vote for: the test door's script reads the option it presses.</summary>
    [Fact]
    public void ACourseVoteWithNoOptionIsNoVote()
    {
        Assert.Null(ServerMessages.Checked("""{"type":"courseVote","matchId":"m7","options":[],"durationMs":8000}"""));
        Assert.IsType<CourseVote>(ServerMessages.Checked("""{"type":"courseVote","matchId":"m7","options":[-1],"durationMs":8000}"""));
    }

    /// <summary>A coordinator names the relays to measure: "no address" (0.0.0.0, ::) is refused by the name
    /// lookup with an error of its own kind, which ended the receive loop; such a relay did not answer.</summary>
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public async Task ARelayAtNoAddressDidNotAnswer(string host)
    {
        var rtts = await RelayMeasure.MeasureAsync([new RelayEndpoint("r1", host, 47811)], r => r.Host, TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(rtts).RttMs);
    }

    /// <summary>A coordinator that leaves parts out (a LAN server is any computer on the network): the session
    /// keeps what can be shown, drops what cannot, and stays connected.</summary>
    [Fact]
    public async Task ACoordinatorsMessagesWithPartsMissingDoNotReachTheScreens()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(
            """{"type":"welcome","playerId":"p1","version":3,"rules":{"laps":3,"racers":30,"allowedMachines":[999,-1,5]}}""",
            """{"no":"type"}""",
            """{"type":"a message of the future"}""",
            "not JSON",
            """{"type":"lobby","players":[null,{"id":"p2"},{"id":"p1","name":"A‮B\u0007","machine":3,"color":{"r":1,"g":2,"b":3}}],"race":{"seats":[null]}}""",
            """{"type":"courseVote","matchId":"m7"}""",
            """{"type":"courseChosen","matchId":"m7","options":[1,2],"votes":[0,0],"winner":7,"course":1}""",
            """{"type":"chooseMachine","matchId":"m7","course":21,"machines":[-4,2,2,41,7]}""",
            """{"type":"matchStart","matchId":"m7","seat":0}""",
            """{"type":"matchStart","matchId":"m7","relay":{"id":"r","host":"","port":47811},"token":"1","seat":64,"setup":{"course":1,"laps":1,"seats":[{"seat":0,"name":"A","machine":1}]}}""",
            """{"type":"matchStart","matchId":"m7","relay":{"id":"r","host":"","port":47811},"token":"1","seat":0,"setup":{"course":1,"laps":1,"seats":[{"seat":-2,"name":"A","machine":1}]}}""",
            """{"type":"chat","text":"last"}""");
        var started = 0;
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => started++);
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.Chat.Count == 1, cancel);                    // every message before it was taken in
        Assert.NotNull(session.ConnectedTo);
        Assert.Equal([5], session.Rules!.AllowedMachines);
        Assert.NotNull(session.Rules.Race);
        // queued on 5, the one allowed machine; the machine stage then offers 2 and 7 only, and since
        // fix/custom-active a stage whose first machine is not the one the server was told sends that machine
        // (QuickPlaySession.Received: the raced machine is told unasked), so the session races 2
        Assert.Equal(2, session.Machine);
        var lobby = session.Lobby!;
        Assert.Equal(["", "AB"], lobby.Players.Select(p => p.Name));
        Assert.All(lobby.Players, p => Assert.NotNull(p.Color));
        Assert.Equal(LobbyState.Forming, lobby.Stage);
        Assert.Null(lobby.Race);
        Assert.Null(session.CourseVote);
        Assert.Null(session.CourseChosen);
        Assert.Equal([2, 7], session.Choice!.Choose.Machines);
        Assert.Equal(0, started);
        Assert.Null(session.Race);
        var chat = Assert.Single(session.Chat);
        Assert.Equal(("", "", "last"), (chat.Channel, chat.FromName, chat.Text));
    }

    /// <summary>A tag is shown after a name on the pages ("NAME #4F2A"): one the coordinator could not have given
    /// (another length, other characters, a way to make a name look like another's) is shown as none, wherever it
    /// comes: the welcome, a lobby's members, a chat line, the public status's racers.</summary>
    [Theory]
    [InlineData("4F2A", "4F2A")]
    [InlineData("4f2a", null)]
    [InlineData("4F2", null)]
    [InlineData("4F2AB", null)]
    [InlineData("4F0A", null)]
    [InlineData("4F\u202eA", null)]
    [InlineData(null, null)]
    public void ATagIsShownOnlyWhenItIsOne(string? sent, string? shown)
    {
        sent = sent?.Replace("\\u202e", "\u202e", StringComparison.Ordinal);
        var welcome = Assert.IsType<Welcome>(ServerMessages.Checked(new Welcome("p1", "s", 1, Tag: sent)));
        Assert.Equal(shown, welcome.Tag);
        var chat = Assert.IsType<ChatMessage>(ServerMessages.Checked(
            new ChatMessage(ControlProtocol.LobbyChannel, "p2", "BEN", "hi", DateTimeOffset.UnixEpoch, FromTag: sent)));
        Assert.Equal(shown, chat.FromTag);
        var lobby = Assert.IsType<LobbyState>(ServerMessages.Checked(
            new LobbyState("l", [new PlayerInfo("p2", "BEN", 1, new Rgb(1, 2, 3), Tag: sent)], 30, 2, false, 0, 0)));
        Assert.Equal(shown, Assert.Single(lobby.Players).Tag);
        var status = ServerMessages.Checked(new ServiceStatus("s", DateTimeOffset.UnixEpoch, 1,
            [new LobbyView("l", [new RacerView("BEN", 1, null, sent)], 30, false, 0)], [], [], []));
        Assert.Equal(shown, Assert.Single(Assert.Single(status!.Lobbies).Players).Tag);
    }

    private static string StartOf(string id) =>
        """{"type":"matchStart","matchId":"MATCH","relay":{"id":"r","host":"127.0.0.1","port":47811},"token":"1","seat":0,"setup":{"course":1,"laps":1,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}]}}"""
            .Replace("MATCH", id, StringComparison.Ordinal);

    /// <summary>The review's case: a match start is a coordinator's word that starts a program on the player's
    /// computer, and a LAN server is anyone's computer. Twenty-five of them to a player in no lobby asked for the
    /// game twenty-five times. None is acted on outside a lobby, and in a lobby one race is on at a time; the
    /// launcher's log is told once a connection.</summary>
    [Theory]
    [InlineData(false, 0, 25)]
    [InlineData(true, 1, 24)]
    public async Task ACoordinatorCannotStartTheGameOutsideALobbyOrTwiceAtOnce(bool inLobby, int started, int refused)
    {
        var cancel = TestContext.Current.CancellationToken;
        var messages = new List<string> { FakeCoordinator.Welcome };
        if (inLobby)
        {
            messages.Add(FakeCoordinator.Lobby);
        }
        messages.AddRange(Enumerable.Range(0, 25).Select(i => StartOf($"m{i}")));
        messages.Add("""{"type":"chat","text":"last"}""");
        await using var server = new FakeCoordinator([.. messages]);
        var races = new List<ForkRace>();
        var said = new List<string>();
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), races.Add);
        session.Refused += said.Add;

        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.Chat.Count == 1, cancel);                    // every message before it was taken in

        Assert.Equal(started, races.Count);
        Assert.Equal(refused, session.StartsRefused);
        Assert.Contains(inLobby ? "while this player's race m0 was still on" : "in no lobby", Assert.Single(said), StringComparison.Ordinal);
        Assert.Equal(inLobby ? "m0" : null, session.Race?.Start.MatchId);
        Assert.NotNull(session.ConnectedTo);
        // the race over (the game closed), the session is free for the lobby's next one
        await session.RaceOverAsync();
        Assert.Null(session.Race);
    }

    [Fact]
    public async Task ACoordinatorThatNamesRelaysWithoutEndIsAskedAboutAFew()
    {
        var relays = string.Join(",", Enumerable.Range(0, 5000).Select(i => $$"""{"id":"r{{i}}","host":"127.0.0.1","port":0}"""));
        var message = Assert.IsType<MeasureRelays>(ServerMessages.Checked($$"""{"type":"measureRelays","matchId":"m7","relays":[{{relays}}],"timeoutMs":1500}"""));
        Assert.Empty(message.Relays);                                          // port 0 is no relay
        var some = string.Join(",", Enumerable.Range(0, 5000).Select(i => $$"""{"id":"r{{i}}","host":"127.0.0.1","port":{{1 + i}}}"""));
        message = Assert.IsType<MeasureRelays>(ServerMessages.Checked($$"""{"type":"measureRelays","matchId":"m7","relays":[null,{"id":"x","port":1},{{some}}],"timeoutMs":1500}"""));
        Assert.Equal(ServerMessages.MaxRelays, message.Relays.Count);
        await Task.CompletedTask;
    }

    /// <summary>A message is read into memory until it ends: one that does not end is not.</summary>
    [Fact]
    public async Task AMessageTooLongToBeOneEndsTheConnection()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome,
            """{"type":"chat","text":" """ + new string('x', ServerMessages.MaxBytes + 10) + "\"}");
        await using var session = Session();
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.ConnectedTo is null, cancel);
        Assert.Contains("too long", session.Status, StringComparison.Ordinal);
        Assert.Empty(session.Chat);
    }

    [Fact]
    public void ChatIsShownAsPlainTextOfAChatLinesLength()
    {
        var chat = Assert.IsType<ChatMessage>(ServerMessages.Checked(new ChatMessage("lobby", "p2", "BEN‮", "hi\r\n\u0000there ⁦😀" + new string('z', 5000), DateTimeOffset.UnixEpoch)));
        Assert.Equal("BEN", chat.FromName);
        Assert.StartsWith("hithere 😀zzz", chat.Text, StringComparison.Ordinal);
        Assert.True(chat.Text.Length <= ServerMessages.MaxChatLength);
    }

    /// <summary>The course vote as it stands and its result give the lobby's bars and shares, which add the counts
    /// up (a sum past the type's range is an error of its own) and read who has voted: a count no vote can have is
    /// none, and a message with a part missing is no message.</summary>
    [Fact]
    public void AVotesCountsAreCountsBeforeTheLobbyAddsThemUp()
    {
        var standing = Assert.IsType<CourseVotes>(ServerMessages.Checked(
            """{"type":"courseVotes","matchId":"m7","votes":[2147483647,2147483647,-5,3],"voted":["p1",null,"p2"]}"""));
        Assert.Equal([ServerMessages.MaxPlayers, ServerMessages.MaxPlayers, 0, 3], standing.Votes);
        Assert.Equal(["p1", "p2"], standing.Voted);
        Assert.Equal((2 * ServerMessages.MaxPlayers) + 3, standing.Votes.Sum());
        Assert.Null(ServerMessages.Checked("""{"type":"courseVotes","matchId":"m7","votes":[1,2]}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"courseVotes","matchId":"m7","voted":[]}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"courseVotes","votes":[1],"voted":[]}"""));
        Assert.Null(ServerMessages.Checked(new CourseVotes("m7", [.. Enumerable.Repeat(1, ServerMessages.MaxVoteOptions + 1)], [])));
        Assert.True(ServerMessages.Checked(new CourseVotes("m7", [1], [.. Enumerable.Repeat("p", 5000)])) is CourseVotes { Voted.Count: 60 });

        var drawn = Assert.IsType<CourseChosen>(ServerMessages.Checked(
            """{"type":"courseChosen","matchId":"m7","options":[1,3,-1],"votes":[2147483647,-1,2147483647],"winner":2,"course":9}"""));
        Assert.Equal([ServerMessages.MaxPlayers, 0, ServerMessages.MaxPlayers], drawn.Votes);
    }

    /// <summary>The lobby page draws a row a seat, the empty ones too: a lobby of seats without end is a lobby of
    /// as many seats as a lobby can have members.</summary>
    [Fact]
    public void ALobbyHasNoMoreSeatsThanItCanHaveMembers()
    {
        var lobby = Assert.IsType<LobbyState>(ServerMessages.Checked(
            """{"type":"lobby","lobbyId":"l1","players":[],"capacity":2147483647,"votingMinimum":2,"votingOpen":false,"startVotes":0,"cpuFillVotes":0}"""));
        Assert.Equal(60, lobby.Capacity);
        Assert.Equal(0, Assert.IsType<LobbyState>(ServerMessages.Checked(lobby with { Capacity = -4 })).Capacity);
        Assert.Equal(30, Assert.IsType<LobbyState>(ServerMessages.Checked(lobby with { Capacity = 30 })).Capacity);
    }

    /// <summary>A server's public status is anybody's answer (the player sets the server's address; a LAN server is
    /// any computer): the Play page reads its lists, names and numbers on the window's thread.</summary>
    [Fact]
    public void AServersStatusWithPartsMissingOrWithoutEndIsMadeWhole()
    {
        Assert.Null(ServerMessages.Checked((ServiceStatus?)null));

        var bare = ServerMessages.Checked(System.Text.Json.JsonSerializer.Deserialize<ServiceStatus>("""{"playersOnline":-3}""", ControlProtocol.Json))!;
        Assert.Equal("", bare.Name);
        Assert.Equal(0, bare.PlayersOnline);
        Assert.Empty(bare.Lobbies);
        Assert.Empty(bare.Starting);
        Assert.Empty(bare.Races);
        Assert.Empty(bare.Relays);

        var odd = ServerMessages.Checked(System.Text.Json.JsonSerializer.Deserialize<ServiceStatus>("""
            {"name":"den\u202e","playersOnline":2147483647,
             "lobbies":[null,{"players":[]},{"id":"l1","capacity":2147483647,"startVotes":-1,"stage":null,
               "players":[null,{"machine":99},{"name":"ANA\u0000","machine":3,"color":"#102030"},{"name":"BEN","color":"a colour's name without end"}]}],
             "races":[null,{"relay":"r"},{"id":"r1","laps":3,"course":1,
               "live":{"course":1,"laps":3,"racers":[{"seat":0,"state":"racing"},{"seat":0,"state":"racing"}]}}]}
            """, ControlProtocol.Json))!;
        Assert.Equal("den", odd.Name);
        Assert.Equal(ServerMessages.MaxPlayers, odd.PlayersOnline);
        var lobby = Assert.Single(odd.Lobbies);
        Assert.Equal(("l1", 60, 0, LobbyState.Forming), (lobby.Id, lobby.Capacity, lobby.StartVotes, lobby.Stage));
        Assert.Equal([new RacerView("", 99, null), new RacerView("ANA", 3, "#102030"), new RacerView("BEN", 0, null)], lobby.Players);
        var race = Assert.Single(odd.Races);
        Assert.Equal(("r1", "", "", ""), (race.Id, race.Relay, race.Region, race.CourseName));
        Assert.Empty(race.Players);
        Assert.Null(race.Live);                                     // a seat named twice: not shown

        var many = ServerMessages.Checked(new ServiceStatus("s", DateTimeOffset.UnixEpoch, 1,
            [.. Enumerable.Range(0, 5000).Select(i => new LobbyView($"l{i}", [.. Enumerable.Repeat(new RacerView("P", 0, null), 500)], 30, false, 0))],
            [], [.. Enumerable.Range(0, 5000).Select(i => new RaceView($"r{i}", "", "", 1, new string('x', 5000), 3, [], 0, DateTimeOffset.UnixEpoch))], []))!;
        Assert.Equal(ServerMessages.MaxListed, many.Lobbies.Count);
        Assert.Equal(ServerMessages.MaxListed, many.Races.Count);
        Assert.All(many.Lobbies, l => Assert.Equal(60, l.Players.Count));
        Assert.All(many.Races, r => Assert.Equal(80, r.CourseName.Length));
    }

    private const string Fingerprint = "aba727f7aba727f7aba727f7aba727f7aba727f7";

    /// <summary>The later parts of the messages, each checked as the rest. The packs a racer
    /// may pick: a pack that is null was listed, and the lobby's machine stage read its name on the window's thread
    /// at every lobby message (the launcher's error, and the page no longer followed the lobby); a racer's own pack
    /// was shown as it came (any length, marks that turn the reading direction).</summary>
    [Fact]
    public void ALobbysPacksAreShownOnlyAsPacksCanBe()
    {
        var pack = $$"""{"name":"GX Pack","fingerprint":"{{Fingerprint}}"}""";
        var lobby = Assert.IsType<LobbyState>(ServerMessages.Checked($$"""
            {"type":"lobby","lobbyId":"l1","capacity":30,"stage":"choosing","players":[
              {"id":"p1","name":"ANA","machine":3,"color":{"r":1,"g":2,"b":3},"pack":"GX‮ Pack{{new string('x', 5000)}}"},
              {"id":"p2","name":"BEN","machine":3,"color":{"r":1,"g":2,"b":3},"pack":null}],
             "seatPacks":[null,{"name":null,"fingerprint":"{{Fingerprint}}"},{"name":"GX","fingerprint":"no"},{{pack}},{{pack}},{{pack}},{{pack}},{{pack}}]}
            """));
        Assert.NotNull(lobby.SeatPacks);
        Assert.All(lobby.SeatPacks, p => Assert.Equal(("GX Pack", Fingerprint), (p.Name, p.Fingerprint)));
        Assert.InRange(lobby.SeatPacks.Count, 1, RaceSetup.MostSeatPacks);
        Assert.Equal("GX Pack" + new string('x', 33), lobby.Players[0].Pack);
        Assert.Null(lobby.Players[1].Pack);
    }

    /// <summary>A tag changed is a tag (the session kept only a valid one already); the players' list, which this
    /// launcher does not ask for, is not passed on; a race's packs a seat are whole, or there is no race.</summary>
    [Fact]
    public void ATagAListAndARacesPacksAreCheckedAsTheRest()
    {
        Assert.Null(ServerMessages.Checked("""{"type":"tag"}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"tag","tag":"abcd"}"""));
        Assert.IsType<TagChanged>(ServerMessages.Checked("""{"type":"tag","tag":"AB23"}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"players","players":[null]}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"playersChanged","joined":null}"""));
        string Start(string packs) =>
            "{\"type\":\"matchStart\",\"matchId\":\"m7\",\"relay\":{\"id\":\"r1\",\"host\":\"\",\"port\":47811},\"token\":\"1f2e\",\"seat\":0," +
            "\"setup\":{\"course\":21,\"laps\":3,\"shuffleSeed\":1,\"cpuSeed\":2,\"seats\":[{\"seat\":0,\"playerId\":\"p1\",\"name\":\"ANA\",\"machine\":3}]," +
            "\"stats\":{\"name\":\"GX\"},\"packs\":" + packs + "}}";
        Assert.Null(ServerMessages.Checked(Start("[null]")));
        Assert.Null(ServerMessages.Checked(Start("[{},{},{},{},{}]")));
        var start = Assert.IsType<MatchStart>(ServerMessages.Checked(Start("""[{"name":"GX"}]""")));
        Assert.Equal(new StatPackWire("GX", "", ""), start.Setup.Packs![0]);
        Assert.Equal(new StatPackWire("GX", "", ""), start.Setup.Stats);
        Assert.NotNull(StatPack.FromWire(start.Setup.Stats!).Refusal);
    }
}
