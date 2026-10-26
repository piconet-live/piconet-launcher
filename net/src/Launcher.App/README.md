# Launcher.App

The program a player opens: the launcher's window (Avalonia), and its modes without a window. What a page shows
and does is here; the logic with no UI type is in `../Launcher.Core`. The names players see are only in `Branding.cs`.

| File or folder | What it is |
|---|---|
| `Program.cs` | The start (Velopack's own first: Setup's and the updater's arguments, a downloaded update put in): the window, or a mode without one (the game started, replays listed, a disc prepared); the handler of last resort put on its routes |
| `CommandLine.cs` | Every option of the launcher, read here and nowhere else; an option not listed is refused. Test settings come from a launch file (`--config`) |
| `App.axaml`, `App.axaml.cs` | The application: the theme, the window made on the shared state, an error on the window's thread |
| `AppState.cs` | The state every page shares: the settings, the Quick Play session, every start of the game; `AppState.Controllers.cs`: the controllers before a start; `AppState.GameFiles.cs`: whether a start has the game's files, and their preparation from the disc |
| `AppState.Dropped.cs` | A Quick Play connection that ended without the player's doing, said in the left column |
| `AppState.Joining.cs` | A Quick Play join on its way: it counts as being in Quick Play for a game start, and a game that is open rules a join out |
| `AppState.Renamed.cs` | The player's name changed: saved, and told to the server the launcher is connected to |
| `ViewLocator.cs` | Which view shows which view model |
| `Branding.cs`, `BuildInfo.cs`, `NotYet.cs` | The names players see; the version and build in the corner; the parts that are built and hidden |
| `Attention.cs` | The lobby's three moments made noticed: a tone of the launcher's own at the settings' volume, and the taskbar's button flashing while the window is not in front (Windows) |
| `Updates.cs`, `Relaunch.cs` | The launcher's own updates (Velopack, from the GitHub releases: looked for at the start and, while the launcher stays open, every 2 hours or so and when a game ends or the player leaves Quick Play (30 minutes after the last look at the soonest), never while a game runs, "Update automatically" on; downloaded in the background, put in at the next start or with "Restart to update", never while a game runs); the close that starts the launcher again |
| `Taskbar.cs` | The launcher's button on the Windows taskbar: the application id it takes, and gives the games it starts |
| `ViewModels/` | What each page shows and does, without visuals |
| `Views/`, `Theme/` | The look only: a view a page, and the one theme they all stand on |
| `Controls/`, `Gpu/` | The launcher's own controls (the 3D views among them) and their drawing on the graphics card |
| `Assets/` | The fonts and the app's icon |
| `app.manifest`, `Launcher.App.csproj` | Windows' manifest; the project, which stamps the commit into the build's name and, for a player copy, writes the one program that stands at the copy's top |

Tests: `../../tests/Launcher.Tests` (the logic, and the start without a window). The pages are checked by pictures
of the running launcher.
