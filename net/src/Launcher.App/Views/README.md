# Launcher.App/Views

The look of each page and nothing else: an `.axaml` file a view, on the classes of `../Theme/Theme.axaml`, bound to
its view model in `../ViewModels` (`../ViewLocator.cs` pairs them). A view's `.axaml.cs` holds only what is the
view's own: sizes that answer the window, a key, a file picker.

| File | What it is |
|---|---|
| `MainWindow.axaml` | The window: its title bar, the left column, the tabs and the page showing (the classic interface, `ClassicShell`); with Settings' "Experimental UI" on it shows `NextUi/NextShell` in that place (`MainWindow.axaml.cs`, `ShowShell`: the pad's page, the height the window is dragged by and the keys go with it), and the question before that is here too |
| `NextUi/` | The redesigned interface's views ("Experimental UI"; off by default), beside the classic ones: the shell, the hub, a list of places |
| `IPadLanding.cs` | A page that says where the pad lands when it shows (the redesigned interface's server choice: the server last used) |
| `PadNavigator.cs`, `PadNavigator.Targets.cs`, `PadActions.cs` | The pad working the window: in the classic interface L and R through the tabs and Z through a tab's pages; in the redesigned one L and R through the areas, X the chat and Y the main menu from anywhere, R1 the page's second action where it has no sub-tabs, B the shell's way back, a press acting at once on what has the focus; the C-stick scrolls up and down and to the side; the redesigned interface's arrow keys move the focus the D-pad's way (`Arrow`), Page Up and Page Down scroll (`ScrollPage`); there B, Escape and Backspace bring the focus back to the page's own list first (`FocusHome`, `PadNav.Home`) and leave the page only from it, while the mouse's back button and the top bar's Back leave it at once; R1 (the GameCube's Z: the standard layout leaves L1 free) and Ctrl with the arrows step through the page's sub-tabs (`StepTab`, `PadNav.Tabs`: Practice's cups, the Garage's shelves, the Replays' filter, Stats' best times, the colours' targets), as L2 and R2 through the areas; a row's value steps left and right and stops at its ends |
| `PadFollow.cs` | The focus after a press goes on to the next step the pressed control names (`PadNav.Next`): a practice course to START PRACTICE, a cup to its courses, a Garage tile to Use, Use back to the tiles, a replay filter to the list; into a list, on the item the list picks |
| `InputsWindow` | "Show inputs": a window of its own, the player's controller live from the game's status board (`InputsKeeper` opens and closes it) |
| `RacerView`, `StartGameView`, `ConnectionView` | The left column's parts: the player, "Start F-Zero GX" with the line saying why it is off, the connection dot |
| `PlayControlView` | Quick Play's action row: RACE, or the lobby's box and Leave Lobby |
| `NameMenu` | The menu that changes the player's name; whoever shows it calls `Begin` when it opens and closes it at `Done`. It saves, then closes (closed first, its Save would do nothing) |
| `DiscView` | The first screen |
| `HomeView`, `LobbyView`, `LanView` | The Play tab's pages (the lobby's shows the Garage's view under its line in the machine stage) |
| `ChatSidebar`, `LobbyChat` | The chat sidebar on every page (its button beside the bell; pinned a column of its own, unpinned over the page; its edge dragged): the lobby's chat, whose lines can be selected and copied, and its message box |
| `GarageView`, `ReplaysView`, `StatsView`, `SettingsView`, `ControllerView` | The other tabs' pages (Stats is the Replays tab's second) |
| `NoticeCard`, `NoticeBanner`, `NoticesList` | A server notice, one look in the chat, the banner and the list (its tier's tint, bar, icon and label); the banner under the title bar; the bell's list |
| `Converters.cs` | From a view model's plain values to what a view draws (a "#RRGGBB" to a brush) |
| `DiscPicker.cs` | The system's file picker for the disc image |
