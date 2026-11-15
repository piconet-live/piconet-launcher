# Launcher.App/ViewModels

What each page shows and does, without visuals (CommunityToolkit.Mvvm): the views bind to these and can be replaced.
Each reads and drives the shared state, `../AppState.cs`.

| File | What it is |
|---|---|
| `ViewModelBase.cs` | What every page's model shares: its work brought to the window's thread |
| `MainViewModel.cs` | The window: the four tabs, their pages, which page shows, when the lobby takes over |
| `MainViewModel.Shell.cs` | Which of the two interfaces the window shows (the classic one, or with "Experimental UI" the redesigned one, and its shell), and whether a developer's data shows |
| `NextUi/` | What the redesigned interface shows that the classic one has no page for: its shell (areas, the way back, hints), the hub, a list of places |
| `ChatSidebarViewModel.cs` | The chat sidebar: open, pinned and its width (kept in the settings), the button's dot, the line out of a lobby; the lines are the lobby page's (`LobbyViewModel.Chat.cs`) |
| `NoticesViewModel.cs`, `NoticeItem.cs` | The server's notices in one place: by tier, the banner (its 10 s), the window's flash and tone, the bell's dot and the list; a notice as its card shows it |
| `DiscViewModel.cs` | The first screen: the player's disc image, checked, and the game's files prepared from it (progress, Cancel, the preparation's sentence) |
| `AccountViewModel.cs` | The left column's ends: the name and machine at the top, the connection dot and the build at the bottom |
| `PlayControlViewModel.cs` | Quick Play's RACE button, or what the lobby is doing; the left column's "Start F-Zero GX" (`Views/StartGameView`); both off with the reason while the game cannot be started |
| `HomeViewModel.cs`, `.Tables.cs`, `HomeRows.cs` | The Play page out of a lobby: the map's place; the lobbies table and the last races; the rows' types |
| `LobbyViewModel.cs`, `.Stages.cs`, `.Machine.cs`, `LobbyRows.cs`, `RaceLine.cs` | The lobby: racers, votes, the chat's lines (shown in the chat sidebar); the line of steps with its clock and, from the draw on, the race's line (mode, course, laps); the vote with each course's chance and the draw on the strip; the machine stage, which is the Garage (what is selected there is raced); the rows' types |
| `LobbyViewModel.Phases.cs` | The lobby as the redesigned interface shows it (`Views/NextUi/QuickPlayView`): which phase it is in for this player (the queue, the gathering, the vote, the machine stage, the start, the race, its result; a custom lobby: the classic page), and the same state in the words and shares those pages show (the vote to start as a share, the clock as a share, the course that is next, the leaders, the lap). It decides nothing |
| `RaceViewModel.cs` | The lobby's race as it runs (standings, the map's racers, a mode's `board` lines from this launcher's own game), then its results; `ForPlayback`: the race of a replay the game plays, over the Replays page when its mode has lines |
| `LanViewModel.cs` | Local network: servers heard, join by address |
| `DiscViewModel.cs` (OpensAtOnce, Ready, Open) | The first screen; the redesigned first start holds it open after the game is ready and opens the launcher at its last step |
| `GarageViewModel.Stage.cs` | The redesigned Garage's stage: the machine the grid's focus or press is on (a stock one previewed, a custom one in the builder, the shelf kept), used, and SELECT put back when the page is left; the classic page calls none of it |
| `GarageViewModel.cs`, `GarageViewModel.Stats.cs`, `GarageViewModel.Lobby.cs` | Each machine's look: colour and setting; its stats from the disc, and the order of the machines' cells by one of them; favourites and their filter, and the Garage's part in a lobby (the machines on offer, the server's fixed setting, the machine picked) |
| `ReplaysViewModel.cs` | The replays as rows, played in the game; the newest races whatever the tab (the redesigned profile's) |
| `StatsViewModel.cs` | Stats, the Replays tab's second page: the player's figures from their replays, online races and practice on tabs of their own |
| `SettingsViewModel.cs` | Settings; the Server box is a draft until it is committed; "Experimental UI" is set by its question's answer, never by the tick alone |
| `ControllerViewModel.cs`, `ControllerViewModel.Helper.cs`, `ControllerViewModel.InGame.cs`, `ControllerRows.cs` | The Controller page: the ports and their settings; its helper, run while the page shows and no game runs; "Set up in the game" (the game started on its own controller options, and when the button is off); a mapping row and a device choice |
