using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The start grid's part in the launcher (Online.Server StartGrid): the match start's grid reaches the game's
/// match file as it came, and the race's end tells the coordinator the race as the game last showed it
/// (<see cref="RaceOver.Final"/>), from which the lobby's next grid is taken.</summary>
public sealed class StartGridTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("start-grid-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Start = """
        {"type":"matchStart","matchId":"m1","relay":{"id":"r1","host":"","port":47811},"token":"1f2e3d4c5b6a7988","seat":0,
         "setup":{"course":21,"laps":3,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3},
                  {"seat":1,"name":"CPU","machine":4}],"shuffleSeed":1,"cpuSeed":2,"grid":[0,1]}}
        """;

    [Fact]
    public void TheGridReachesTheMatchFileAsItCame()
    {
        var start = (MatchStart)ControlProtocol.DecodeServer(Start)!;
        Assert.Equal([0, 1], start.Setup.Grid);
        var file = MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Full));
        Assert.Contains("\"grid\": [", file);
        Assert.Equal([0, 1], System.Text.Json.Nodes.JsonNode.Parse(file)!["match"]!["setup"]!["grid"]!.AsArray().Select(x => (int)x!));
    }

    [Fact]
    public async Task TheRacesEndCarriesTheLastSnapshotTheGameShowed()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, Start);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"),
            _ => started.TrySetResult());
        await session.JoinAsync(server.Address, cancel: cancel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        RacerLive R(int seat, string state, int frame) => new(seat, 0, 3, 0, frame, null, 0, 0, 100, 0, 0, state, 0, 0, 0, 10);
        await session.ReportAsync(new RaceSnapshot(100, 0, [R(0, RacerLive.Racing, 100), R(1, RacerLive.Racing, 100)]));
        await session.ReportAsync(new RaceSnapshot(9000, 0, [R(0, RacerLive.Finished, 9000), R(1, RacerLive.Out, 9000)]));

        await session.RaceOverAsync();

        RaceOver? over = null;
        for (var i = 0; i < 500 && over is null; i++)
        {
            over = server.Received.Select(ControlProtocol.DecodeClient).OfType<RaceOver>().FirstOrDefault();
            await Task.Delay(10, cancel);
        }
        Assert.NotNull(over);
        Assert.Equal("m1", over.MatchId);
        Assert.Equal(9000, over.Final!.Frame);
        Assert.Equal([RacerLive.Finished, RacerLive.Out], over.Final.Racers.Select(r => r.State));
    }
}
