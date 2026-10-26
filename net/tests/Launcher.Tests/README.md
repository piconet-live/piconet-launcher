# Launcher.Tests

The launcher's tests (xunit v3; the test project is a program). No game, no disc (but your own image, read by one
test where it is at hand: `Files/RealDiscStatsTests`), no window, and no network but sockets on this computer. A test class a file; a class too long for one file is a partial class.

| Files | What they test |
|---|---|
| `CoreTests`, `SessionTests`, `RulesTests`, `LobbyTests`, `RaceStandingsTests`, `LiveTests` | Settings, names, the LAN browser, the build's name and the match file, the words for a refusal, the course draw, the lobby's screens, the live race |
| `StatPackMessageTests` | A race's stat pack from the server's match start to the match file, and a match start whose pack is none |
| `CustomRulesTests` | A custom lobby's rules: the summary line of what differs from Quick Play's, the whole list, the host's settings (only what differs, the pack always, the presets), the refusals' and leavings' lines, a browser's row |
| `LobbyFlowTests`, `StoryStageTests`, `GameVolumeTests` | The draw as a strip of chances and a pointer, the lobby's steps, the start vote's count, where the Garage is open; the story's stages named by their chapter; the game's volume written into its settings, and the launcher's three tones |
| `MachineSortTests` | The stat table, a value as text, the order of a list by a stat, a changed value laid over the table |
| `AllMachinesTests` | The game's 41 machines: names and files by id |
| `NoServerTests`, `ServerHealthTests`, `ServerStatusFeedTests` | No server but the one the player chose; the connection dot; the server's public status |
| `DroppedConnectionTests` | A connection that ends without the player's doing is said, with the server's reason when it gave one (a rate limit); the server's close is answered |
| `NoticeLogTests` | A server notice's checks, the notices file (restart, 100, once each, damaged), the last hour's never in the chat |
| `RetiredServerTests` | A retired server's own words are the notice the launcher goes Home with |
| `PresenceTests` | Discord's status: each state of the launcher as its two lines, no connection without an application id, the setting's off clearing it, a failing link never reaching the player, a card at most once a gap |
| `AssemblyNamesTests` | The launcher's programs and libraries name the product as their company, and no person |
| `StackStampTests` | The server stack a build was made for: its official server, a test build's mark, a build that names none |
| `PlayerKeyTests` | The launcher's secret (the player's tag comes from it): made once and kept; a bad one replaced; settings saved with a "tag" still load |
| `RecompLaunchTests`, `PreparationWantedTests`, `InstallTests`, `DataFoldersTests`, `ReplayTests`, `ReplayReadersTests`, `ReplayWordsTests` | The game's start and its arguments; whether the first screen has to prepare the game's files; a player copy known by its marker; a data folder moved or renamed keeps its replays; replays read and described; what each of the four readers of a replay's header says of one file, shape by shape |
| `Security*Tests`, `FuzzTests` | What a hostile server, LAN, replay or settings file can make the launcher do; `FakeCoordinator` (in `SecurityFileNameTests.cs`) is a server that says what a test tells it to |
| `ControllerFilesTests`, `ControllerHelperTests` | The game's controller settings read and written (unknown lines kept, four ports, each kind), the first start's default; the helper's lines and the launcher's commands against a double (`HelperDouble`) |
| `PadIntentsTests`, `FocusMapTests`, `PadReaderTests` | The pad that works the window: presses and the eased repeat of a held direction, the stick's gap, the C-stick's scroll; where the focus goes on layouts shaped as the pages; the pad's port (never a keyboard's), and the rule that its helper runs only with the window in front and no game (a game's start ends it, none until the game has gone) |
| `LastResortTests` | The handler of last resort, also by starting `Launcher.App` without a window |
| `MachineModelTests`, `PaintTests`, `StudioTests`, `MapCameraTests`, `CourseShotTests`, `GarageFiles.cs` | The Garage's and the map's models, paint and cameras (the Practice page's course pictures, their framing, sky and cache key), on made-up files |
| `Files/` | Disc preparation, on made-up images; a stat pack's import from a changed car data file (`StatPackImportTests`, and on your own disc where it is at hand: `RealDiscStatPackTests`) |
| `App/` | `Launcher.App`'s own code, run without a window |

No test here starts a server or names `Online.Server`: the launcher against a real server is `Integration.Tests`,
the service's (in the monorepo `../Integration.Tests`; `scripts/structure_check.py --explain 15`). The launcher
needs none of it.

```
dotnet build net/net.slnx --disable-build-servers -nodeReuse:false -v quiet -nologo
(cd net && dotnet test --project tests/Launcher.Tests --no-build)        # all: about 25 s
```
Every test has a time limit of 180 s (`../TestTimeLimit.cs`); its own test is the one skipped in an ordinary run (`scripts/check.sh` runs it alone).
