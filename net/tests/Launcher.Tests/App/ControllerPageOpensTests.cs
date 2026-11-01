using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The Controller page opens at once whatever the devices are doing, and what lands after the page was left
/// goes nowhere: a helper that was on its way is ended, and none stays. And a paced list ends with every row.</summary>
[Collection(ControllerPageTests.Name)]
public sealed class ControllerPageOpensTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    /// <summary>The folder goes, a few tries if need be: when a page leaves, the pad reader may start its helper again,
    /// and that start can still be making the game's user folder as the test ends (nothing waits for it, as nothing in
    /// the launcher needs to).</summary>
    public void Dispose()
    {
        for (var tries = 50; ; tries--)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) when (tries > 0)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>A launcher with a game program that takes its time to go, as the real one letting go of the adapter.</summary>
    private AppState State(string name)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(folder, "game", "sys"));
        var runner = Path.Combine(folder, "runner.sh");
        File.WriteAllText(runner, """
            #!/bin/sh
            echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}'
            echo '{"is":"ports","ports":[{"kind":6,"device":"","connected":false}]}'
            echo '{"is":"ready","version":1}'
            cat > /dev/null
            sleep 1

            """.Replace("            ", "", StringComparison.Ordinal));
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(Path.Combine(folder, "m.dll"), "");
        File.WriteAllText(Path.Combine(folder, "game", "sys", "main.dol"), "");
        var state = AppState.Load(Path.Combine(folder, "data"));
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: Path.Combine(folder, "m.dll"), GameFolder: Path.Combine(folder, "game")) });
        return state;
    }

    private static async Task Gone(CancellationToken cancel)
    {
        for (var i = 0; i < 300 && ControllerHelper.AnyRunning; i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.False(ControllerHelper.AnyRunning);
    }

    [Fact]
    public async Task LeftBeforeTheHelperLandedItIsEndedAndNothingStays()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;   // the game here is a shell script
        }
        var cancel = TestContext.Current.CancellationToken;
        var state = State("left");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(async () =>
            {
                var page = new ControllerViewModel(state);
                page.Shown();
                page.Hidden();
                page.Shown();
                await Task.Delay(1, cancel);
                page.Hidden();
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(25, cancel);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.False(state.PageHelper);
                await Gone(cancel);
                await state.ControllerWrites;
            }, cancel);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    /// <summary>The reader's helper still on its way out (a second to go): the page's show comes back at once, its
    /// helper starts after the reader's has gone.</summary>
    [Fact]
    public async Task TheOldHelpersGoingDoesNotHoldThePage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var cancel = TestContext.Current.CancellationToken;
        var state = State("old");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(async () =>
            {
                state.WindowFocus(true);
                _ = state.Pads;
                state.PadsChanged();
                for (var i = 0; i < 300 && !ControllerHelper.AnyRunning; i++)
                {
                    await Task.Delay(10, cancel);
                }
                var page = new ControllerViewModel(state);
                var started = System.Diagnostics.Stopwatch.StartNew();
                page.Shown();
                Assert.InRange(started.ElapsedMilliseconds, 0, 100);
                for (var i = 0; i < 600 && page.Devices.Count == 0; i++)
                {
                    await Task.Delay(10, cancel);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.NotEmpty(page.Devices);   // the helper came, after the old one went
                page.Hidden();
            }, cancel);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
        // (after the shutdown: the page left, the window still in front, the pad reader may rightly run its own again)
        await Gone(cancel);
    }

    [Fact]
    public async Task APacedListEndsWithEveryRowAndFollowsItsSource()
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var list = new ItemsControl();
                var source = new System.Collections.ObjectModel.ObservableCollection<string>(Enumerable.Range(0, 41).Select(i => $"row {i}"));
                Paced.SetItems(list, source);
                var window = new Window { Content = list };
                window.Show();
                Assert.InRange(list.Items.Count, 1, 8);   // a first few on the first pass
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(source, list.Items.Cast<string>());
                source[3] = "changed";
                source.RemoveAt(40);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(source, list.Items.Cast<string>());
                window.Close();
                Assert.Empty(list.Items);                  // no rows held off screen
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }
}
