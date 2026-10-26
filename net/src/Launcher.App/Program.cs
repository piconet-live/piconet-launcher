using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Velopack;

namespace Launcher.App;

[SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",

    Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
internal static class Program
{
    /// <summary>
    /// The launcher's window; or one of the modes without it, as the command line says (<see cref="CommandLine"/>
    /// describes every option): <c>--play-offline</c>, the recompiled game started for offline play (the same start
    /// as the left column's Play F-Zero GX), waiting until the game is closed; <c>--play-match FILE</c>, the game
    /// started on the race of that match file (tests and development), recorded into the replay folder or where
    /// <c>--replay FILE</c> says; <c>--play-replay FILE</c>, the game playing that replay file back, which stays paused
    /// on the race's last picture until the game is closed (the replay scrubber); <c>--list-replays</c>, the replay folder's replays as the Replays page lists them, one line
    /// each. The exit status of a game start is the game's.
    /// With a launch file (<c>--config FILE</c>) that asks for it: Quick Play without the window
    /// (<see cref="QuickPlay"/>). A command line or a launch file that is refused: its sentence on standard error,
    /// status <see cref="CommandLine.RefusedStatus"/>, and nothing is started.
    /// In every mode an error nothing caught is written to <c>launcher.log</c> in the data folder
    /// (<see cref="InstallLastResort"/>).
    /// <para>Or, with <c>--prepare-disc IMAGE</c>, the game's files prepared from that disc image into the data
    /// folder without the window (<see cref="Launcher.Core.DiscPrep"/>; the first-run screen does the same): progress
    /// lines, the result and the game folder's digest on standard output. <c>--bundle FOLDER</c> another mod bundle
    /// than the launcher's (tests and development). Exit status: 0 the files are ready, 2 the image is refused (not
    /// a disc, another game or version, a modified or damaged file), 3 not enough free space, 4 stopped (Ctrl+C),
    /// 5 the launcher's own fault or the data folder's, 6 another launcher is preparing in the same data folder
    /// (<see cref="Launcher.Core.DiscPrepResult.ExitStatus"/>).</para>
    /// <para>Or, with <c>--import-stat-pack CARDATA --pack-name TEXT</c>, a stat pack file made of every value of that
    /// changed car data file (<c>cardata.lz</c>, or unpacked) that differs from the disc's own
    /// (<see cref="Launcher.Core.StatPackImport"/>; the protocol's handbook/stat-packs.md): what it wrote, or why it
    /// wrote nothing, on standard output. <c>--disc IMAGE</c> another disc image than the settings' (it must be the
    /// unchanged disc, every file: <c>--any-disc</c> takes another); <c>--out FILE</c> where the pack is written
    /// (<c>--overwrite</c>: over a file that is there). Exit status: 0 written, 2 refused, 5 the pack could not be
    /// written.</para>
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack first, before the command line is read (Updates.cs): Setup, an update and the uninstaller start the
        // launcher with arguments of their own, which it handles here and then ends the process. Its own apply of a
        // downloaded update at the start is off: the window's start does it (Updates.PutInAtStart), and not while a
        // game runs (another launcher window's), as the update replaces the folder a game runs from. In a launcher
        // that was not installed it does nothing. Its taskbar id is the launcher's own (Taskbar.Claim), so the
        // shortcut Setup makes and the window share one button.
        VelopackApp.Build().SetAppUserModelId(Branding.AppId).SetAutoApplyOnStartup(false).Run();
        var (line, refusal) = CommandLine.Parse(args);
        if (line is null)
        {
            Console.Error.WriteLine(refusal);
            return CommandLine.RefusedStatus;
        }
        Line = line;
        Faults = InstallLastResort(AppState.DataFolderOf(line.DataFolder));
        Gpu.GpuDebug.Use(line.Debug.GpuLog, line.Debug.GpuTurn);
        if (line.Debug.LoadTrace)
        {
            UseLoadTrace(Path.Combine(AppState.DataFolderOf(line.DataFolder), "load-trace.log"));
        }
        switch (line.Mode)
        {
            case StartMode.Fault:
                return FaultWithoutWindow(line.Debug.Fault!, Faults);
            case StartMode.PrepareDisc:
                return PrepareDisc(line.File!, line.Bundle);
            case StartMode.ListReplays:
                return ListReplays(AppState.Load(line.DataFolder));
            case StartMode.ImportStatPack:
                var disc = line.Disc ?? AppState.Load(line.DataFolder).Settings.DiscPath;
                return line.Report
                    ? Launcher.Core.StatPackImport.Report(disc, line.File!, Console.Out, line.AnyDisc)
                    : Launcher.Core.StatPackImport.Run(disc, line.File!, line.Name, line.Out, Console.Out, line.AnyDisc, line.Overwrite,
                        author: line.Author, modulePath: line.Module);
            case StartMode.QuickPlay:
                return QuickPlay(line.Debug.QuickPlay!);
            case StartMode.Window:
                if (ReferenceEquals(line.Debug, Launcher.Core.DebugSettings.Off) && !line.DevService)
                {
                    Updates.PutInAtStart(args, text => Faults?.Note(text));   // a downloaded update, unless a game runs (never a test's)
                }
                Taskbar.Claim();   // before the window is made
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        var state = AppState.Load(line.DataFolder, line.Debug);
        // the game's status board, a line of JSON for each thing it says (BoardLines), on the standard output
        var writing = new object();
        void Say(string text)
        {
            lock (writing)
            {
                Console.Out.WriteLine(text);
                Console.Out.Flush();
            }
        }
        state.BoardMade += board => Launcher.Core.BoardLines.Follow(board, Say);
        state.Noted += text => Say(System.Text.Json.JsonSerializer.Serialize(new { @event = "noted", values = new { text } }));
        var problem = line.Mode switch
        {
            StartMode.PlayReplay => state.Play(line.File!),
            StartMode.PlayMatch => state.PlayMatch(line.File!, line.ReplayFile),
            StartMode.PlayControllerSetup => state.PlayOffline(controllerSetup: true),
            _ => state.PlayOffline(),
        };
        if (problem is not null)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }
        state.Fork!.WaitForExit();
        if (state.Board is { } last)
        {
            last.LookNow();   // (what came after the board's last look)
            Say(Launcher.Core.BoardLines.Last(last));
        }
        return state.Fork.ExitCode;
    }

    /// <summary>
    /// Quick Play without the window and without a person (tests only): joins the coordinator as Home's RACE button
    /// does (<see cref="AppState.JoinAsync"/>), then a <see cref="Launcher.Core.QuickPlayScript"/> makes the calls the
    /// lobby page's buttons make on the same session; the session starts the recompiled game at the match start as
    /// for a player (<c>AppState.StartRace</c>), records the replay, passes the live race on, and brings the player
    /// back to the lobby when the game closes. Nothing here is a second path into a race.
    /// <para>What the visit does is the launch file's (<see cref="Launcher.Core.QuickPlayVisit"/>: the server, the
    /// races, the votes, where its lines go, its time limit), and so is what its games get
    /// (<see cref="Launcher.Core.DebugSettings"/>: the title, more runner arguments, the game's AI on the player's
    /// seat, a test lag, another limit for a game that is not racing).</para>
    /// Exit status: 0 the races were run; 2 the server could not be reached or refused the player; 3 the time limit.
    /// A game still running when the visit ends is ended with it.
    /// </summary>
    private static int QuickPlay(Launcher.Core.QuickPlayVisit visit)
    {
        var state = AppState.Load(Line.DataFolder, Line.Debug);
        // the launch file's own server and no other: a visit never goes to the settings' server (LaunchConfig refuses
        // a visit that names none), which in a new data folder is the official service's
        var url = visit.Server;
        state.Noted += line => Faults?.Note(line);
        using var file = visit.Log is { } path ? new StreamWriter(path, append: false) { AutoFlush = true } : null;
        var output = file ?? Console.Out;
        var writing = new object();
        void Write(string line)
        {
            lock (writing)
            {
                output.WriteLine(line);
            }
        }
        state.BoardMade += board => Launcher.Core.BoardLines.Follow(board, Write);
        var script = new Launcher.Core.QuickPlayScript(new Launcher.Core.QuickPlayScriptOptions(
            visit.Races, visit.StartWith, visit.VoteCourse, Watch: visit.Watch), Write);
        Launcher.Core.QuickPlaySession session;
        try
        {
            session = state.JoinAsync(new Uri(url)).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or UriFormatException or IOException)
        {
            // what Home shows (HomeViewModel.RaceAsync)
            var notice = $"Could not reach the server: {e.Message}";
            Write(System.Text.Json.JsonSerializer.Serialize(new { @event = "failed", values = new { text = notice } }));
            Console.Error.WriteLine(notice);
            return 2;
        }
        void Step()
        {
            var game = state.Fork;
            int? pid = null;
            try
            {
                pid = game is { HasExited: false } ? game.Id : null;
            }
            catch (InvalidOperationException)
            {
                // it exited between the two reads
            }
            script.Step(session, pid is not null, state.Problem, pid);
        }
        session.Changed += Step;
        state.Changed += Step;
        state.LiveReceived += script.Live;
        Step();
        var finished = script.Done.Wait(TimeSpan.FromSeconds(visit.TimeLimitSeconds));
        Step();
        session.Changed -= Step;
        state.Changed -= Step;
        state.LiveReceived -= script.Live;
        if (!finished)
        {
            Write(System.Text.Json.JsonSerializer.Serialize(new { @event = "failed", values = new { text = "the visit's time limit is over" } }));
        }
        if (finished && script.Failure is null)
        {
            try
            {
                session.LeaveAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // the connection went first
            }
        }
        state.Shutdown();
        // a test visit leaves nothing running: a game still up when the visit ends (the time limit, a refusal after a
        // race began) is asked to stop through its status board, then ended by the launcher's own handle of it (that
        // process alone)
        state.StopGame(AppState.StopGrace);
        if (script.Failure is { } failure)
        {
            Console.Error.WriteLine(failure);
            return 2;
        }
        return finished ? 0 : 3;
    }

    /// <summary>What the Replays page lists, as text (tests and development): one line for each replay of the replay
    /// folder, newest first, with what a row shows (the course, the laps, the racer it is seen from, their place and
    /// time, the players) and what recorded it.</summary>
    private static int ListReplays(AppState state)
    {
        foreach (var r in Launcher.Core.ReplayFile.List(state.Settings.ReplayFolder))
        {
            var own = r.RecorderResult;
            var (engine, module) = Launcher.Core.ReplayFile.RecordedBy(r.Path);
            Console.Out.WriteLine(string.Join(" | ",
                r.FileName,
                r.Recorded.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                r.CourseName,
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{r.Laps} lap(s)"),
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"length {r.Length:m\\:ss}"),
                r.Recorder is { } who ? $"seen from {who.Name} ({(who.Raced.Name(null) is { Length: > 0 } m ? m : "?")})" : "seen from nobody",
                own is null ? "no results" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"place {own.Place} of {r.Results!.Count}, {(own.Time is { } t ? t.ToString("m\\:ss\\.fff", System.Globalization.CultureInfo.InvariantCulture) : own.Out ? "out" : "disqualified")}"),
                "players " + Launcher.Core.Replays.Racers(r.Players),
                r.StatPackLabel.Length > 0 ? "stat pack: " + r.StatPackLabel : "original stats",
                $"recorded by {engine ?? "?"} {(module is null ? "?" : module[..8])}",
                Launcher.Core.ReplayFile.Problem(r.Path) ?? "whole"));
        }
        return 0;
    }

    /// <summary>The game's files prepared from a disc image (<c>--prepare-disc</c>). The settings are read, never
    /// written; the bundle is the one a game start would use.</summary>
    private static int PrepareDisc(string image, string? bundle)
    {
        var data = AppState.DataFolderOf(Line.DataFolder);
        var settings = new Launcher.Core.SettingsStore(Path.Combine(data, "settings.json")).Load();
        bundle ??= Launcher.Core.RecompLaunch.Resolve(settings.Recomp, Launcher.Core.Install.This, data).Bundle;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        var result = Launcher.Core.DiscPrep.Prepare(image, data, bundle, new ProgressLines(), cancel: stop.Token);
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{result.Outcome}: {result.Message} ({result.Elapsed.TotalSeconds:0.0} s{(result.BytesWritten > 0 ? $", {result.BytesWritten:N0} bytes written, {result.Hashing.TotalSeconds:0.0} s of it hashing" : "")})"));
        if (result.GameFolder is { } folder)
        {
            Console.WriteLine("game folder: " + folder);
        }
        if (result.FolderSha256 is { } digest)
        {
            Console.WriteLine("game folder sha256: " + digest);
        }
        return result.ExitStatus;
    }

    /// <summary>A preparation's progress as lines: one a second at most, and one at each new stage.</summary>
    private sealed class ProgressLines : IProgress<Launcher.Core.DiscPrepProgress>
    {
        private Launcher.Core.DiscPrepStage? _stage;
        private long _last;

        public void Report(Launcher.Core.DiscPrepProgress value)
        {
            var now = Environment.TickCount64;
            if (value.Stage == _stage && now - _last < 1000)
            {
                return;
            }
            (_stage, _last) = (value.Stage, now);
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{value.Stage}: {value.Fraction * 100:0}% {value.BytesDone >> 20}/{value.BytesTotal >> 20} MB, file {value.FilesDone}/{value.FilesTotal} {value.File}"));
        }
    }

    /// <summary>This start's command line (before <see cref="Main"/>, and in a test that makes the application
    /// itself: the window, the player's data folder, no test setting).</summary>
    public static CommandLine Line { get; private set; } = new();

    /// <summary>The launcher's log file's name, in the data folder.</summary>
    public const string LogFileName = "launcher.log";

    /// <summary>The handler of last resort of this run (null before <see cref="Main"/>).</summary>
    /// <summary>The load trace's lines into <paramref name="file"/> (<see cref="Launcher.Core.LoadTrace"/>), one run after another.</summary>
    private static void UseLoadTrace(string file)
    {
        var gate = new Lock();
        Launcher.Core.LoadTrace.Use(line =>
        {
            lock (gate)
            {
                try
                {
                    Launcher.Core.LogFiles.Append(file, line + Environment.NewLine);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // a check's log is not worth a load
                }
            }
        });
        Launcher.Core.LoadTrace.Note($"==== a start, {BuildInfo.Text}");
    }

    public static Launcher.Core.LastResort? Faults { get; private set; }

    /// <summary>
    /// Puts the handler of last resort (<see cref="Launcher.Core.LastResort"/>) on the routes an error nothing
    /// caught takes outside the window's thread (that one is <see cref="App.Watch"/>'s, once there is a window):
    /// <para>a background task whose error nobody read (found when the task is collected): written, the launcher
    /// goes on;</para>
    /// <para>a thread other than the first (a timer, the game's exit, a network callback): written, that thread
    /// ends and the launcher goes on, where .NET would end the process;</para>
    /// <para>anything else, which ends the process (the first thread: the window's outside a job of its own, and
    /// the modes without a window): written before it does.</para>
    /// The player is told on standard error until the window takes that over.
    /// </summary>
    private static Launcher.Core.LastResort InstallLastResort(string dataFolder)
    {
        var faults = new Launcher.Core.LastResort(Path.Combine(dataFolder, LogFileName), BuildInfo.Text);
        // on the console the notice is followed by where the log is (the window offers to open its folder instead)
        faults.Tell = notice => Console.Error.WriteLine($"{notice} ({faults.LogFile})");
        var first = Environment.CurrentManagedThreadId;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            faults.Caught("in a background task (the launcher goes on)", e.Exception);
            e.SetObserved();
        };
        System.Runtime.ExceptionServices.ExceptionHandling.SetUnhandledExceptionHandler(e =>
            Environment.CurrentManagedThreadId != first && faults.Caught("on a background thread (that thread ended, the launcher goes on)", e));
        // (nobody is told: the launcher is closing, and .NET prints the error on standard error itself)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            faults.Caught("by anything: the launcher closes", e.ExceptionObject as Exception, tell: false);
        return faults;
    }

    /// <summary>Tests only (a launch file's <c>"fault"</c>): raises an error nothing catches on one of the handler
    /// of last resort's routes: <c>window</c> (a job of the window's thread), <c>task</c> (a background task nobody
    /// waits for), <c>thread</c> (a thread-pool thread), <c>process</c> (the process's own event, raised without
    /// ending it) or <c>first</c> (the first thread: the launcher closes). With the window it does so two seconds
    /// after the window opened, for a look at what the player is told; with <c>"faultWithoutWindow"</c> at once,
    /// and the exit status is 0 when the launcher was still running after it.</summary>
    internal static void TestFault(string route)
    {
        switch (route)
        {
            case "window":
                Avalonia.Threading.Dispatcher.UIThread.Post(() => throw new InvalidOperationException("test fault: window"));
                break;
            case "task":
                TestFaultInATask();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                break;
            case "thread":
                using (var thrown = new ManualResetEventSlim())
                {
                    ThreadPool.UnsafeQueueUserWorkItem(_ =>
                    {
                        thrown.Set();
                        throw new InvalidOperationException("test fault: thread");
                    }, null);
                    thrown.Wait();
                }
                break;
            case "process":
                System.Runtime.ExceptionServices.ExceptionHandling.RaiseAppDomainUnhandledExceptionEvent(
                    new InvalidOperationException("test fault: process"));
                break;
            default:
                throw new InvalidOperationException("test fault: " + route);
        }
    }

    // in a method of its own, so that nothing holds the task when the collector looks
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void TestFaultInATask()
    {
        var task = Task.Run(new Action(() => throw new InvalidOperationException("test fault: task")));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
    }

    private static int FaultWithoutWindow(string route, Launcher.Core.LastResort faults)
    {
        if (route == "window")
        {
            App.Watch(Avalonia.Threading.Dispatcher.UIThread, faults);
        }
        TestFault(route);
        if (route == "window")
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        // the task's and the thread's errors are dealt with on threads of their own, and this one ends the launcher:
        // it goes on when the handler is done with the error (written, the player told), five seconds at most. (It
        // waited for the log to be there, which is the first of the two: under load the launcher ended between them,
        // and the notice was never written.)
        // The task's error reaches the handler when the collector finds the task held by nobody, and the thread that
        // ran it can hold it a moment longer than the task takes to end: the one look after it (TestFault) then
        // found it held, nothing looked again, and no log was written in the five seconds (1 run in 40 beside busy
        // loops, and once in a check). So the collector looks again until the handler has the error.
        SpinWait.SpinUntil(() =>
        {
            if (route == "task" && faults.Dealt == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(20);
            }
            return faults.Dealt > 0;
        }, TimeSpan.FromSeconds(5));
        Console.Out.WriteLine("still running");
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
