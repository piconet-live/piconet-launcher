# Launcher.Core/Files

What the launcher reads from outside itself (files it did not write, or wrote and cannot trust any more), each turned into a
checked value or a refusal with a sentence: the settings, a replay, the disc image and the look read from it, the game folder, the launch file. And the launch file it writes for a game.

**The rule.** Nothing outside this folder opens a file from outside: `File.*`, `FileStream` and JSON read from a
file belong here and nowhere else in `Launcher.Core` or `Launcher.App`.

**A file from outside can say anything.** A size, a count, a name's length or a depth is held against a limit before
anything is allocated for it, and nothing here calls itself on what a file says.

| File | Its one job |
|---|---|
| `SettingsFile.cs`, `SettingsCheck.cs`, `NoticeLog.cs` | `settings.json`: read, made sound, saved; a file that was not read as it stood is kept. `notices.json`: the server's last 100 notices, a damaged file read as none |
| `ReplayFile.cs` | A replay file (`*.fzrp`): the folder's list, what the Replays page shows of one, why one is not played, what recorded it |
| `ReplayIndex.cs` | The replay index (`cache/replay-index.json`): what the Stats page counts of each replay, so a start reads only new and changed files |
| `LaunchConfig.cs` | The launch file (`--config FILE`): a start's test settings, each checked; a member it does not know is refused |
| `GameLaunchFile.cs` | The game's launch file, written for each game started (the runner's `--config`): the start, and the test settings handed on |
| `BoardFile.cs` | The status board's file (`../Board/`): made shared in `<data>/boards`, removed with its board; boards of launchers that are gone (by the process id in their header) removed |
| `MatchFile.cs` | The match file: this player's race as the game is given it, built from the server's match start and written (`port/facts/tests/places.tsv` names the file for the input delay typed in it) |
| `RecompLaunch.GameFiles.cs`, `RecompLaunch.UserFolder.cs`, `ControllerFiles.cs`, `IniFiles.cs` | The game's folders: what is missing for a start, whether the first screen has to prepare, the build's name, a replay's image against the same-race table; the user folder's emulator settings and the fork's controller copied once; the game's controller settings (`Config\Dolphin.ini` ports, `Config\GCPadNew.ini`): read, one port written with every unknown line kept, a first start's default; an emulator settings file read as the emulator reads it (sections of one name one section, a key's last line) and written without loss (bytes that are no UTF-8, the mark, the line ends; beside the file, then moved) |
| `LogFiles.cs`, `Install.cs` | The launcher's own logs (`launcher.log`, `gpu.log`): text added at the end, a long one set aside; where the launcher is, and whether a marker beside its libraries makes it an installed launcher (its data in the player's own folder) or a player copy with its data in itself |
| `DataMove.cs` | An older player copy's data (settings, replays, the game's folders, the prepared game files) copied into an installed launcher's data folder once, each file checked, links never followed, the older copy left as it was |
| `DiscImage.cs`, `DiscImage.Table.cs` | A plain GameCube image: its kind by its first bytes, the header, the system area, reads inside its real length; the file table: the one reader (`TableOf`) with its limits, and the writer of its one form |
| `DiscPins.cs`, `DiscCheck.cs` | Which disc is the game: id, disc number, revision, hashes, the pinned words (`../DiscPins.json`); the Disc page's question, answered by the preparation's own first look (`DiscPrep.Refusal`) |
| `DiscPrepTypes.cs`, `DiscPrep.cs` | The preparation's states, outcomes, progress and options; the door: `Prepare`, one at a time, what a stopped one left behind |
| `DiscPrep.Game.cs` | Which file is the game: every other kind of file told apart, each with its sentence |
| `DiscPrep.Copy.cs` | The plan, the copy, its check, and the swap that keeps a working copy until the new one is checked |
| `DiscPrep.Status.cs` | What the data folder holds: the cheap look, the whole check (`Verify`), the words rewritten without the disc |
| `DiscPrep.Words.cs`, `DiscPrep.Record.cs` | The words written into the copy (the pins, the bundle's patch list); the folder's digests and `prepared.json` |
| `DiscArt.cs`, `U8Archive.cs`, `MachineModel.cs`, `MachinePaint.cs` | The look read from the player's disc, before any preparation checked it: pictures, an archive's files, machines' models and paint; and `DiscArt.Stats.cs`, `MachineStatFile.cs`: the machines' stats, the car data's blocks and, from the main module only when it is the pinned one, the letters; `StatPackImport.cs`: a stat pack made of a changed car data file's differences from the disc's (`--import-stat-pack`); `DiscArt.Courses.cs`: the courses' course-select models and their files; `CourseStillCache.cs`: where the Practice page's course stills are kept and their disc key; `DiscArt.Sounds.cs`, `GcaxBank.cs`, `GameSounds.cs`, `WaveFile.cs`: the menu sound bank, its decoding (GCAX, DSP ADPCM), the launcher's sounds from it and their cache, wave files |

**The limits** (`DiscImage`): every part inside the image's real length; the loader, the executable and the file
table at most 24 MiB each (the console's memory); a pinned file's size equal to its pin before it is read; the file
table at most 4 MiB, 65,536 entries, names of 255 bytes without '/', paths of 255 characters, folders 32 deep.

**Testing.** `net/tests/Launcher.Tests/Files/`, on made-up images only (`SyntheticDisc.cs`): no game file is read or written, and a refusal is checked by its sentence.

    (cd net && dotnet test --project tests/Launcher.Tests --no-build --filter-class Launcher.Tests.DiscPrepTests)

A new guard comes with a made-up image that fails without it (`DiscPrepTests.Hostile.cs` shows how to make one).
The real disc is only ever opened for reading; `Launcher.App --prepare-disc IMAGE --data FOLDER` prints the game
folder's digest, the same for every image of the game.
