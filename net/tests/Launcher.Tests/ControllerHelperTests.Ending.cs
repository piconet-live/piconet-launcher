using System.Diagnostics;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

// How a helper is ended, and that none runs while a game starts or runs (found in review).
public sealed partial class ControllerHelperTests
{
    /// <summary>A program that takes its input and goes when it closes, as the helper does, and leaves its process
    /// ID in the user folder it was given; made a runner so that <see cref="ControllerHelper.Start"/> starts it.</summary>
    private RecompPaths ScriptedRunner(string name)
    {
        var folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.Combine(folder, "user"));
        var runner = Path.Combine(folder, "runner.sh");
        File.WriteAllText(runner, "#!/bin/sh\necho $$ >> \"$2/pids\"\ncat > /dev/null\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return new RecompPaths(runner, Path.Combine(folder, "m.dll"), Path.Combine(folder, "game"), Path.Combine(folder, "user"));
    }

    private static bool Alive(int id)
    {
        try
        {
            using var p = Process.GetProcessById(id);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>A helper that no longer takes its input (its pipe is full): the page's command does not wait in the
    /// write, and a game start, which ends every helper first, comes back in a bounded time.</summary>
    [Fact]
    public async Task AHelperThatTakesNothingInHoldsNeitherThePageNorAGameStart()
    {
        using var stuck = new ManualResetEventSlim();
        var helper = new ControllerHelper(new StuckWriter(stuck), new StringReader(""));
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            helper.Watch(0);   // the window's thread: never in the write, however many lines wait
        }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"1000 commands took {watch.Elapsed.TotalMilliseconds:0} ms");
        var stopping = Task.Run(ControllerHelper.StopAll, TestContext.Current.CancellationToken);   // a game start
        var done = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
        var inTime = done == stopping;
        stuck.Set();
        await stopping;
        Assert.True(inTime, "StopAll had not come back 6 s after a helper stopped taking its input");
        await helper.SentAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);   // nothing waits for ever
    }

    private sealed class StuckWriter(ManualResetEventSlim free) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => free.Wait();
    }

    /// <summary>While a game of this launcher starts or runs no helper is started: the page's, which would come
    /// back a moment after the start check ended it, and the game never contend for the adapter.</summary>
    [Fact]
    public void NoHelperIsStartedWhileAGameStartsOrRuns()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var paths = ScriptedRunner("hold");
        using var before = ControllerHelper.Start(paths);
        Assert.NotNull(before);
        Assert.False(ControllerHelper.Held);

        // a game's start: every helper is ended, and none is started from here on
        var hold = ControllerHelper.HoldForGame();
        Assert.True(ControllerHelper.Held);
        Assert.Null(ControllerHelper.Start(paths));
        Assert.All(File.ReadAllLines(Path.Combine(paths.UserFolder, "pids")), pid => Assert.False(Alive(int.Parse(pid, System.Globalization.CultureInfo.InvariantCulture))));

        // the game runs: still none; it has gone: the page may start its helper again
        var info = new ProcessStartInfo("/bin/sh") { RedirectStandardInput = true, UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("cat > /dev/null");
        using var game = Process.Start(info)!;
        hold.Until(game);
        Assert.Null(ControllerHelper.Start(paths));
        game.StandardInput.Close();
        Assert.True(game.WaitForExit(TimeSpan.FromSeconds(10)));
        Assert.False(ControllerHelper.Held);
        using var after = ControllerHelper.Start(paths);
        Assert.NotNull(after);

        // a start that came to nothing gives the hold back at once
        ControllerHelper.HoldForGame().Dispose();
        Assert.False(ControllerHelper.Held);
    }

    /// <summary>Helpers being started on other threads while a game start ends them all: when the game start comes
    /// back, none of them runs, whichever side of the start each one was on.</summary>
    [Fact]
    public async Task AHelperWhoseStartIsInFlightDoesNotOutliveAGameStart()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var paths = ScriptedRunner("flight");
        var starters = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            var started = 0;
            for (var i = 0; i < 6; i++)
            {
                started += ControllerHelper.Start(paths) is null ? 0 : 1;   // (not put away here: the game start ends them)
            }
            return started;
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.Delay(40, TestContext.Current.CancellationToken);
        using (ControllerHelper.HoldForGame())
        {
            // every program that had begun wrote its ID as its first act; one ended before that never ran on
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var pids = File.Exists(Path.Combine(paths.UserFolder, "pids")) ? File.ReadAllLines(Path.Combine(paths.UserFolder, "pids")) : [];
            Assert.All(pids, pid => Assert.False(Alive(int.Parse(pid, System.Globalization.CultureInfo.InvariantCulture)), $"helper {pid} outlived the game start"));
            var started = (await Task.WhenAll(starters)).Sum();
            Assert.InRange(started, 0, 36);
            Assert.Null(ControllerHelper.Start(paths));
        }
        ControllerHelper.StopAll();
    }
}
