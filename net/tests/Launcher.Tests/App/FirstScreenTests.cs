using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The first screen (<see cref="DiscView"/>) in the launcher's own window, used as a player uses it: the disc
/// image's path in its box, CONTINUE and Cancel pressed with the headless window's mouse. CONTINUE prepares the
/// game's files from the image (<see cref="DiscPrep"/>) when a game start has none, and the launcher opens when they
/// are ready; a preparation that is refused, stopped, short of space or kept out by another one says so in its own
/// sentence and leaves the screen usable. And the launcher's start: straight in with files that are ready or only
/// another online part's, the first screen with files that are missing or damaged (FirstScreenTests.Start.cs).
/// Made-up images only (<see cref="SyntheticDisc"/>), with their own pins (<see cref="AppState.Preparation"/>); the
/// "game program" is two empty files, which nothing starts. A preparation is held where it asks for the free space,
/// so that what the screen shows meanwhile is looked at, not raced.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed partial class FirstScreenTests : IDisposable
{
    private const string NotPrepared = "Game files not prepared";

    private static readonly string[] Stages = ["CHECKING", "COPYING", "FINISHING"];

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
    private readonly SyntheticDisc _disc = new();
    private readonly ManualResetEventSlim _go = new(true);
    private readonly ManualResetEventSlim _asked = new(false);
    private long? _free;

    public void Dispose()
    {
        _go.Set();
        Directory.Delete(_dir, recursive: true);
        _go.Dispose();
        _asked.Dispose();
    }

    private string Data => Path.Combine(_dir, "data");

    private DiscPins Pins => _disc.Pins();

    private string Game => DiscPrep.GameFolder(Data, Pins);

    /// <summary>What the launcher's preparations are told: the made-up disc's pins, and a free space that waits
    /// while <see cref="_go"/> is not set (the preparation stands still there, its first stage reported).</summary>
    private DiscPrepOptions Options => new()
    {
        Pins = Pins,
        FreeSpace = _ =>
        {
            _asked.Set();
            _go.Wait(TimeSpan.FromSeconds(60));
            return _free;
        },
    };

    /// <summary>A native mod bundle for the made-up disc: its patch list (the executable's pin and one more word)
    /// and an image, which only has to be there.</summary>
    private string Bundle(string name, uint linked = 0x497FD784)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.bin"), name);
        string Entry(uint address, int offset, uint replacement, string what) => string.Create(CultureInfo.InvariantCulture,
            $"{{\"address\": \"{address:X8}\", \"original\": \"{SyntheticDisc.Word(_disc.Dol, offset):X8}\", \"new\": \"{replacement:X8}\", \"what\": \"{what}\"}}");
        var entries = Entry(SyntheticDisc.TextAddress + 0x40, 0x140, 0x38600000, "pin") + ",\n" +
            Entry(SyntheticDisc.TextAddress + 0x80, 0x180, linked, "linked");
        File.WriteAllText(Path.Combine(folder, DiscPrep.PatchListName),
            $"{{\"version\": 1, \"complete\": true, \"image_sha1\": \"{name}\", \"main_rel\": \"801BA660\", \"patches\": [\n{entries}\n]}}");
        return folder;
    }

    /// <summary>The launcher's state in this test's data folder: the game program "installed" (or not), its online
    /// part <paramref name="bundle"/>, the stored disc image (or none), and a server that nobody answers for.</summary>
    private AppState State(string? stored, string? bundle = null, bool program = true, string? server = null)
    {
        Directory.CreateDirectory(Data);
        var (runner, module) = (Path.Combine(_dir, "runner.exe"), Path.Combine(_dir, "module.dll"));
        if (program)
        {
            File.WriteAllText(runner, "");
            File.WriteAllText(module, "");
        }
        var settings = new Dictionary<string, object?>
        {
            ["discPath"] = stored,
            ["noServer"] = server is null,
            ["server"] = server,
            ["recomp"] = new { runner, module, bundle = bundle ?? Bundle("bundle") },
        };
        File.WriteAllText(Path.Combine(Data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var state = AppState.Load(Data);
        state.Preparation = Options;
        return state;
    }

    private PreparedState Prepared(AppState state) => DiscPrep.Status(Data, state.GamePaths.Bundle, Pins).State;

    /// <summary>The game's files prepared by the preparation itself, before a launcher starts.</summary>
    private void PrepareWithoutTheLauncher(string image, string bundle) =>
        Assert.Equal(DiscPrepOutcome.Prepared,
            DiscPrep.Prepare(image, Data, bundle, null, new DiscPrepOptions { Pins = Pins }, TestContext.Current.CancellationToken).Outcome);

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets the window's thread work until <paramref name="done"/>, twenty seconds at most.</summary>
    private static void Until(Func<bool> done, string what)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        Dispatcher.UIThread.RunJobs();
        while (!done() && DateTime.UtcNow < limit)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), "not within twenty seconds: " + what);
    }

    /// <summary>The first screen as the player has it before them: its controls, and what they show.</summary>
    private sealed class Screen(MainWindow window, MainViewModel main)
    {
        public MainWindow Window { get; } = window;

        public MainViewModel Main { get; } = main;

        /// <summary>The first screen is the page showing (null: the launcher's pages are).</summary>
        public DiscView? View => Window.GetVisualDescendants().OfType<DiscView>().SingleOrDefault();

        private T Part<T>(string name) where T : Control => View!.FindControl<T>(name)!;

        public TextBox PathBox => Part<TextBox>("DiscPath");

        public Button Continue => Part<Button>("Continue");

        public Button Cancel => Part<Button>("Cancel");

        public bool Preparing => Part<StackPanel>("Preparing").IsEffectivelyVisible;

        public string? Stage => Part<TextBlock>("Stage").Text;

        public string? Size => Part<TextBlock>("Size").Text;

        public string? Percent => Part<TextBlock>("Percent").Text;

        public double Bar => Part<ProgressBar>("Bar").Value;

        /// <summary>The preparation's whole sentence, as the screen keeps it (the tooltip of its line); null: no line
        /// shows.</summary>
        public string? Said => Part<Grid>("SaidLine") is { IsEffectivelyVisible: true } line ? ToolTip.GetTip(line) as string : null;

        /// <summary>The line's label, as the screen shows it; null: none shows.</summary>
        public string? SaidLabel => Part<TextBlock>("Said") is { IsEffectivelyVisible: true } said ? said.Text : null;

        /// <summary>The launcher's pages show: the left column, the tabs, Quick Play.</summary>
        public bool Opened => Main is { Current: HomeViewModel, Navigation: true } && View is null;

        /// <summary>The window was closed as the application closes it (its pages put away).</summary>
        public bool Closed { get; private set; }

        /// <summary>What the application does when its window is closed: the pages are put away, which stops a
        /// preparation at work and waits a moment for it.</summary>
        public void CloseTheLauncher()
        {
            Closed = true;
            Main.Dispose();
        }

        public void Type(string path)
        {
            PathBox.Text = path;
            Dispatcher.UIThread.RunJobs();
        }

        public void Press(Button button)
        {
            Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, $"{button.Name} cannot be pressed");
            Click(Window, button);
        }
    }

    /// <summary>The launcher's window on this state, as a start makes it, and the steps on the application's
    /// thread. What fails there is carried out and raised here.</summary>
    private static async Task OnTheLauncher(AppState state, Action<Screen> steps)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            var failure = await session.Dispatch(() =>
            {
                MainViewModel? main = null;
                MainWindow? window = null;
                Screen? screen = null;
                try
                {
                    GameSkin.Load(state.Settings.DiscPath);
                    main = new MainViewModel(state, new NobodyAnswers());
                    window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    steps(screen = new Screen(window, main));
                    return null;
                }
#pragma warning disable CA1031 // any failure is the test's result
                catch (Exception e)
#pragma warning restore CA1031
                {
                    return System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                }
                finally
                {
                    window?.Close();
                    if (screen is not { Closed: true })
                    {
                        main?.Dispose();
                    }
                    state.Shutdown();
                    GameSkin.Load(null);                                       // the disc's file is let go
                }
            }, TestContext.Current.CancellationToken);
            failure?.Throw();
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>The server's status asked, and no server there: the launcher's pages do without.</summary>
    private sealed class NobodyAnswers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("no server in this test"));
    }

    [Fact]
    public async Task ContinuePreparesTheGameAndTheLauncherOpens()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: null);
        await OnTheLauncher(state, screen =>
        {
            Assert.NotNull(screen.View);                                       // the first screen, with nothing chosen
            Assert.False(screen.Main.Navigation);
            Assert.False(screen.Continue.IsEffectivelyEnabled);
            Assert.False(screen.Preparing);
            Assert.Null(screen.Said);

            screen.Type(image);
            _go.Reset();                                                       // the preparation will stand still
            screen.Press(screen.Continue);
            Until(() => _asked.IsSet, "the preparation asks for the free space");
            Dispatcher.UIThread.RunJobs();

            // meanwhile: the stage, the size and the percentage with the bar and Cancel, where CONTINUE was
            Assert.True(screen.Preparing);
            Assert.Equal(("CHECKING", "0 MB / 1 MB", "0%", 0d), (screen.Stage, screen.Size, screen.Percent, screen.Bar));
            Assert.False(screen.Continue.IsEffectivelyVisible);
            Assert.True(screen.Cancel.IsEffectivelyEnabled);
            Assert.False(screen.PathBox.IsEffectivelyEnabled);                 // the file is not changed under it
            Assert.NotNull(screen.View);
            Assert.Null(state.Settings.DiscPath);                              // not kept before the image is taken
            Assert.Equal(PreparedState.NotPrepared, Prepared(state));

            // what the screen shows from here on: only stages it knows, and never backwards
            var page = (DiscViewModel)screen.View!.DataContext!;
            var shown = new List<(string Stage, double Fraction)>();
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(DiscViewModel.Fraction) or nameof(DiscViewModel.Stage))
                {
                    shown.Add((page.Stage, page.Fraction));
                }
            };
            _go.Set();
            Until(() => screen.Opened, "the launcher opens");

            Assert.All(shown, s => Assert.Contains(s.Stage, Stages));
            Assert.Equal(shown.Select(s => s.Fraction).Order(), shown.Select(s => s.Fraction));
            Assert.Equal(PreparedState.Ready, Prepared(state));
            Assert.Equal(image, state.Settings.DiscPath);
            Assert.Equal(Game, state.GamePaths.GameFolder);                    // what a game start takes
            Assert.Null(state.GameProblem);
        });
        // and the next start is straight in: the preparation is never seen again
        await OnTheLauncher(State(stored: image), screen => Assert.True(screen.Opened));
    }

    [Fact]
    public async Task ARefusedImageIsSaidInThePreparationsWordsAndAnotherFileCanBeChosen()
    {
        var good = _disc.Write(_dir, "good.iso");
        // one byte of one file is another: the first look, which reads the header, takes it; the preparation does not
        var changed = new SyntheticDisc();
        changed.Files["stage/st01.gma"] = SyntheticDisc.Bytes(40, 70_000);
        var bad = changed.Write(_dir, "changed.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(bad));
        var refusal = DiscPrep.Prepare(bad, Path.Combine(_dir, "elsewhere"), null, null, new DiscPrepOptions { Pins = Pins },
            TestContext.Current.CancellationToken);
        Assert.Equal(DiscPrepOutcome.ModifiedFile, refusal.Outcome);

        var state = State(stored: null);
        await OnTheLauncher(state, screen =>
        {
            screen.Type(bad);
            screen.Press(screen.Continue);
            Until(() => screen.Said is not null, "the refusal is said");

            Assert.Equal(refusal.Message, screen.Said);                        // the preparation's own sentence
            Assert.NotNull(screen.View);                                       // still the first screen, as it was
            Assert.False(screen.Preparing);
            Assert.True(screen.Continue.IsEffectivelyVisible && screen.Continue.IsEffectivelyEnabled);
            Assert.True(screen.PathBox.IsEffectivelyEnabled);
            Assert.Null(state.Settings.DiscPath);                              // a refused image is not kept
            Assert.Equal(PreparedState.NotPrepared, Prepared(state));
            Assert.False(Directory.Exists(Game));

            screen.Type(good);                                                 // another file
            Assert.Null(screen.Said);                                          // what was said of the last one is over
            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens on the good image");
            Assert.Equal(PreparedState.Ready, Prepared(state));
            Assert.Equal(good, state.Settings.DiscPath);
        });
    }

    [Fact]
    public async Task CancelLeavesNothingThatLooksReady()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: null);
        await OnTheLauncher(state, screen =>
        {
            screen.Type(image);
            _go.Reset();
            screen.Press(screen.Continue);
            Until(() => _asked.IsSet, "the preparation asks for the free space");
            Dispatcher.UIThread.RunJobs();

            screen.Press(screen.Cancel);
            Assert.Equal("STOPPING", screen.Stage);
            Assert.False(screen.Cancel.IsEffectivelyEnabled);                  // told once
            Assert.True(screen.Preparing);                                     // until the preparation has let go
            _go.Set();
            Until(() => !screen.Preparing, "the preparation stops");

            Assert.Equal("Stopped · nothing kept", screen.Said);
            Assert.Equal("Stopped · nothing kept", screen.SaidLabel);
            Assert.NotNull(screen.View);
            Assert.Equal(PreparedState.NotPrepared, Prepared(state));
            Assert.Null(DiscPrep.ReadyFolder(Data, state.GamePaths.Bundle, Pins));
            Assert.False(File.Exists(DiscPrep.RecordFile(Data)));
            Assert.False(Directory.Exists(Game));
            Assert.False(Directory.Exists(Game + ".preparing"));
            Assert.Equal(NotPrepared, state.GameProblem);
            Assert.Null(state.Settings.DiscPath);

            screen.Press(screen.Continue);                                     // and it can be done after all
            Until(() => screen.Opened, "the launcher opens after a second try");
            Assert.Equal(PreparedState.Ready, Prepared(state));
        });
    }

    [Fact]
    public async Task TooLittleSpaceAndAnotherLauncherPreparingAreSaidInThePreparationsWords()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: null, server: "ws://127.0.0.1:9/ws");
        await OnTheLauncher(state, screen =>
        {
            screen.Type(image);
            _free = 1 << 20;                                                   // 1 MB free
            screen.Press(screen.Continue);
            Until(() => screen.Said is not null, "too little space is said");
            Assert.StartsWith($"Not enough free space for the game's files in {Data}: they need ", screen.Said, StringComparison.Ordinal);
            Assert.EndsWith(" and 1 MB is free. Free 64 MB more and try again.", screen.Said, StringComparison.Ordinal);
            Assert.NotNull(screen.View);
            Assert.Equal(PreparedState.NotPrepared, Prepared(state));
            Assert.False(Directory.Exists(Game + ".preparing"));

            // another window of the launcher prepares in the same data folder, and stands still half-way
            _free = null;
            _asked.Reset();
            _go.Reset();
            var other = DiscPrep.PrepareAsync(image, Data, state.GamePaths.Bundle, null, Options, TestContext.Current.CancellationToken);
            Until(() => _asked.IsSet, "the other preparation is at work");
            screen.Press(screen.Continue);
            Until(() => screen.Said is not null, "the other preparation is said");
            Assert.Equal(DiscPrep.BusyMessage, screen.Said);
            Assert.Equal("Being prepared in another window", screen.SaidLabel);
            Assert.NotNull(screen.View);
            Assert.True(screen.Continue.IsEffectivelyEnabled);

            _go.Set();                                                         // the other one finishes
            Until(() => other.IsCompleted, "the other preparation ends");
            Assert.Equal(DiscPrepOutcome.Prepared, other.Result.Outcome);
            // nothing is left to prepare: this window, which found no game at its start, opens on the other
            // window's files, and its buttons know the game is there
            Assert.Equal(NotPrepared, state.GameProblem);                      // as this window's start found it
            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens on the other window's files");
            Assert.Equal(image, state.Settings.DiscPath);
            Assert.Null(state.GameProblem);
            Assert.Equal((true, null, true, null), Buttons(screen));
        });
    }
}
