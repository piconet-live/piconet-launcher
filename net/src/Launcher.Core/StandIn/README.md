# Launcher.Core/StandIn

The stand-in service: the launcher's dev mode (`--dev-service`), a service in the launcher's own process, so that a
contributor can work on Quick Play's pages without the real server, a server stack or keys. It takes the real
connection's place through `ICoordinatorConnection` (`StandInCoordinator`). Races never start on it: it has no race server, and says so.

| File | What it does |
|---|---|
| `StandInService.cs` | The dev mode's entry: the address the launcher is pointed at (under `.invalid`, nobody's), the connect for the session, the status answers; Quick Play's rotation (the protocol's `protocol/quickplay/`, beside the program: the service's mode files), and a host's mode file compiled as the service does |
| `StandInCoordinator.cs` | The connection: the interface's rules (nothing delivered before a handler, in order, one at a time, checked as a server's; `Closed` once; sends after the end do nothing) |
| `StandInLobby.cs` | The lobby played for one player: five stand-in racers, chat, the start and CPU votes, the course vote and its draw (with the rotation, as the service's: five cards, one spicy, Random; without it, courses alone, and a notice says why), the machine stage; then "No races on the stand-in service" and the lobby forms again. As a custom lobby: the room's members and host, its rules, the host's rules change, kick and start, the host handed on when the host leaves |
| `StandInRooms.cs` | The custom lobbies: the browser's list (three made-up lobbies, one locked with the password "gx", one racing; the player's own once made, which two stand-ins join), create, join and its refusals (wrong password, full, closed) |
| `StandInStatus.cs` | GET /api/status (the stand-in's lobby, as a coordinator's status) and the health check, answered in the process |

The app's side: `AppState.StandIn` (the switch read in `Launcher.App/CommandLine.cs`), which points the session,
the status feed and the connection dot here, keeps the player's own server in their settings file, and lets RACE
join without a disc or a game.
