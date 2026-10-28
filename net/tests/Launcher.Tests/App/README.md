# Launcher.Tests/App

The few tests of what only `Launcher.App`'s own code does, run without a window (Avalonia.Headless).

- `HeadlessApp.cs`: not a test: the application without a window and the collection its tests run in.
- `GameSkinTests.cs`: the launcher's start with a disc image whose art cannot be read: the application with its
  theme, the settings and `GameSkin.Load`. Put a headless session away with `DisposeAsync`; a `using` waits for
  itself for ever.
- `FirstScreenTests.cs`: the first screen in the launcher's window, with the mouse: CONTINUE prepares the game's
  files from a made-up image and the launcher opens; a refused image, Cancel, too little space and another
  preparation at work, each said in the preparation's sentence with the screen still usable. A preparation is held
  where it asks for the free space, so what shows meanwhile is looked at. `.Start.cs`: the start with files that
  are ready, another online part's, damaged or missing. `.Buttons.cs`: RACE and "Play F-Zero GX" off with the
  reason while a start lacks the game's files or the game program. `.Stopped.cs`: Cancel and the window's close in
  the middle of a copy (held there through `AppState.PreparationWatch`), a preparation that cannot succeed, and a
  game that becomes startable while the launcher is open. A player copy is made up with `Install.Of`.
- `CommandLineTests.cs`: the command line: the modes and their files, a launch file's test settings, and every
  option that is not listed refused.
- `LanJoinTests.cs`: the Local network tab's Join needs the disc and the whole game, as RACE does; without them
  nothing is joined and the page says why
- `ConnectionBoxTests.cs`: the connection box laid out: without a stack's stamp it reads as version 0.1.0's; a
  stack's build names the stack, a test build has its mark
- `NameInTheColumnTests.cs`: the name changed as a player does it: click the name in the left column, type, click
  Save. Its window has the toolkit's layer for menus, which the launcher's window theme lacks.
- `GarageSortTests.cs`: the Garage's stats and order on a made-up disc, pressed in the launcher's own window: the
  heading row's words, a stat's row (twice), a machine's cell; the order kept for the next start, no id changed; and
  a damaged car data file, with which the Garage still works; and another disc named in Settings while it runs.
- `PracticePageTests.cs`: Play's Practice page pressed: its heading, a cup's chip and a card, Laps to its end,
  START PRACTICE off, the Garage button and back; a rules panel with every kind of row, each pressed; the rows'
  ranges; a course picker as a pool (None, a cup, cards in and out, All).
- `ReplayTabsTests.cs`: the Replays page's tabs All, Races, Practice with their counts, pressed; a practice run's row
  ("Practice", the time, no Results); the header's word that makes a replay a practice run; "•••" open on one row
  at a time, and a row deleted leaving the others as they were; the rows under their date headings (Today, then
  each month), in the tab shown.
- `Headless.cs`: what the newer tests share: a press with the headless window's mouse, a key, waiting for the page, a
  window with the toolkit's layer for menus.
- `CustomLobbiesTests.cs`: custom lobbies pressed in the launcher's window: on the stand-in the browser, a locked
  row's password (wrong, then right), the lobby as a joiner (the host's mark, the rules that differ, All rules), back
  to the browser, Create with Edit rules, the lobby as host (Edit rules, START RACE, Kick); on a led server the racer
  sent with the join, no queue, the host's controls the host's only, a new host shown, a kick back to the browser
  with why; a custom lobby's own machine setting (0 fixed, each player's own) in the Garage and the choice, and its
  All rules closed on another page. With `LOBBY_SHOTS` set to a folder, the pages are kept there as PNGs.
- `LobbyPackLineTests.cs`: the lobby's line and notice with a stat pack, on a led server: a label of a forty-character
  name and author cut to the room that is left, and a refused pack's sentence whole in the notice until its cause is gone
- `LedServer.cs`, `LobbyStageRulesTests.cs`: a server the test leads message by message and that keeps what the
  launcher sends; with it, what the page shows is what races: a ready player stays ready with every pick and colour;
  a stage the server closed is closed on the page, sends nothing and shows the server's machine; the last send
  leaves room for a late message; a second session's match of the same name is its own; a dropped connection takes
  the server's machine list with it; a machine picked in the Garage is the player's everywhere.
