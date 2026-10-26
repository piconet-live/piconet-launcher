using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.Tests;

// A preparation stopped in the middle of its copy, by Cancel and by the window closed; one that cannot succeed
// whatever the player does; and a game that becomes startable while the launcher is open.
public sealed partial class FirstScreenTests
{
    /// <summary>Holds a preparation still at its first report with bytes copied: on the preparation's own thread
    /// (<see cref="AppState.PreparationWatch"/>), so the copy stands where it is until <see cref="Go"/>.</summary>
    private sealed class HeldInTheCopy : IProgress<DiscPrepProgress>, IDisposable
    {
        public ManualResetEventSlim Held { get; } = new(false);

        public ManualResetEventSlim Go { get; } = new(false);

        public void Report(DiscPrepProgress value)
        {
            if (value is { Stage: DiscPrepStage.Extracting, BytesDone: > 0 } && !Held.IsSet)
            {
                Held.Set();
                Go.Wait(TimeSpan.FromSeconds(60));
            }
        }

        public void Dispose()
        {
            Go.Set();
            Held.Dispose();
            Go.Dispose();
        }
    }

    private void AssertNothingLooksReady(AppState state)
    {
        Assert.Equal(PreparedState.NotPrepared, Prepared(state));
        Assert.Null(DiscPrep.ReadyFolder(Data, state.GamePaths.Bundle, Pins));
        Assert.False(File.Exists(DiscPrep.RecordFile(Data)));
        Assert.False(Directory.Exists(Game));
        Assert.False(Directory.Exists(Game + ".preparing"));
    }

