# Launcher.Core

The launcher's logic, with no UI type: `../Launcher.App` shows it, `../../tests/Launcher.Tests` tests it. It refers
to `Online.Protocol` (the messages shared with the server) and to nothing else of the server: no project reference,
no `using` (`scripts/structure_check.py --explain 15`). What arrives from outside is read in `Inbound/` (the network) and
`Files/` (the disk).

| Files | What they are |
|---|---|
| `ICoordinatorConnection.cs`, `CoordinatorClient.cs`, `QuickPlaySession.cs`, `QuickPlaySession.Received.cs`, `QuickPlaySession.Name.cs`, `QuickPlaySession.Custom.cs` | The connection to a server as the session uses it (an interface: a stand-in service or a test double can take the WebSocket's place), and the WebSocket one (what it sends); Quick Play on it: the player's tag (the first saved, one for a taken pair only for the connection), the lobby, the votes, the machine choice, the match start handed to the game; what each of the server's messages does to it; a visit to the custom lobbies (no queue: the browser's list, a lobby made or joined with the racer, the host's rules, kick and start) |
| `ServerNotice.cs` | A notice from the server's operator: its four tiers, known by the server's id, its tier word taken off (`Files/NoticeLog.cs` keeps the last 100) |
| `Inbound/` | Everything that arrives over the network, checked: a server's messages and answers, LAN announcements, relay pings (its own README) |
| `StandIn/` | The stand-in service, the launcher's dev mode (`--dev-service`): a lobby, chat and the votes in the launcher's own process, no races (its own README) |
| `Board/` | The status board of the running game, a file both map: its race, its heartbeat and phase, the freeze judge, the mailbox's stop (its own README) |
| `Presence.cs`, `DiscordPresence.cs`, `DiscordLink.cs` | Discord's status: the launcher's state as two lines; the connection to the player's own Discord (local IPC, DiscordRichPresence), one card in 5 s at most, quiet retries |
| `QuickPlayDefaults.cs` | Quick Play's rules as the service's code sets them: for a server that names none, and the stand-in's |
| `ServerChoice.cs` | When a server is set and who is asked unprompted |
| `LobbyScreen.cs`, `CourseDraw.cs`, `LobbyTones.cs`, `DrawSounds.cs`, `LauncherSounds.cs`, `RaceStandings.cs`, `Courses.cs`, `Machines.cs` | What the lobby page shows by the lobby's stage, the steps of its line, where its Garage is open; the draw as a strip of chances with a pointer; the launcher's sounds: which sound plays (the game's from the disc, `Files/GameSounds.cs`, the draw's made in code, `DrawSounds.cs`, else the launcher's own tones); a race's order; names |
| `MachineStats.cs` | The machines' stats as one table, whatever it was read from, and the order a list of machines is shown in |
| `RecompLaunch.cs` | The game: where it is, its arguments, its start (what its folders hold is read in `Files/RecompLaunch.*.cs`; the match file is `Files/MatchFile.cs`) |
| `Replays.cs`, `ReplayWords.cs` | A replay as values (`Files/ReplayFile.cs` reads one), shown in the file browser; what the Replays page says of one |
| `ReplayStats.cs` | The Stats page's figures from the player's replays (races, wins, podiums, places, best times per course and machine, courses raced most, recent form), online races and practice apart |
| `Settings.cs`, `DataFolders.cs` | The settings as values: the racer, each machine's look, the launcher's own choices (`Files/SettingsFile.cs` reads and saves them); the replays' and the fork's folder as the data folder's own: never written as full paths, and another copy's taken as this one's |
| `StackStamp.cs` | The server stack this build was made for: its official server, and whether it is a test build |
| `Files/` | Everything read from the disk, checked: `settings.json`, the disc image, the look read from it (pictures, machines' models and paint) and the game's files prepared from it, the launch file (its own README) |
| `CourseModel.cs`, `PackedModel.cs`, `Studio.cs`, `MapCamera.cs`, `CourseShot.cs`, `VenueSky.cs`, `ColorWheel.cs` | What the 3D views draw from where, and the colour wheel, as plain maths (drawn in `Launcher.App/Gpu`) |
| `LastResort.cs` | An error nothing caught: written to `launcher.log`, said once |
| `QuickPlayScript.cs` | The test door: Quick Play driven without a person |
| `DolphinTools.cs` | From the retired Dolphin fork: what is left of it opens a folder in the file browser |
| `Pads/` | The pad that works the window: `PadReader.cs` (the game's controller helper run while the window is in front and no game runs; the pad's port), `PadIntents.cs` (presses, the eased repeat, the C-stick's scroll), `FocusMap.cs` (where the focus goes, by position) |
| `ControllerSettings.cs`, `ControllerHelper.cs`, `ControllerStart.cs` | A controller port, the GameCube controls, the standard layouts and the first start's choice; the game's own program asked about the controllers (`moderngekko-run.exe --controllers`), the one question before a start, and the sentences for the player |
| `DiscPins.json` | Which disc is the game (compiled in; `Files/DiscPins.cs` reads it), written by `net/tools/DiscPinsWriter` |
