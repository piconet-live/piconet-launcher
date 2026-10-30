# Launcher.App/ViewModels

What each page shows and does, without visuals (CommunityToolkit.Mvvm): the views bind to these and can be replaced.
Each reads and drives the shared state, `../AppState.cs`.

| File | What it is |
|---|---|
| `ViewModelBase.cs` | What every page's model shares: its work brought to the window's thread |
| `MainViewModel.cs` | The window: the four tabs, their pages, which page shows, when the lobby takes over |
| `NoticesViewModel.cs`, `NoticeItem.cs` | The server's notices in one place: by tier, the banner (its 10 s), the window's flash and tone, the bell's dot and the list; a notice as its card shows it |
| `DiscViewModel.cs` | The first screen: the player's disc image, checked, and the game's files prepared from it (progress, Cancel, the preparation's sentence) |
| `AccountViewModel.cs` | The left column's ends: the name and machine at the top, the connection dot and the build at the bottom |
| `PlayControlViewModel.cs` | The left column's RACE button, or what the lobby is doing; "Play F-Zero GX"; both off with the reason while the game cannot be started |
| `HomeViewModel.cs`, `.Tables.cs`, `HomeRows.cs` | The Play page out of a lobby: the map's place; the lobbies table and the last races; the rows' types |
| `LobbyViewModel.cs`, `.Stages.cs`, `.Machine.cs`, `LobbyRows.cs` | The lobby: racers, votes, chat; the line of steps with its clock, the course vote with each course's chance and the draw on the strip; the machine stage, which is the Garage (what is selected there is raced); the rows' types |
| `RaceViewModel.cs` | The lobby's race as it runs (standings, the map's racers, a mode's `board` lines from this launcher's own game), then its results; `ForPlayback`: the race of a replay the game plays, over the Replays page when its mode has lines |
| `LanViewModel.cs` | Local network: servers heard, join by address |
| `GarageViewModel.cs`, `GarageViewModel.Stats.cs`, `GarageViewModel.Lobby.cs` | Each machine's look: colour and setting; its stats from the disc, and the order of the machines' cells by one of them; favourites and their filter, and the Garage's part in a lobby (the machines on offer, the server's fixed setting, the machine picked) |
| `ReplaysViewModel.cs` | The replays as rows, played in the game |
| `StatsViewModel.cs` | Stats, the Replays tab's second page: the player's figures from their replays, online races and practice on tabs of their own |
| `SettingsViewModel.cs` | Settings; the Server box is a draft until it is committed |
| `ControllerViewModel.cs`, `ControllerViewModel.Helper.cs`, `ControllerViewModel.InGame.cs`, `ControllerRows.cs` | The Controller page: the ports and their settings; its helper, run while the page shows and no game runs; "Set up in the game" (the game started on its own controller options, and when the button is off); a mapping row and a device choice |
