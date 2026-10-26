using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The collection of the Controller page's tests, which run with nothing beside them: each starts the
/// launcher's application without a window (a process holds one) and the game's program as the controller helper
/// (the helpers of one process are one set).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ControllerPageTests
{
    public const string Name = "the Controller page";
}

/// <summary>One application without a window for every test of the class. The toolkit's own shared objects (the
/// underline of a link's text is one) belong to the thread that first draws them, so a page drawn in a second
/// application of the process fails there; the page's tests draw in this one.</summary>
public sealed class OneHeadlessApplication : IAsyncDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

/// <summary>
/// The Controller page's "Set up in the game" (<see cref="ControllerView"/>), pressed as a player presses it: with
/// the headless window's mouse, on the button that reads so. What is checked is what the player gets: the game
/// started as offline play is, with its launch file asking for the game's own controller options, and the button
/// off with its reason while the game cannot be started. The game is a script that stands for the runner: it answers
/// as the controller helper when it is asked to be one, and as a game it writes down what it was started with and
/// stays open a moment. The button in Quick Play, in a race and while a connection is made or lost:
/// SetUpInGameTests.QuickPlay.cs.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed partial class SetUpInGameTests(OneHeadlessApplication application) : IDisposable, IClassFixture<OneHeadlessApplication>
{
    private string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs.Append(_dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A new folder for the next launcher of a test (its data, its game, its record of a start).</summary>
    private void AnotherFolder()
    {
        _dirs.Add(_dir);
        _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
    }

    private string Started => Path.Combine(_dir, "started.txt");

    /// <summary>While this file is there, the script that stands for the game does not answer as the controller
    /// helper: the check before a start waits.</summary>
    private string Hold => Path.Combine(_dir, "hold");

    /// <summary>While this file is there, the script that stands for the game stays open.</summary>
    private string Stay => Path.Combine(_dir, "stay");

    /// <summary>The launcher's state in a data folder of this test: no server, and, as asked, a disc image that
    /// passes the first look and a game that is "installed".</summary>
    private AppState State(bool disc, bool game)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { noServer = true }));
        var state = AppState.Load(data);
        if (disc)
        {
            state.Update(s => s with { DiscPath = new SyntheticDisc().Write(_dir, "gx.iso") });
        }
        // Port 1 is Off, so the page shows no mapping table: its "Set" links are underlined, and the toolkit's
        // underline belongs to the first application of the process that drew one (another class's, in a whole
        // run). A page that draws one here fails with "a different thread owns it".
        ControllerFiles.Write(state.GamePaths.UserFolder, 0, PortSettings.Off);
        if (!game)
        {
            return state;
        }
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, $$"""
            #!/bin/sh
            if [ "$1" = --controllers ]; then
              while [ -e '{{Hold}}' ]; do sleep 0.05; done
              echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}'
              echo '{"is":"ports","ports":[{"kind":0,"device":"","connected":false}]}'
              echo '{"is":"ready","version":1}'
              cat > /dev/null
              exit 0
            fi
            printf '%s\n' "$@" > '{{Started}}.part'
            mv '{{Started}}.part' '{{Started}}'
            sleep 1.5
            while [ -e '{{Stay}}' ]; do sleep 0.05; done

            """.Replace("            ", "", StringComparison.Ordinal));
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

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), TopLevel.GetTopLevel(control)!)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What the player sees of the button: whether it can be pressed, and the line under it.</summary>
    private sealed record Seen(bool Enabled, string? Note);

    /// <summary>The page in a window, as the Settings tab shows it; <paramref name="steps"/> get the window, the
    /// button and a look at it, on the application's thread.</summary>
    private Task<T> OnThePage<T>(AppState state, Func<Window, Button, Func<Seen>, Task<T>> steps) =>
        application.Session.Dispatch(async () =>
            {
                var page = new ControllerViewModel(state);
                var view = new ControllerView { DataContext = page };
                var window = new Window { Content = view, Width = 1000, Height = 800 };
                window.Show();
                page.Shown();
                Dispatcher.UIThread.RunJobs();
                var button = view.FindControl<Button>("SetUpInGameButton")!;
                var note = view.FindControl<TextBlock>("SetUpInGameNote")!;
                try
                {
                    Assert.Equal("Set up in the game", button.Content);
                    return await steps(window, button, () => new Seen(button.IsEffectivelyEnabled, note.Text));
                }
                finally
                {
                    page.Hidden();
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);

    /// <summary>Waits on the application's thread, which goes on working meanwhile, until <paramref name="done"/>.</summary>
    private static async Task<bool> Until(Func<bool> done, int seconds = 20)
    {
        for (var i = 0; i < seconds * 20 && !done(); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        return done();
    }

    [Fact]
    public async Task TheButtonStartsTheGameOnItsControllerOptions()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;   // the game here is a shell script
        }
        var state = State(disc: true, game: true);
        var (before, started, during, after) = await OnThePage(state, async (window, button, look) =>
        {
            var before = look();
            Click(window, button);
            var started = await Until(() => File.Exists(Started));
            Dispatcher.UIThread.RunJobs();
            var during = look();
            Click(window, button);   // a second press while the game is open starts nothing
            await Until(() => !state.Racing);
            await Until(() => look().Enabled, 5);
            return (before, started, during, look());
        });

        Assert.Equal(new Seen(true, ControllerViewModel.SetUpInGameWhat), before);
        Assert.True(started);
        // the game was started as offline play is: its launch file in its user folder, named to the runner
        var arguments = File.ReadAllLines(Started);
        var launch = GameLaunchFile.PathFor(state.GamePaths);
        Assert.Equal(launch, arguments[Array.IndexOf(arguments, "--config") + 1]);
        Assert.DoesNotContain("--controllers", arguments);
        var file = JsonNode.Parse(File.ReadAllText(launch))!.AsObject();
        // every member: the start asks for the controller options, the game window's strip has the launcher's name,
        // the launcher's status board for this game is named, and nothing of a test is on
        Assert.Equal(["about", "lanes", "controllerSetup", "windowName", "showFrameRate", "fullScreen", "board"], file.Select(m => m.Key));
        Assert.EndsWith(".board", (string?)file["board"], StringComparison.Ordinal);
        Assert.True((bool)file["controllerSetup"]!);
        Assert.Equal("Piconet Launcher: the game's controller options", (string?)file["about"]);
        // while the game is open the button is off and says that the game closes by itself; then it is back
        Assert.Equal(new Seen(false, "Open on the controller options"), during);
        Assert.Equal(new Seen(true, ControllerViewModel.SetUpInGameWhat), after);
        Assert.Single(File.ReadAllLines(Started), "--config");   // one start: the second press did nothing
    }

    [Fact]
    public async Task TheButtonIsOffWithItsReasonWhileTheGameCannotStart()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        // no disc image
        var none = State(disc: false, game: true);
        var seen = await OnThePage(none, async (window, button, look) =>
        {
            Click(window, button);
            await Until(() => File.Exists(Started), 1);
            return look();
        });
        Assert.Equal(new Seen(false, "No disc image"), seen);
        Assert.False(File.Exists(Started));
        Assert.False(File.Exists(GameLaunchFile.PathFor(none.GamePaths)));

        // a disc image, and the game's files are not there: off, and the reason is the left column's alone
        AnotherFolder();
        var bare = State(disc: true, game: false);
        var problem = RecompLaunch.Problem(bare.GamePaths);
        Assert.NotNull(problem);
        seen = await OnThePage(bare, (_, _, look) => Task.FromResult(look()));
        Assert.Equal(new Seen(false, ""), seen);

        // a game is open already (offline play, started before the page shows)
        AnotherFolder();
        var open = State(disc: true, game: true);
        Assert.Null(open.PlayOffline());
        Assert.True(File.Exists(Started) || await Task.Run(async () =>
        {
            for (var i = 0; i < 100 && !File.Exists(Started); i++)
            {
                await Task.Delay(50);
            }
            return File.Exists(Started);
        }, TestContext.Current.CancellationToken));
        var launched = File.ReadAllText(GameLaunchFile.PathFor(open.GamePaths));
        var (while_open, pressed) = await OnThePage(open, async (window, button, look) =>
        {
            var seen = look();
            Click(window, button);
            await Until(() => false, 1);
            return (seen, File.ReadAllText(GameLaunchFile.PathFor(open.GamePaths)));
        });
        Assert.Equal(new Seen(false, ""), while_open);   // (the left column says "Game running")
        Assert.Equal(launched, pressed);                                   // nothing was started over it
        Assert.DoesNotContain("controllerSetup", launched, StringComparison.Ordinal);   // offline play asks for no options screen
        open.Fork!.WaitForExit(TimeSpan.FromSeconds(10));
    }
}
