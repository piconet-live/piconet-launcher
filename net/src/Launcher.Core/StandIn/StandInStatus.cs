using System.Net;
using System.Net.Http.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>The stand-in service's status page and health check (<see cref="StandInService"/>): GET /api/status
/// answers with the stand-in's lobby (<see cref="ServiceStatus"/>, as a coordinator's), anything else with OK. Made
/// in the launcher: no request leaves it.</summary>
internal sealed class StandInStatus : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        if (request.RequestUri?.AbsolutePath == "/api/status")
        {
            response.Content = JsonContent.Create(Status(DateTimeOffset.UtcNow), options: ControlProtocol.Json);
        }
        return Task.FromResult(response);
    }

    /// <summary>The stand-in as its status page tells it: its racers in one forming lobby, nothing racing.</summary>
    private static ServiceStatus Status(DateTimeOffset now)
    {
        var racers = StandInLobby.Others.Select(p => new RacerView(p.Name, p.Machine, Rgb.Hex(p.Color), p.Tag)).ToList();
        return new ServiceStatus(StandInService.Name, now, racers.Count,
            [new LobbyView(StandInLobby.LobbyId, racers, StandInLobby.Capacity, VotingOpen: true, StartVotes: 0)],
            [], [], []);
    }
}
