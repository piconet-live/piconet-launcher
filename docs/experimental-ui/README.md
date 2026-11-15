# The experimental UI

A second interface for the launcher, made for the pad first and for players new to Piconet: one choice a screen, a
yellow focus that the pad, the keyboard and the pointer all move, and F-Zero GX's own look. It is behind Settings'
**Experimental UI** and off by default. The classic launcher stays every player's and looks and works as it did;
switching back takes effect at once.

Both interfaces keep one state (`AppState` and the classic view models). The new one is a second set of views,
view models, controls and a theme beside the classic ones (`Views/NextUi`, `ViewModels/NextUi`, `Controls/NextUi`,
`Theme/NextUi` under `net/src/Launcher.App/`), which `ViewLocator` picks while the setting is on.

This page shows every area as it is on this branch, with pictures and short videos taken from the launcher itself.

| Classic launcher (unchanged) | Experimental UI |
|---|---|
| ![The classic launcher's Play page](media/classic-launcher.png) | ![The experimental UI's hub](media/hub.png) |

## Turning it on

- **Players:** Settings › Launcher › Try something new › **Experimental UI**. Ticking it asks first ("Turn on the
  experimental UI?"); unticking it is at once. In the new interface the switch is in Settings › Launcher.
- **Developers:** start the stand-in service (README, [Dev mode](../../README.md#dev-mode)) with
  `"experimentalUi": true` in the dev data folder's `settings.json`:

      cd piconet-launcher/net
      dotnet run --project src/Launcher.App -- --dev-service --data ../../piconet-dev-data

The new settings (`experimentalUi`, `developerMode`, `buttonIcons`, `confirmButton`, the player's own servers, the
hidden message of the day) are left out of `settings.json` at their defaults, so a file from 0.6.5 stays as it was.

## The hub

Play's first page: Quick Play, Custom lobby, Practice and Start F-Zero GX, with a line for the entry the focus is on.
Behind it Pico's Wild Goose from the player's own disc is filmed shot after shot, as an advertisement is, in slow
motion, its engines burning from the burners the disc gives (`ShowcaseShots`, `EngineFlames`, `Studio`). The pointer
turns and holds it; let go, it goes on a few seconds later. It stands still under Reduced motion.

![The hub's showcase, 15 s](media/showcase.webp)

[showcase.mp4](media/showcase.mp4) (1280×720, 15 s)

The top bar, the same on every page: the launcher's name (the main menu from anywhere), Back, the areas (Play,
Garage, Replays, More) between their keys, how many players are online on the server (a figure and a count that
rolls when it changes), the profile, the notices, the chat and Settings. The hint bar at the foot names what the
pad's buttons, or the keys, do on this page.

Without a disc image the showcase is left out and the background is the flag alone, a dark chequered flag drawn as a
hologram on the graphics card (`HoloFlag`):

![The hub without a disc image](media/hub-without-disc.png)

## Quick Play

Quick Play begins with the server: the official one and the player's own, each with its racers, lobbies and ping.
A server pressed opens a panel with its three ways, three like cards: **Join a race** (an open Quick Play lobby
only; with none open, "No race found" and nothing is joined or made), **Host a lobby** (a Quick Play lobby, through
Quick Play's queue; never a custom lobby) and **See its lobbies**.

| Choose a server | A server's panel |
|---|---|
| ![Choose a server](media/quick-play-servers.png) | ![A server's three ways](media/quick-play-server-panel.png) |

[quick-play-join.mp4](media/quick-play-join.mp4): Join a race, then the lobby gathering.

Each phase of the lobby is a page of its own: the racers gathering with the vote to start and the vote on CPU
racers; the course vote with its cards, the clock as a ring and the draw; the machine choice as a grid with the
machine in 3D and its grades; the start, the race and its result. "Practice while you wait" keeps the seat and calls
the player back in time for the start, the last seconds counted down aloud.

| Gathering | Course vote | Machine |
|---|---|---|
| ![Waiting for racers](media/quick-play-gather.png) | ![Vote for the next course](media/quick-play-vote.png) | ![Pick your machine](media/quick-play-machine.png) |

[quick-play-vote.mp4](media/quick-play-vote.mp4): the vote to start, the course vote and the draw, the machine
choice (22 s).

Nothing that cannot be taken back happens without a question: leaving a lobby, leaving the queue, the main menu from
a lobby (the seat is kept and the lobby's pill brings the player back), ending the game.

| Leave the lobby? | A server's lobbies |
|---|---|
| ![Leave the lobby?](media/quick-play-leave-question.png) | ![The lobbies page](media/quick-play-lobbies.png) |

## Custom lobbies

The list says each lobby's host, rules, how full it is and what it is doing; the player's own lobby says YOUR
LOBBY. Create lobby and the host's Edit rules are an option page in steps: the lobby (name, password, racers), the
race (mode, course, the mode's own rules) and, on request, More rules. Left and right change a row and stop at its
ends; the panel on the right says what the row does.

| Custom lobbies | Step 1, the lobby | Step 2, the race |
|---|---|---|
| ![Custom lobbies](media/custom-lobbies.png) | ![New lobby, step 1](media/custom-rules-lobby.png) | ![New lobby, step 2](media/custom-rules-race.png) |

[lobby-rules.mp4](media/lobby-rules.mp4): option rows changed with the arrow keys.

The main menu asks first wherever something would be lost: a new lobby's rules, the host's unsaved rules, a lobby or
the queue, a server's address being typed, a button being set, a game running.

![Leave the new lobby?](media/main-menu-question.png)

## Practice

The course first (the cups as chips, the courses as cards with their pictures), then its set-up on a page of its
own; B goes back a step.

![Practice: the courses](media/practice.png)

## Garage

The machines as a grid that scrolls, the shelves (Favourites, Stock, Custom), search, the stats' lens and Sort &
filter. The machine under the focus stands large on the right, so moving through the grid, with the pad or the
pointer, shows each machine. A press opens the machine's own page; only **Use this machine** races it. The machine
raced has a green outline and a green name.

![Moving through the Garage](media/garage.webp)

[garage.mp4](media/garage.mp4) (10 s)

| The machines | A machine's page | Colours |
|---|---|---|
| ![The Garage grid](media/garage.png) | ![A machine's page](media/garage-machine.png) | ![The colours panel](media/garage-colours.png) |

The colours come in two steps: the palette, then "Mix your own colour" (the wheel and the hex). The builder for a
custom machine is an option page in two steps, the parts and the pilot, then the colours.

## Replays, More and the profile

Replays: All, Races and Practice with their counts, the replays by date, a replay's panel. More: Mods, Stats and
About. About holds the version, the update, "Update automatically", "Developer mode", What's new and the licences.
The profile: the name, the server, the machine raced, three figures and the last races.

| Replays | More | About |
|---|---|---|
| ![Replays](media/replays.png) | ![More](media/more.png) | ![About](media/more-about.png) |

| Stats | Profile |
|---|---|
| ![Stats](media/more-stats.png) | ![Profile](media/profile.png) |

## Settings

One page: the groups on the left (Basics with the six settings most players change, then Display, Graphics, Sound,
Controller, Online, Social and the others under them), and on the right a window on the group or row the focus is on: its
drawing, what it does, its value now, every choice with the one chosen marked or a number's meter, when a change
takes effect and what it applies to, and something good to know (`SettingInfo`, `SettingFacts`).

| The groups | Sound |
|---|---|
| ![Settings](media/settings.png) | ![Settings › Sound](media/settings-sound.png) |

[settings-hover.mp4](media/settings-hover.mp4): the pointer gives the focus as the pad does, and the window follows.

Sound has the launcher's sounds by kind: menu, notifications and others, each a share of the launcher's volume and
heard as it is set. "Music and game sounds" is the game's volume (the game mixes its music and its sounds itself).
The Controller page has the button icons (the pad's own family by default; a GameCube controller's in its own colours
and sizes) and the confirm button.

## Developer mode

More › About › Developer mode (or Settings) puts a DEV chip in the top bar, two lines of what has the focus in the hint
bar, and the keys at the rows. The chip opens Diagnostics: the build, the files, the connection, the input and the
interface, with Copy diagnostics.

![Diagnostics (local folders hidden in this picture)](media/developer-diagnostics.png)

## Controls

| | Pad | Keyboard | Mouse |
|---|---|---|---|
| Move the focus | D-pad, stick | Arrow keys | The pointer gives the focus where it moves |
| Choose | A (or as the pad's family has it: Settings › Controller › Confirm button) | Enter | Click |
| The page's main action (Ready, Use this machine, Create lobby, Race) | Start | S | The button itself |
| Back | B (first to the page's own list, then off the page) | Escape, Backspace | Back button; forward comes back |
| Areas | L, R (a trigger's deep press) | Q, E; Ctrl+Tab | The top bar |
| A page's sub-tabs (cups, shelves, filters) | R1 | Ctrl+arrows | The chips |
| The page's second action (lobby panel, rules, racers) | R1 where the page has no sub-tabs | Y | |
| Chat | X | X | The chat icon |
| Main menu (asks first where something would be lost) | Y | | The launcher's name |
| Scroll | C-stick | Page Up, Page Down | Wheel |

A text box pressed with the pad opens a keyboard on the screen.

## Sounds

Also on this branch: the launcher's sounds go out on one stream that is kept running, so more than five quick sounds
in a row (the pad's cursor down a page, the draw's hops) no longer end in silence for some two seconds. Before, every
sound went through Windows' `PlaySound`, a device opened and closed each time. Now `SoundLine` (Launcher.Core) mixes
10 ms blocks (`SoundMix`) onto one waveOut stream (`WaveOutStream`, Launcher.App), a new sound cuts the one before
it with a 4 ms fade, the stream closes after 5 s of silence, and every sound is brought to 44100 Hz. Both
interfaces use it.

In the new interface the pointer moving onto a control sounds the menu's cursor as the pad's move does, and a large
button (the hub's entries, More's cards, Settings' groups) sounds the game's confirm.

## Where the code is

| Folder or file | What it is |
|---|---|
| `net/src/Launcher.App/Views/NextUi/` | The views ([its README](../../net/src/Launcher.App/Views/NextUi/README.md)) |
| `net/src/Launcher.App/ViewModels/NextUi/` | What the new interface shows that the classic one has no page for ([README](../../net/src/Launcher.App/ViewModels/NextUi/README.md)) |
| `net/src/Launcher.App/Controls/NextUi/` | Its controls: `HoloFlag`, `PageDeck`, `StepTrail`, `OptionButton`, `OnlineCount`, `ClockRing`, `Chamfer` ([README](../../net/src/Launcher.App/Controls/NextUi/README.md)) |
| `net/src/Launcher.App/Theme/NextUi/Theme.axaml` | Theme v3: every colour, size and duration of the new interface ([README](../../net/src/Launcher.App/Theme/NextUi/README.md)) |
| `net/src/Launcher.App/ViewLocator.cs` | Gives a page its new view while the setting is on, else the classic one |
| `net/src/Launcher.Core/` | `ShowcaseShots`, `EngineFlames`, `LobbyRecall`, `SoundLine`, `SoundMix`, `Pads/PadIcons`, the new settings |
| `net/tests/Launcher.Tests/App/Next*Tests.cs` | The new interface's tests, headless ([README](../../net/tests/Launcher.Tests/App/README.md)) |

Pages shown before stay in the window, hidden (`PageDeck`), and the large ones are made ahead while the window is
idle, so a change of page does not build and lay out the page anew.

## Tests

New: `ExperimentalUiTests`, `NextShellTests`, `NextQuickPlayTests`, `NextPlayTests`, `NextCustomTests`,
`NextGarageTests`, `NextMoreTests`, `NextSettingsTests`, `NextFirstStartTests`, `NextPolishTests` (text contrast of
4.5:1 measured from the theme, 150 % and Reduced motion), `NextTipsTests` (every option row and setting has its own
line), `SettingInfoTests`, `ShowcaseShotsTests`, `SoundLineTests`; the older tests follow the new views where they
show.

On the PC these pictures were taken on (Windows 11, SDK 10.0.401), the whole suite ran 1565 tests: 1464 passed, 86
were skipped, 15 failed. 13 of them fail the same way on unchanged `main` (a576238) on that PC:
`StackStampBuildCheckTests` (8; they start `dotnet` themselves), `LastResortTests.TheLauncherWritesAnErrorNothingCaught`
(3; "·" comes back as "�"), `BoardTests.TheBoardIsAFileOfTheLaunchersFolderThatGoesWithIt` and
`InterfaceScaleTests.TheWindowsLeastSizeGrowsAndAWindowTooSmallScrolls`. The other two,
`HomeJoinLobbyTests.ALobbyJoinedWhileItsMatchStartsIsWaitedIn` and `DroppedConnectionTests`, are timing: they
pass run alone.

## How these pictures were made

Debug build of this branch on Windows 11, `--dev-service` (the stand-in service, five stand-in racers), a data
folder with `experimentalUi` on and the owner's disc image, the window at 1600×900, captured with Pillow and ffmpeg
(`gdigrab`); videos re-encoded to H.264 at 1280×720 and two of them as animated WebP. On the stand-in service races do
not start, and this data folder had no replays or races, so Replays and Stats are empty. The stand-in skips the
first start, so it is not pictured here (its three steps are the disc and the game prepared, the name, the
controller). The machines and course pictures are drawn from the player's own disc; no game file is in this folder.
