using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A spectator seat in the lobby: offered by a server that
/// takes spectators; a spectator's start becomes a match file that says so, which the game's launch file carries, and
/// records nothing.</summary>
public sealed class SpectatorSeatTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spectator-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly MatchStart Start = new("m1", new RelayEndpoint("r1", "", 47811), "1f2e3d4c5b6a7988", 0,
        new RaceSetup(21, 3, [new SeatSetup(0, "p1", "ANA", 3, null)], 1, 2));

    [Fact]
    public void ASpectatorsMatchFileSaysSoAndTheLaunchFileCarriesIt()
    {
        var racer = JsonNode.Parse(MatchFile.Build(Start, new RaceOptions("127.0.0.1", OtherSounds.Quieter)))!.AsObject();
        Assert.False(racer.ContainsKey("spectate"));
        var text = MatchFile.Build(Start, new RaceOptions("127.0.0.1", OtherSounds.Quieter) { Spectate = true });
        Assert.True(JsonNode.Parse(text)!["spectate"]!.GetValue<bool>());
        var launch = JsonNode.Parse(GameLaunchFile.Race("bundle", text))!;
        Assert.True(launch["match"]!["spectate"]!.GetValue<bool>());
        Assert.Null(launch["match"]!["record"]);
    }

    [Fact]
    public void AWatchStartIsCheckedAsAMatchStart()
    {
        var w = new WatchStart("m1", Start.Relay, Start.Token, 0, Start.Setup);
        var checkedOne = Assert.IsType<WatchStart>(ServerMessages.Checked(w));
        Assert.Equal((w.MatchId, w.Token, w.Seat), (checkedOne.MatchId, checkedOne.Token, checkedOne.Seat));
        Assert.Equal(w.Setup.Seats, checkedOne.Setup.Seats);
        Assert.Null(ServerMessages.Checked(w with { Seat = 99 }));
    }

    [Fact]
    public async Task WatchIsOfferedByAServerThatTakesSpectators()
    {
        var cancel = TestContext.Current.CancellationToken;
        const string welcome = """{"type":"welcome","playerId":"p1","serverName":"Den","version":3,"spectators":true}""";
        await using var server = new FakeCoordinator(welcome, FakeCoordinator.Lobby);
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => { });
        await session.JoinAsync(server.Address, cancel: cancel);
        await Headless.Until(() => session.Lobby is not null, cancel);
        Assert.True(session.CanWatch);
        await session.WatchAsync(true);
        Assert.True(session.Watching);
    }
}
