using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// Not a check but a measurement, run only on demand: STALL_WALK names a file and every page and common action is
/// walked through in the launcher's window, on the toolkit's own main loop, with the stall watch running
/// (<see cref="StallWatch"/>); the file gets one row per action, its worst stall, and what the stall was on.
/// STALL_WALK_DISC names a real disc image (else a made-up one). No screen: frames are ticked and no pixels reach a
/// card, so it shows the window thread's work, not what a real window's drawing adds.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class StallWalkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
    private readonly List<string> _rows = [];
    private StallWatch? _watch;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task EveryPageAndAction()
    {
        if (Environment.GetEnvironmentVariable("STALL_WALK") is not { Length: > 0 } output || !OperatingSystem.IsLinux())
        {
            return;
        }
        if (Environment.GetEnvironmentVariable("STALL_WALK_NAMES") is { Length: > 0 })
        {
            StallWatch.AskToolkit();   // (slows layouts: for finding what a stall is, not how long)
        }
        var cancel = TestContext.Current.CancellationToken;
        var disc = Environment.GetEnvironmentVariable("STALL_WALK_DISC") ?? new SyntheticDisc().Write(_dir, "gx.iso");
        var data = Path.Combine(_dir, "data");
        var replays = Path.Combine(data, "replays");
        Directory.CreateDirectory(replays);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(disc) + "}");
        var state = AppState.Load(data);
        var (runner, module, game) = Runner();
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: game) });
        await using var server = new LedServer();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var thread = Dispatcher.UIThread;
                using var stop = new CancellationTokenSource();
                _watch = new StallWatch(job => thread.Post(job, DispatcherPriority.Send), _ => { });
                // frames tick on a thread of their own, as a real window's timer does: the drawing (software here)
                // is not the window thread's work
                var frames = new Thread(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Thread.Sleep(16);
                    }
                }) { IsBackground = true };
                frames.Start();
                Exception? failed = null;
                thread.Post(async () =>
                {
                    try
                    {
                        await Walk(state, server, disc);
                    }
                    catch (Exception e)
                    {
                        failed = e;
                    }
                    await stop.CancelAsync();
                });
                thread.MainLoop(stop.Token);
                frames.Join();
                _watch.Dispose();
                Assert.Null(failed);
            }, cancel);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await ui.DisposeAsync();
            await File.WriteAllLinesAsync(output, _rows, CancellationToken.None);
        }
    }

    private async Task Walk(AppState state, LedServer server, string disc)
    {
        MainViewModel main = null!;
        MainWindow window = null!;
        await Step("the disc's look read at the start", () => { _ = state.LookForTheGame(); GameSkin.Load(disc); });
        await Step("pages made", () =>
        {
            main = new MainViewModel(state);
            if (Environment.GetEnvironmentVariable("STALL_WALK_NO_PREBUILD") is not { Length: > 0 })
            {
                Prebuild(main);
            }
        });
        await Step("window opened on Home", () =>
        {
            window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
            window.Show();
            main.Current = main.Home;
        }, 1200);
        state.WindowFocus(true);
        _ = state.Pads;   // what a real window does: the pad reader's helper runs while the window is in front
        state.PadsChanged();
        await Task.Delay(800);

        foreach (var round in new[] { "first", "second" })
        {
            await Step($"Controller page, {round} open", () => main.ShowSettings(SettingsTab.Controller), 900);
            await Step($"Controller: port 2", () => main.Controller.PortIndex = 1);
            await Step($"Controller: port 1", () => main.Controller.PortIndex = 0);
            await Step($"Controller: a port's kind changed", () => main.Controller.KindIndex = main.Controller.KindIndex == 1 ? 2 : 1, 900);
            await Step($"Controller: left, {round}", () => main.ShowTabCommand.Execute("play"), 900);
            await Step($"Garage, {round} open", () => main.ShowTabCommand.Execute("garage"), 1500);
            await Step($"Garage: another machine, {round}", () => main.Garage.MachineIndex = round == "first" ? 12 : 13, 900);
            await Step($"Practice, {round} open", () => main.Current = main.Practice, 1500);
            await Step($"Replays, {round} open", () => main.ShowTabCommand.Execute("replays"), 900);
            foreach (var tab in new[] { SettingsTab.Game, SettingsTab.Online, SettingsTab.Launcher })
            {
                await Step($"Settings {tab}, {round} open", () => main.ShowSettings(tab));
            }
            await Step($"Home, {round} back", () => main.Current = main.Home);
        }
        await Step("a setting changed", () => state.Update(s => s with { SoundVolume = 40 }));

        var all = Enumerable.Range(0, GameMachines.Count).ToArray();
        await Step("lobby: joined", () => _ = state.JoinAsync(server.Address), 500);
        await Until(() => server.Of<Hello>().Count == 1);
        await server.SendAsync(LedServer.Welcome());
        await Until(() => server.Of<EnterQuickPlay>().Count == 1);
        await StepAsync("lobby page, first open", async () =>
        {
            main.Current = main.Lobby;
            await server.SendAsync(Full(LobbyState.Forming));
        }, 1200);
        await StepAsync("course vote", () => server.SendAsync(Full(LobbyState.Starting), new CourseVote("m1", [1, 3, 5, 7, 8, CourseVote.RandomCourse], 60_000)), 1500);
        await StepAsync("machine choice", () => server.SendAsync(new ChooseMachine("m1", 21, all, 40_000), Full(LobbyState.Choosing)), 1500);
        await Step("machine choice: another machine", () => main.Lobby.Garage.MachineIndex = 17, 900);
        await StepAsync("back to the gathering", () => server.SendAsync(Full(LobbyState.Forming)), 900);
        // the race page, with the disc's data still being read (the disc loaded a moment before), as a slow disc would
        await Step("the disc read again", () => GameSkin.Load(disc), 0);
        var race = new LobbyRace("m1", 8, 3, [.. Enumerable.Range(0, 30).Select(i => new SeatSetup(i, null, $"PLAYER {i}", i, new Rgb(1, 2, (byte)i)) with { StockColor = i % 4 })],
            DateTimeOffset.UtcNow);
        LobbyState Racing() => Full(LobbyState.Racing) with { Race = race };
        await StepAsync("race page, data still being read", () => server.SendAsync(Racing()), 900);
        await StepAsync("race: first report", () => server.SendAsync(new LiveRace("m1", Report(600, RacerLive.Racing))), 500);
        await StepAsync("race: a report", () => server.SendAsync(new LiveRace("m1", Report(900, RacerLive.Racing))), 500);
        await StepAsync("race: results", () => server.SendAsync(new LiveRace("m1", Report(1200, RacerLive.Finished))), 900);
        await StepAsync("back to the gathering, after the race", () => server.SendAsync(Full(LobbyState.Forming)), 900);
        await StepAsync("leave the lobby", () => state.LeaveAsync(), 900);
        await Step("Home again", () => main.Current = main.Home);
        window.Close();
        main.Dispose();
    }

    private static void Prebuild(MainViewModel main) =>

        new ViewLocator().Prebuild(main.AllPages);


    /// <summary>Thirty racers' report.</summary>
    private static RaceSnapshot Report(int frame, string racerState) => new(frame, 0, [.. Enumerable.Range(0, 30).Select(i =>
        new RacerLive(i, i, 2, 20_000, 60_000 + i, 19_000, 900, 80, 100, 0, 1000, racerState, 0, 0, 0, 1000 - i))]);

    /// <summary>A full lobby of thirty, as a big one tells it.</summary>
    private static LobbyState Full(string stage) => LedServer.Lobby(stage) with
    {
        Players = [.. Enumerable.Range(0, 30).Select(i => new PlayerInfo(i == 0 ? "p1" : $"p{i + 1}", i == 0 ? "ANA" : $"PLAYER {i}", i, new Rgb(1, 2, (byte)i), 0, false, false))],
    };

    private Task Step(string name, Action action, int settleMs = 600) => StepAsync(name, () =>
    {
        action();
        return Task.CompletedTask;
    }, settleMs);

    /// <summary>The action on the window's thread as a job of its own, the thread left to run, and what it held up.</summary>
    private async Task StepAsync(string name, Func<Task> action, int settleMs)
    {
        await Task.Delay(100);   // what the step before still had going is not this one's
        _watch!.Take();
        StallWatch.Doing(name);
        await action();
        await Task.Delay(settleMs);
        var seen = _watch.Take();
        var worst = seen.OrderByDescending(s => s.Ms).FirstOrDefault();
        _rows.Add(FormattableString.Invariant($"{worst?.Ms ?? 0,7:F1}\t{seen.Count,3}\t{seen.Sum(s => s.Ms),7:F0}\t{name}\t{worst?.Doing}\t{(worst is null ? "" : string.Join("; ", worst.Toolkit))}"));
        StallWatch.Doing("");
    }

    private static async Task Until(Func<bool> met)
    {
        for (var i = 0; i < 500 && !met(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(met());
    }

    /// <summary>A game program that answers as the helper does: it takes a moment to go after its input closes, as the
    /// real one does letting go of the adapter.</summary>
    private (string Runner, string Module, string Game) Runner()
    {
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, """
            #!/bin/sh
            sleep 0.1
            echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}'
            echo '{"is":"ports","ports":[{"kind":6,"device":"","connected":false}]}'
            echo '{"is":"ready","version":1}'
            cat > /dev/null
            sleep 0.4

            """.Replace("            ", "", StringComparison.Ordinal));
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        return (runner, module, Path.Combine(_dir, "game"));
    }
}
