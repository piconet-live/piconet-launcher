using System.IO.MemoryMappedFiles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.Views;
using Launcher.Core;
using Launcher.Core.Board;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// "Show inputs" as the Inputs window (<see cref="InputsKeeper"/>, <see cref="InputsWindow"/>), with a script that stands
/// for the game and a made-up game's side of its status board: on, the window opens with the start, titled "Inputs", and
/// draws the pad the game wrote on its board, 60 times a second; it is at rest where the game holds no pad and
/// once the game has ended, and stays open between races; off, it closes and its place is kept, and on again while a
/// game runs it opens there; it closes with the launcher's window. Off, a start opens nothing. With INPUTS_SHOTS naming
/// a folder, the window is kept there as PNGs.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class InputsWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Started => Path.Combine(_dir, "started.txt");

    private string Stop => Path.Combine(_dir, "stop");

    /// <summary>A launcher whose game is a script: asked as the controller helper it ends at once; as a game it says it
    /// started and runs until the test says stop.</summary>
    private AppState State(string settings)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), settings);
        var state = AppState.Load(data);
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, "#!/bin/sh\nif [ \"$1\" = --controllers ]; then cat > /dev/null; exit 0; fi\n"
            + $"touch '{Started}'\nwhile [ ! -f '{Stop}' ]; do sleep 0.05; done\n");
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

    private void StartGame(AppState state)
    {
        File.Delete(Started);
        File.Delete(Stop);
        Assert.Null(state.PlayOffline());
        Pump(() => File.Exists(Started) && state.Board is not null, "the game was not started");
    }

    private void EndGame(AppState state)
    {
        File.WriteAllText(Stop, "");
        Pump(() => !state.Racing, "the game did not end");
    }

    /// <summary>The window after a look, as its timer's (a headless test's dispatcher runs no timers).</summary>
    private static InputsWindow Looked(InputsWindow window)
    {
        window.Look();
        return window;
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("INPUTS_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(200);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [Fact]
    public async Task TheInputsWindowOpensWithAStartAndDrawsTheGamesPad()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the game is a shell script here");
        var state = State("""{"noServer": true, "showInputs": true}""");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var keeper = new InputsKeeper(state);
                Assert.Null(keeper.Window);

                StartGame(state);
                Pump(() => keeper.Window is { IsVisible: true }, "the Inputs window did not open with the start");
                var window = keeper.Window!;
                Assert.Equal("Inputs", window.Title);
                PumpFor(100);   // (the game has not taken its board yet: nothing to read, the pad at rest)
                Assert.Null(window.Shown);
                Shot(window, "inputs-at-rest");

                // the game takes its board and writes the player's pad: A and Z held, the stick up and left, R half in
                using (var game = new GameSide(state.Board!.Path))
                {
                    game.Pad(1, 0x0110, -50, 50, 0, 59, 0, 75);
                    Pump(() => Looked(window).Shown is not null, "the window did not draw the game's pad");
                    var shown = window.Shown!;
                    Assert.Equal((-50, 50, 0, 59, 0, 75), (shown.StickX, shown.StickY, shown.CStickX, shown.CStickY, shown.L, shown.R));
                    Assert.Equal(["A", "Z"], shown.Buttons.Order(StringComparer.Ordinal));
                    Shot(window, "inputs-live");

                    // a replay or a watched race: the game holds no pad of the player's
                    game.Pad(2, 0, 0, 0, 0, 0, 0, 0, held: false);
                    Pump(() => Looked(window).Shown is null, "the window kept a pad the game no longer holds");
                    game.Pad(3, 0x1000, 72, 0, 0, 0, 150, 0);
                    Pump(() => Looked(window).Shown is { L: 150 }, "the window did not draw the next pad");
                }

                // the game ends: the window stays, at rest
                EndGame(state);
                Pump(() => Looked(window).Shown is null, "the window kept the pad of a game that ended");
                Assert.Same(window, keeper.Window);

                // off: it closes, and where it stood is kept
                state.Update(s => s with { ShowInputs = false });
                Pump(() => keeper.Window is null, "switched off, the window stayed");
                Assert.False(window.IsVisible);
                var place = state.Settings.InputsWindow;
                Assert.NotNull(place);
                Assert.Equal((480, 256), (place.Width, place.Height));

                // off, a start opens nothing; on while the game runs, it opens at once
                StartGame(state);
                PumpFor(100);
                Assert.Null(keeper.Window);
                state.Update(s => s with { ShowInputs = true });
                Pump(() => keeper.Window is { IsVisible: true }, "switched on in a game, the window did not open");
                EndGame(state);

                // the player closes it: the next start opens it again
                keeper.Window!.Close();
                Pump(() => keeper.Window is null);
                StartGame(state);
                Pump(() => keeper.Window is { IsVisible: true }, "the next start did not open the window again");
                EndGame(state);

                // it goes with the launcher's window
                var last = keeper.Window!;
                keeper.Dispose();
                Pump(() => !last.IsVisible, "the window outlived the launcher's");
                Assert.Null(keeper.Window);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.WriteAllText(Stop, "");
            await ui.DisposeAsync();
            state.Shutdown();
        }
        // and kept for the next run
        var again = AppState.Load(Path.Combine(_dir, "data")).Settings.InputsWindow;
        Assert.Equal((480, 256), (again?.Width, again?.Height));
    }

    /// <summary>A place on no screen, or a size out of bounds (a file edited by hand): the window's own, centred.</summary>
    [Fact]
    public async Task APlaceOrSizeThatCannotBeIsLeft()
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var window = new InputsWindow(() => null, new InputsPlace(-90_000, -90_000, 5, 1_000_000));
                Assert.Equal((480d, 256d), (window.Width, window.Height));
                Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
                var kept = new InputsWindow(() => null, new InputsPlace(0, 0, 640, 320));
                Assert.Equal((640d, 320d), (kept.Width, kept.Height));
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    /// <summary>The game's side of its board: takes it and writes the LocalPad block as the core's board.cpp does.</summary>
    private sealed unsafe class GameSide : IDisposable
    {
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly BoardView _view;

        public GameSide(string path)
        {
            _map = MemoryMappedFile.CreateFromFile(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete),
                null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            _accessor = _map.CreateViewAccessor();
            byte* at = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
            _view = new BoardView(at + _accessor.PointerOffset);
            _view.HeaderRunnerSize = BoardLayout.Size;
            _view.HeaderRunnerVersion = BoardLayout.Version;
        }

        public void Pad(uint serial, uint buttons, int stickX, int stickY, int cStickX, int cStickY, uint l, uint r, bool held = true) =>
            _view.WriteLocalPad(new BoardLocalPad
            {
                Serial = serial, Held = held ? 1u : 0u, Buttons = buttons, StickX = stickX, StickY = stickY, CStickX = cStickX, CStickY = cStickY, L = l, R = r,
            });

        public void Dispose()
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _map.Dispose();
        }
    }
}