- `GarageFitTests.cs`: the Garage tab and the lobby's machine stage at window sizes from 1280 x 720 to wide and low
  ones, at 100%, 125% and 130%, and after the window or the page changed under the stage: the 3D view and the column
  beside it end above the cells, the cells inside the page, the page does not scroll, the 3D view is 260 px at least.
- `GarageFavouritesTests.cs`: hearts pressed on cells and over the 3D view, the filter in the sorted order, nothing
  marked showing every machine, the marks and the filter kept for the next start.
- `DelaySettingsTests.cs`: the Settings pages' "Input delay" (0, 1, 2), "Full screen mode" (Borderless, Exclusive) and "Vertical sync" (Off, On) pressed with
  the mouse, kept, and what a start of the game (a script) is given in its launch file: vertical sync when on, the
  settings file's way of showing pictures only when it is one of the two kept for comparison
- `VolumeSettingsTests.cs`: the Settings page's game volume moved with the mouse and the keys, then in the game's own
  settings at a start of the game (a script); the launcher's volume heard when set, and Mute. In the Controller
  page's collection: a start of the game ends every controller helper of the process.
- `SettingsTabsTests.cs`: the Settings tab's four pages pressed by their headings, each with its own rows; the page
  last chosen kept for this run and the next, a page the file names that is none of them read as Game; RACE
  without a disc and Home's "Choose your disc" open Launcher, RACE without a server Online.
- `InterfaceScaleTests.cs`: the interface scale: the file's value read defensively, the keys' steps and ends, the
  slider dragged changing nothing until Apply, the keys acting at once from a text box, the window's least size
  and its scrolling, the 3D views in `Unscaled`, a menu at the scale of where it opens.
- `NameMenuTests.cs`: the name's menu by itself: Save, Enter, Cancel, and a name the game cannot show.
- `SetUpInGameTests.cs`: the Controller page's "Set up in the game" pressed with the headless window's mouse: the
  game (a script here) is started as offline play is, with `controllerSetup` in its launch file; the button is off,
  with its reason, with no disc image, with no game, and while a game is open. One application for the class, in a
  collection that runs alone, and a page without links.
- `SetUpInGameTests.QuickPlay.cs`: the same button in Quick Play, against a coordinator the test leads by the hand
  (`LedCoordinator`: it takes the connection, sends and drops it when told): off with its reason in a lobby and in a
  race; following a connection made and lost while the page is open; and a join that lands while the press waits
  for the controllers' check starts no game. And offline play's own start (`AppState.PlayOffline`) starts none in
  Quick Play.
- `SetUpInGameTests.Joining.cs`: a join on its way counts as Quick Play for every game start, is given up after
  its limit, and can be left; with a game open RACE and the Local network tab's Join join nothing and say why
- `LobbyPingTests.cs`: each racer's ping in the lobby's list, read from the rows (nothing pressed): a number and its
  unit in a column of its own that the widest takes whole, nothing for a CPU's seat or a racer without a number;
  the server's ping answered at once, also while a silent relay is measured; a list of pings checked as any message.
- `NoticeTierTests.cs`: a server notice in the window by tier: info in the list with the bell's dot; notice's
  banner gone after 10 s (the test's clock); warning's kept until × is pressed, and the flash; urgent's tone at the
  launcher's volume and none when muted; the last hour's sent again neither flash nor sound; the bell's list, closed by a press outside it, Escape or
  another page.

The pages against a real server (`DebugRefusalTests`, `LobbyChoiceOrderTests`, `LobbyVoteTests`, `LobbyChatTests`,
`LeaveLobbyTests`) are in the service's `Integration.Tests` (in the monorepo `../../Integration.Tests`).
- `PadNavigationTests.cs`: the pad working the launcher's window, its intents given to the window's navigator: the
  first press only shows where the pad is; the D-pad by position; L, R, Z and B through the tabs and pages; A on a
  row of choices; a slider stepped and held at its end; Start on RACE; the question over the page keeps the pad;
  no text box, no name, no window button is ever stopped on; the mouse hides the mark; the pad's readings work the
  pages only with Settings' "Controller navigation" on (off by default).
- `UpdateTimesTests.cs`: when the open launcher looks for an update again: two hours and up to five minutes after
  the last look, and at a game's end or leaving Quick Play only when the last look is over half an hour old.