    [Fact]
    public async Task CancelInTheMiddleOfTheCopyKeepsNothing()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: null);
        using var hold = new HeldInTheCopy();
        state.PreparationWatch = hold;
        await OnTheLauncher(state, screen =>
        {
            screen.Type(image);
            screen.Press(screen.Continue);
            Until(() => hold.Held.IsSet, "the preparation is in its copy");
            Dispatcher.UIThread.RunJobs();

            // files of the game are written, and the screen says how far it is
            Assert.NotEmpty(Directory.GetFiles(Game + ".preparing", "*", SearchOption.AllDirectories));
            Assert.Equal("COPYING", screen.Stage);
            Assert.True(screen.Bar > 0);
            Assert.StartsWith("0 MB / ", screen.Size, StringComparison.Ordinal);    // its first bytes: a small file of the made-up disc

            screen.Press(screen.Cancel);
            Assert.Equal("STOPPING", screen.Stage);
            hold.Go.Set();
            Until(() => !screen.Preparing, "the preparation stops");

            Assert.Equal("Stopped · nothing kept", screen.Said);
            Assert.NotNull(screen.View);
            AssertNothingLooksReady(state);
            Assert.Equal(NotPrepared, state.GameProblem);
        });
    }

    [Fact]
    public async Task TheWindowClosedInTheMiddleOfTheCopyLeavesNothingReadyAndNothingAtWork()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: null);
        using var hold = new HeldInTheCopy();
        state.PreparationWatch = hold;
        await OnTheLauncher(state, screen =>
        {
            screen.Type(image);
            screen.Press(screen.Continue);
            Until(() => hold.Held.IsSet, "the preparation is in its copy");
            Assert.NotEmpty(Directory.GetFiles(Game + ".preparing", "*", SearchOption.AllDirectories));

            // the copy goes on a moment after the window's close has begun, as a disk's write would
            var page = (DiscViewModel)screen.View!.DataContext!;
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                hold.Go.Set();
            }, TestContext.Current.CancellationToken);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            screen.CloseTheLauncher();                                         // returns when the preparation has let go
            Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));

            // nothing that looks ready, and nothing at work: the lock is free, so a preparation is not told "busy"
            AssertNothingLooksReady(state);
            Assert.False(File.Exists(Path.Combine(Data, DiscPrep.FolderName, DiscPrep.LockName)));
            var after = DiscPrep.Prepare(image, Data, state.GamePaths.Bundle, null, new DiscPrepOptions { Pins = Pins }, TestContext.Current.CancellationToken);
            Assert.Equal(DiscPrepOutcome.Prepared, after.Outcome);
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.Preparing);
        });
    }

    /// <summary>The online part's patch list, made one that cannot be used.</summary>
    private static void Break(string bundle)
    {
        var list = Path.Combine(bundle, DiscPrep.PatchListName);
        File.WriteAllText(list, File.ReadAllText(list).Replace("\"complete\": true", "\"complete\": false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APreparationThatCannotSucceedOpensTheLauncherWithThePlayButtonsOff()
    {
        const string Why = "The game program's patch list is incomplete (it was built without the game's files).";
        var image = _disc.Write(_dir);
        PrepareWithoutTheLauncher(image, Bundle("bundle"));
        Break(Bundle("bundle"));                                               // the launcher's own fault: no disc mends it
        Break(Path.Combine(_dir, "bundle"));
        var state = State(stored: image, bundle: Path.Combine(_dir, "bundle"), server: "ws://127.0.0.1:9/ws");
        Break(state.GamePaths.Bundle!);
        var stale = DiscPrep.Status(Data, state.GamePaths.Bundle, Pins);
        Assert.Equal((PreparedState.Stale, false, Why), (stale.State, stale.NeedsDisc, stale.Message));

        await OnTheLauncher(state, screen =>
        {
            Assert.NotNull(screen.View);                                       // the first screen, with what is wrong
            Assert.Equal(Why, screen.Said);
            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens although the preparation failed");

            Assert.True(screen.Main.Navigation);                               // Settings and Replays can be reached
            Assert.Equal(Why, state.Problem);                                  // said where the launcher's notices go
            Assert.Equal((true, Why, false, null), Buttons(screen));            // and no game is started on those files
            Assert.Equal("PREPARE AGAIN", screen.Main.Control.GoText);         // the big button leads back, with no restart
            Assert.Equal(image, state.Settings.DiscPath);
            Assert.NotNull(state.PlayOffline());

            // the launcher's files are mended (an update of the copy); the button pressed, the first screen again
            Bundle("bundle");
            var race = screen.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("go") && b.IsEffectivelyVisible);
            Assert.Equal("PREPARE AGAIN", Assert.IsType<TextBlock>(race.Content).Text);
            Click(screen.Window, race);
            Assert.NotNull(screen.View);
            Assert.False(screen.Main.Navigation);
            Assert.Equal(image, screen.PathBox.Text);
            Assert.Equal(Why, screen.Said);                                    // what the last try said
            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens on the game");
            Assert.Null(state.Problem);                                        // the failure's notice is taken back
            Assert.Null(state.GameProblem);
            Assert.Equal((true, null, true, null), Buttons(screen));
            Assert.Equal("RACE", screen.Main.Control.GoText);
        });
    }

    [Fact]
    public async Task AGameThatBecomesStartableWhileTheLauncherIsOpenTurnsThePlayButtonsOn()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: image, program: false, server: "ws://127.0.0.1:9/ws");
        await OnTheLauncher(state, screen =>
        {
            Assert.True(screen.Opened);                                        // no game program: open, the buttons off
            Assert.False(Buttons(screen).Game);

            // the game program arrives and the game is prepared, by another window of the launcher
            File.WriteAllText(state.GamePaths.Runner, "");
            File.WriteAllText(state.GamePaths.Module, "");
            PrepareWithoutTheLauncher(image, state.GamePaths.Bundle!);
            Assert.NotNull(state.GameProblem);                                 // not looked at yet

            // the buttons are shown again from time to time (the server's status, any change): they look again
            Until(() =>
            {
                state.Update(s => s);
                return state.GameProblem is null;
            }, "the buttons find the game");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((true, null, true, null), Buttons(screen));
        });
    }
}
