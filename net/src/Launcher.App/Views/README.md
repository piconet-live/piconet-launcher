# Launcher.App/Views

The look of each page and nothing else: an `.axaml` file a view, on the classes of `../Theme/Theme.axaml`, bound to
its view model in `../ViewModels` (`../ViewLocator.cs` pairs them). A view's `.axaml.cs` holds only what is the
view's own: sizes that answer the window, a key, a file picker.

| File | What it is |
|---|---|
| `MainWindow.axaml` | The window: its title bar, the left column, the tabs and the page showing |
| `RacerView`, `PlayControlView`, `ConnectionView` | The left column's parts: the player, the play control, the connection dot |
| `NameMenu` | The menu that changes the player's name; whoever shows it calls `Begin` when it opens and closes it at `Done`. It saves, then closes (closed first, its Save would do nothing) |
| `DiscView` | The first screen |
| `HomeView`, `LobbyView`, `LobbyChat`, `LanView` | The Play tab's pages (the lobby's shows the Garage's view under its line in the machine stage); the lobby's chat, whose lines can be selected and copied |
| `GarageView`, `ReplaysView`, `StatsView`, `SettingsView`, `ControllerView` | The other tabs' pages (Stats is the Replays tab's second) |
| `NoticeCard`, `NoticeBanner`, `NoticesList` | A server notice, one look in the chat, the banner and the list (its tier's tint, bar, icon and label); the banner under the title bar; the bell's list |
| `Converters.cs` | From a view model's plain values to what a view draws (a "#RRGGBB" to a brush) |
| `DiscPicker.cs` | The system's file picker for the disc image |
