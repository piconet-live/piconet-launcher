using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Settings page's "Input delay" (each player picks 0, 1 or 2; 2 unless they pick less),
/// "Full screen mode" (Borderless or Exclusive; the one default is <see cref="LauncherSettings.FullScreenDefault"/>)
/// "Vertical sync" (off unless switched on) and "Refresh rate (experimental)" (60 Hz unless the player picks 120,
/// 240 or the display's), pressed with the headless window's mouse: a label and its values,
/// the setting kept as it is pressed, and what a start of the game (a script here that stands for the runner) is
/// given in its launch file: the kind of full screen, always; vertical sync when it is on; nothing of how pictures are shown for a player, whose
/// game shows them at a console's moment by itself; the way written into the settings file by hand, "field", when
/// it is one of the two kept for comparison.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class DelaySettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Started => Path.Combine(_dir, "started.txt");

    /// <summary>The launcher's state with a game that is "installed": a script that writes down its arguments.</summary>
    private AppState State(string settings)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), settings);
        var state = AppState.Load(data);
        var runner = Path.Combine(_dir, "runner.sh");
        // as the game's program: asked as the controller helper (the window's pad reader, while the window is in
        // front), it says nothing and ends when its input closes; any other start is a game's
        File.WriteAllText(runner, "#!/bin/sh\nif [ \"$1\" = --controllers ]; then cat > /dev/null; exit 0; fi\n"
            + $"printf '%s\\n' \"$@\" > '{Started}.part'\nmv '{Started}.part' '{Started}'\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
        Assert.Null(RecompLaunch.Problem(state.GamePaths));
        return state;
    }

    /// <summary>The launch file of the game a press of "Play F-Zero GX" starts, read once the game was started.</summary>
    private JsonObject LaunchFileOfAStart(AppState state)
    {
        File.Delete(Started);
        Assert.Null(state.PlayOffline());
        Pump(() => File.Exists(Started), "the game was not started");
        var arguments = File.ReadAllLines(Started);
        var file = arguments[Array.IndexOf(arguments, "--config") + 1];
        var launch = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Pump(() => !state.Racing);
        return launch;
    }

    // The refresh rate, experimental: a label and four values, each above 60 with its frame of delay, the game's 60
    // until the player picks; 60 writes nothing into the
    // launch file. It is left at 240.
    private void TheRefreshRateRow(Window window, Control view, AppState state)
    {
        var refresh = Named<ListBox>(view, "RefreshRate");
        Assert.Equal(["60 Hz", "120 Hz", "240 Hz", "Display"],
            refresh.Items.Cast<string>());
        Assert.Equal((0, "60"), (refresh.SelectedIndex, state.Settings.RefreshRate));
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Refresh rate (experimental)");
        Assert.Null(LaunchFileOfAStart(state)["refreshRate"]);
        Click(window, Value(refresh, 3));
        Assert.Equal((3, "display"), (refresh.SelectedIndex, state.Settings.RefreshRate));
        Assert.Equal("display", (string?)LaunchFileOfAStart(state)["refreshRate"]);
        Click(window, Value(refresh, 0));
        Assert.Null(LaunchFileOfAStart(state)["refreshRate"]);
        Click(window, Value(refresh, 2));
        Assert.Equal("240", (string?)LaunchFileOfAStart(state)["refreshRate"]);
    }

    private static Control Value(ListBox list, int index)
    {
        var value = list.ContainerFromIndex(index);
        Assert.NotNull(value);
        return value;
    }

    /// <summary>"Widescreen" on Game: Off, Game's and True, on the choice the settings file had (an old file's
    /// true: Game's), each kept as it is pressed and read back at the next start.</summary>
    [Fact]
    public async Task TheWidescreenChoiceIsPressedOnThePageAndKept()
    {
        var cancel = TestContext.Current.CancellationToken;
        var state = State("""{"noServer": true, "widescreen": true}""");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1000, Height = 900 };
                window.Show();
                main.ShowSettings(SettingsTab.Game);
                Dispatcher.UIThread.RunJobs();
                var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
                var wide = Named<ListBox>(view, "Widescreen");
                Assert.True(wide.IsEffectivelyVisible);
                Assert.Equal(["Off", "Game's", "True"], wide.Items.Cast<string>());
                Assert.Equal((1, WidescreenMode.Game), (wide.SelectedIndex, state.Settings.Widescreen));
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Widescreen");
                Click(window, Value(wide, 2));
                Assert.Equal((2, WidescreenMode.TrueWide), (wide.SelectedIndex, state.Settings.Widescreen));
                Click(window, Value(wide, 0));
                Assert.Equal((0, WidescreenMode.Off), (wide.SelectedIndex, state.Settings.Widescreen));
                Click(window, Value(wide, 2));
                window.Close();
                main.Dispose();
            }, cancel);
            var again = AppState.Load(Path.Combine(_dir, "data"));
            Assert.Equal(WidescreenMode.TrueWide, again.Settings.Widescreen);
            Assert.Equal(2, new SettingsViewModel(again).WidescreenIndex);
            again.Shutdown();
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    [Fact]
    public async Task TheInputDelayAndVerticalSyncArePressedOnThePageKeptAndGivenToTheGame()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the game is a shell script here");
        var cancel = TestContext.Current.CancellationToken;
        // (a settings file a tester of the test build left: the way kept for comparison, written by hand)
        var state = State("""{"noServer": true, "present": "field"}""");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1000, Height = 900 };
                window.Show();
                main.ShowSettings(SettingsTab.Online);                                   // the input delay is on Online
                Dispatcher.UIThread.RunJobs();
                var view = window.GetVisualDescendants().OfType<SettingsView>().Single();

                // the input delay: a label and the values 0, 1 and 2, on 2 until the player picks
                var delay = Named<ListBox>(view, "InputDelay");
                Assert.True(delay.IsEffectivelyVisible);
                Assert.Equal(["0", "1", "2"], delay.Items.Cast<string>());
                Assert.Equal((2, 2), (delay.SelectedIndex, state.Settings.InputDelay));
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Input delay");
                Click(window, Value(delay, 0));
                Assert.Equal((0, 0, 0), (delay.SelectedIndex, state.Settings.InputDelay, state.Settings.RaceInputDelay));
                Click(window, Value(delay, 1));
                Assert.Equal((1, 1), (delay.SelectedIndex, state.Settings.InputDelay));

                // vertical sync and full screen, on Game
                main.ShowSettings(SettingsTab.Game);
                Dispatcher.UIThread.RunJobs();
                Assert.False(delay.IsEffectivelyVisible);
                // vertical sync: a label and two values, off until switched on
                var sync = Named<ListBox>(view, "VerticalSync");
                Assert.True(sync.IsEffectivelyVisible);
                Assert.Equal(["Off", "On"], sync.Items.Cast<string>());
                Assert.Equal((0, false), (sync.SelectedIndex, state.Settings.VerticalSync));
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Vertical sync");
                // full screen: a label and two values, on the default until the player picks
                var full = Named<ListBox>(view, "FullScreen");
                Assert.Equal(["Borderless", "Exclusive"], full.Items.Cast<string>());
                var byDefault = LauncherSettings.FullScreenDefault == LauncherSettings.FullScreenExclusive ? 1 : 0;
                Assert.Equal((byDefault, LauncherSettings.FullScreenDefault), (full.SelectedIndex, state.Settings.FullScreen));
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Full screen mode");
                // a start as it is: no vertical sync in the game's launch file; "field" from the settings file; the
                // kind of full screen, said whichever it is
                var launch = LaunchFileOfAStart(state);
                Assert.Null(launch["verticalSync"]);
                Assert.Equal("field", (string?)launch["present"]);
                Assert.Equal(LauncherSettings.FullScreenDefault, (string?)launch["fullScreen"]);
                Click(window, Value(full, 1));
                Assert.Equal((1, "exclusive"), (full.SelectedIndex, state.Settings.FullScreen));
                Assert.Equal("exclusive", (string?)LaunchFileOfAStart(state)["fullScreen"]);
                Click(window, Value(full, 0));
                Assert.Equal((0, "borderless"), (full.SelectedIndex, state.Settings.FullScreen));
                Assert.Equal("borderless", (string?)LaunchFileOfAStart(state)["fullScreen"]);
                Click(window, Value(full, 1));
                Click(window, Value(sync, 1));
                Assert.Equal((1, true), (sync.SelectedIndex, state.Settings.VerticalSync));
                launch = LaunchFileOfAStart(state);
                Assert.True((bool)launch["verticalSync"]!);
                // a player's settings name no way of showing pictures: the launch file says nothing, and the game's
                // own way is a console's moment
                state.Update(s => s with { Present = new LauncherSettings().Present });
                launch = LaunchFileOfAStart(state);
                Assert.Null(launch["present"]);
                Assert.True((bool)launch["verticalSync"]!);
                Click(window, Value(sync, 0));
                Assert.False(state.Settings.VerticalSync);
                Assert.Null(LaunchFileOfAStart(state)["verticalSync"]);
                TheRefreshRateRow(window, view, state);
                window.Close();
                main.Dispose();
            }, cancel);

            // kept for the next start; the page opens on them
            var again = AppState.Load(Path.Combine(_dir, "data"));
            Assert.Equal((1, false, "console", "exclusive"),
                (again.Settings.InputDelay, again.Settings.VerticalSync, again.Settings.Present, again.Settings.FullScreen));
            Assert.Equal((1, 1), (new SettingsViewModel(again).InputDelayIndex, new SettingsViewModel(again).FullScreenIndex));
            Assert.Equal(("240", 2), (again.Settings.RefreshRate, new SettingsViewModel(again).RefreshRateIndex));
            again.Shutdown();
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }
}
