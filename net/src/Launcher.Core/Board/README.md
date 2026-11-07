# Launcher.Core/Board

The status board between the launcher and the game it runs: one small file in the launcher's own data folder that
both processes map (layout: `protocol/board.toml`, generated into `StatusBoard.g.cs`, compiled in from
`protocol/generated`; the whole account: protocol/formats.md section 8). It replaced the UDP live feed.

| File | What it does |
|---|---|
| `GameBoard.cs` | Maps a start's board (the file: `../Files/BoardFile.cs`), looks at it every 15 ms: taken or refused, the phase, each new race snapshot (only one that can be shown), the game's events, the freeze judge, the local pad (`Pad`, for the Inputs window); writes the mailbox (`AskStop`, `Notice`); removes the file with the board |
| `PadReading.cs` | The board's LocalPad as the Inputs window takes it: the buttons by name, the sticks and triggers kept within their reach; none while the game holds no pad |
| `FreezeJudge.cs` | A heartbeat that stood 10 s where the game should beat (not before its first beat, at the start gate, or while the core waits for the relay): the game is hung |
| `BoardSnapshots.cs` | The board's binary race as the pages' `RaceSnapshot` |
| `ModeBoard.cs` | The mode's `board` lines (ModeHead, ModeValues; read by `GameBoard`, once and each new set) and how a value reads: its word, else by its type; the rows the race page draws. A head for the generated reader comes from `ModeBoardHead.Blank()`: a new `BoardModeHead`'s records have no text arrays |
| `BoardLines.cs` | The board as lines of JSON for the modes without a window (a test visit, `--play-match`) |

The app's side is `Launcher.App/AppState.Board.cs` (a board for every start, "Game not responding" and End game,
the stop asked before a process is ended). Tests: `BoardTests` in `net/tests/Launcher.Tests`.
