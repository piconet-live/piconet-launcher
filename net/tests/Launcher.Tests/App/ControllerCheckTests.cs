using Launcher.App;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The check before a start, from the launcher's shared state (Launcher.App's own code): while it asks its
/// helper, the Controller page runs none of its own, and the page is told when that begins and when it is over. And
/// the check ends with its launcher: a launcher that closes leaves none behind that still writes.</summary>
[Collection(ControllerHelpersAlone.Name)]
public sealed class ControllerCheckTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ThePagesHelperGoesForTheCheckAndThePageHearsWhenItMayRunAgain()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        // a game that is "installed", whose runner is a script that answers as the helper does, after a moment
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, """
            #!/bin/sh
            sleep 0.4
            echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}'
            echo '{"is":"ports","ports":[{"kind":6,"device":"","connected":false}]}'
            echo '{"is":"ready","version":1}'
            cat > /dev/null

            """.Replace("            ", "", StringComparison.Ordinal));
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        var state = AppState.Load(Path.Combine(_dir, "data"));
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
        Assert.Null(RecompLaunch.Problem(state.GamePaths));

        // the page's helper, as it runs while the page shows
        using var pages = ControllerHelper.Start(state.GamePaths);
        Assert.NotNull(pages);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pages.Ended += () => ended.TrySetResult();
        pages.Listen();
        Assert.False(state.CheckingController);

        var heard = new List<bool>();   // what the page would read at each word of a change
        state.Changed += () =>
        {
            lock (heard)
            {
                heard.Add(state.CheckingController);
            }
        };
        var check = state.CheckControllerAsync();
        Assert.True(state.CheckingController);                                         // from the first moment
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);   // the page's helper went first
        Assert.Same(check, state.CheckControllerAsync());                              // one check at a time
        Assert.Null(await check.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        Assert.False(state.CheckingController);
        // a first start: port 1 is the gamepad the helper named
        Assert.Equal("SDL/0/Pad", ControllerFiles.Read(state.GamePaths.UserFolder).Ports[0].Device);
        // the page heard the check begin, and, a moment after its end, that it is over
        for (var i = 0; i < 100 && Snapshot().Count < 2; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Equal([true, false], Snapshot());

        List<bool> Snapshot()
        {
            lock (heard)
            {
                return [.. heard];
            }
        }
    }

    /// <summary>RACE and the Local network tab's Join start the check and join beside it. When the join failed at
    /// once, or the window was closed, the check went on by itself: its helper answered a moment later and the game's
    /// controller settings were written for a launcher that was gone (in the tests, into a folder being removed:
    /// "Directory not empty" at two landings).</summary>
    [Fact]
    public async Task AClosingLauncherEndsTheCheckAndNothingIsWrittenAfterIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        // a helper that answers after three seconds (a first start would write port 1 then), and goes, as the
        // game's own does, when its input is closed
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, """
            #!/bin/sh
            (
            sleep 3
            echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}'
            echo '{"is":"ports","ports":[{"kind":6,"device":"","connected":false}]}'
            echo '{"is":"ready","version":1}'
            ) &
            cat > /dev/null

            """.Replace("            ", "", StringComparison.Ordinal));
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        var state = AppState.Load(Path.Combine(_dir, "data"));
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
        var heard = 0;
        state.Changed += () => Interlocked.Increment(ref heard);

        var check = state.CheckControllerAsync();
        Assert.True(state.CheckingController);
        var began = System.Diagnostics.Stopwatch.StartNew();
        state.Shutdown();

        // over when the shutdown returns, before the helper would have answered (3 s) or been given up (6 s)
        Assert.True(check.IsCompleted, "the check outlived the shutdown");
        Assert.Null(await check);
        Assert.InRange(began.Elapsed.TotalSeconds, 0, 2);
        string[] Files() => [.. Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        var (files, changes) = (Files(), Volatile.Read(ref heard));
        await Task.Delay(3500, TestContext.Current.CancellationToken);
        Assert.Equal(files, Files());                                                  // nothing was written since
        Assert.False(ControllerFiles.HasSettings(state.GamePaths.UserFolder));         // and no port 1 chosen unseen
        Assert.Equal(changes, Volatile.Read(ref heard));                               // nor a page told of anything
        // and a closed launcher starts no other check
        Assert.Null(await state.CheckControllerAsync());
        Assert.False(state.CheckingController);
    }
}
