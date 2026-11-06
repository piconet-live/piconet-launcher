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
/// The graphics cache (<see cref="PipelineCache"/>, <see cref="AppState.Pipelines"/>) in the launcher's own window, with
/// a script that stands for the runner's <c>--build-pipelines</c>: it says a share built, then holds while the test's
/// hold file is there, then says done. The window is on Quick Play's page, as after the first screen (the first start's
/// own build: FirstScreenTests.Graphics.cs). Ray tracing turned on in Settings asks first (Cancel: off, nothing built;
/// Build: on, and its look built while every start waits, the column saying so); once built, turning it on asks nothing.
/// Another Aurora (its library changed) clears Dawn's cache and builds again, saying why; no Aurora beside the runner builds
/// nothing. With <c>PIPELINE_SHOTS</c> set
/// to a folder, the window is kept there as PNGs at each of those moments.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class GraphicsCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private string Hold => Path.Combine(_dir, "hold");

    private string Builds => Path.Combine(_dir, "builds.txt");

    private string Runner => Path.Combine(_dir, "runner.sh");

    /// <summary>The launcher's state with a game that is "installed", Aurora chosen and offered (a card that can ray
    /// trace), the runner a script: the controller helper's start says nothing; a build writes down its last argument,
    /// says 42% built, holds while <see cref="Hold"/> is there, and says done.</summary>
    /// <param name="runner">A build of the test's own in place of the script's.</param>
    private AppState State(Func<RecompPaths, string, IProgress<double>, CancellationToken, Task<PipelineBuildEnd>>? runner = null)
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{"noServer": true}""");
        File.WriteAllText(Runner, "#!/bin/sh\nif [ \"$1\" = --controllers ]; then cat > /dev/null; exit 0; fi\n"
            + "for a in \"$@\"; do last=\"$a\"; done\n"
            + $"if [ \"$last\" = {PipelineCache.BuildArgument} ]; then\n"
            + $"  echo \"$last\" >> '{Builds}'\n"
            + "  echo 'fzgx: pipelines: the build starts'\n"
            + "  echo '{\"progress\": 0.42}'\n"
            + $"  while [ -e '{Hold}' ]; do sleep 0.05; done\n"
            + "  echo '{\"done\": true, \"shaders\": 13408, \"seconds\": 1}'\n"
            + "  exit 0\nfi\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(Runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(Path.Combine(_dir, "m.dll"), "");
        File.WriteAllText(Path.Combine(_dir, "aurora-present.dll"), "aurora 1");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        var state = AppState.Load(Data);
        state.CardRayTraces = () => true;
        state.PipelineDrivers = () => "Made-up Graphics 1.0";
        state.PipelineRunner = runner ?? state.PipelineRunner;
        state.Update(s => s with
        {
            Recomp = new RecompSettings(Runner: Runner, Module: Path.Combine(_dir, "m.dll"), GameFolder: Path.Combine(_dir, "game")),
        });
        Assert.Null(RecompLaunch.Problem(state.GamePaths));
        return state;
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("PIPELINE_SHOTS") is { Length: > 0 } folder)
        {
            Aim();
            for (var i = 0; i < 30; i++)
            {
                Thread.Sleep(20);   // (the bar's growth is a transition of half a second)
                Frame();
            }
            Directory.CreateDirectory(folder);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    private int BuildsRun => File.Exists(Builds) ? File.ReadAllLines(Builds).Length : 0;

    /// <summary>The left column's block while the cache is built: its words and why; null: it does not show.</summary>
    private static (string? Line, string? Below, double Bar)? Column(Window window)
    {
        var block = Named<Border>(window, "Graphics");
        if (!block.IsEffectivelyVisible)
        {
            return null;
        }
        var texts = block.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        var bar = block.GetVisualDescendants().OfType<ProgressBar>().Single();
        return (texts.ElementAtOrDefault(0), texts.ElementAtOrDefault(1), bar.Value);
    }

    private async Task OnTheLauncher(AppState state, Action<MainWindow, MainViewModel> steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                main.Current = main.Home;                                         // (past the first screen: no disc here)
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    steps(window, main);
                }
                finally
                {
                    File.Delete(Hold);
                    window.Close();
                    main.Dispose();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    /// <summary>A runner with no Aurora library beside it draws the emulator's own pictures: no cache, nothing waits, and ray
    /// tracing would turn on with no question.</summary>
    [Fact]
    public async Task WithoutAuroraBesideTheRunnerNothingIsBuiltAndNothingWaits()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the runner is a shell script here");
        var state = State();
        try
        {
            File.Delete(Path.Combine(_dir, "aurora-present.dll"));
            await state.CheckPipelinesAsync();
            Assert.Equal((PipelinePhase.None, (string?)null), (state.Pipelines.Phase, state.PipelinesBlock));
            Assert.False(File.Exists(Builds));
            Assert.True(await state.RaysReadyAsync());
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>Two builds never run at once: another look's build starts once the build before it has gone. Its runner is
    /// ended when it is cancelled, and goes a moment later (here 300 ms); two runners at once wrote one Dawn cache and one
    /// launch file (build k: the plain and the rays' look, 01:16).</summary>
    [Fact]
    public async Task ANewLooksBuildStartsOnlyOnceTheBuildBeforeHasGone()
    {
        var gate = new Lock();
        var (running, most) = (0, 0);
        var looks = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = State(async (_, launch, _, cancel) =>
        {
            lock (gate)
            {
                running++;
                most = Math.Max(most, running);
                // the look the build's launch file asks for (written compact or indented)
                var rays = launch.Contains("\"auroraRayTracing\":true", StringComparison.Ordinal)
                    || launch.Contains("\"auroraRayTracing\": true", StringComparison.Ordinal);
                looks.Add(rays ? PipelineCache.Rays : PipelineCache.Plain);
            }
            try
            {
                await Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, cancel)).ConfigureAwait(false);
                if (cancel.IsCancellationRequested)
                {
                    await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);   // (the killed runner going)
                    throw new OperationCanceledException(cancel);
                }
                return new PipelineBuildEnd(true, null);
            }
            finally
            {
                lock (gate)
                {
                    running--;
                }
            }
        });
        var token = TestContext.Current.CancellationToken;
        try
        {
            await state.CheckPipelinesAsync();
            await Until(() => { lock (gate) { return looks.Count == 1; } }, token, "the plain look's build did not start");
            state.Update(s => s with { RayTracing = true });   // another look: the plain one's build is cancelled
            await Until(() => state.Pipelines is { Phase: PipelinePhase.Building, Look: PipelineCache.Rays }, token, "the rays' build was not asked for");
            await Until(() => { lock (gate) { return looks.Count == 2; } }, token, "the rays' build did not start");
            release.TrySetResult();
            await Until(() => state.Pipelines is { Phase: PipelinePhase.Ready, Look: PipelineCache.Rays }, token, "the rays' build did not end");
            lock (gate)
            {
                Assert.Equal(1, most);
                Assert.Equal([PipelineCache.Plain, PipelineCache.Rays], looks);
            }
        }
        finally
        {
            release.TrySetResult();
            state.Shutdown();
        }
    }

    [Fact]
    public async Task RayTracingAsksBeforeItsFirstBuildAndCancelLeavesItOff()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the runner is a shell script here");
        var state = State();
        await OnTheLauncher(state, (window, main) =>
        {
            PumpTelling(state, () => state.Pipelines.Phase == PipelinePhase.Ready, "the plain look was not built");
            main.ShowSettings(SettingsTab.Game);
            Pump(() => main.Settings.RayTracingReason.Length > 0 || main.Settings.RayTracingPossible);
            main.Settings.RayTracingPossible = true;                             // (the made-up card can)
            Dispatcher.UIThread.RunJobs();
            var box = Named<ToggleSwitch>(window, "RayTracing");

            // turned on: the question, the switch back off, nothing built
            box.BringIntoView();
            Click(window, box);
            Pump(() => main.Graphics.Asking, $"the question was not asked (box {box.IsChecked}, shown {box.IsEffectivelyVisible}, on {box.IsEffectivelyEnabled}, setting {state.Settings.RayTracing}, first {state.RaysBuildFirst})");
            Assert.False(box.IsChecked);
            Assert.False(state.Settings.RayTracing);
            Shot(window, "4-rays-question");
            Click(window, Named<Panel>(window, "RaysQuestion").GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            Pump(() => !main.Graphics.Asking);
            Assert.False(state.Settings.RayTracing);
            Assert.False(box.IsChecked);
            Assert.Equal(PipelinePhase.Ready, state.Pipelines.Phase);
            Assert.Equal(1, BuildsRun);

            // turned on again and Build: on, and its look built while every start waits
            File.WriteAllText(Hold, "");
            Click(window, box);
            Pump(() => main.Graphics.Asking, "the question was not asked again");
            Click(window, Named<Panel>(window, "RaysQuestion").GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Build")));
            PumpTelling(state, () => state.Pipelines is { Phase: PipelinePhase.Building, Look: PipelineCache.Rays, Fraction: > 0.4 }, "the rays' build did not start");
            Assert.True(state.Settings.RayTracing);
            Pump(() => box.IsChecked == true && Column(window)?.Bar > 0.4);
            Assert.Equal(("Preparing graphics · 42%", (string?)null, 0.42), Column(window));
            Assert.Equal(AppState.PreparingText, state.PlayOffline());
            Shot(window, "5-rays-progress");
            main.Current = main.Home;
            Pump(() => main.Control is { GameEnabled: false, GameNote: AppState.PreparingText }, "Start F-Zero GX on, or no reason");
            Shot(window, "6-rays-blocked-buttons");
            File.Delete(Hold);
            PumpTelling(state, () => state.Pipelines is { Phase: PipelinePhase.Ready, Look: PipelineCache.Rays }, "the rays' build did not end");
            Assert.Equal(2, BuildsRun);

            // built: off and on again asks nothing and builds nothing
            main.ShowSettings(SettingsTab.Game);
            Pump(() => Named<ToggleSwitch>(window, "RayTracing").IsEffectivelyVisible);
            box = Named<ToggleSwitch>(window, "RayTracing");
            box.BringIntoView();
            Click(window, box);
            PumpTelling(state, () => !state.Settings.RayTracing && state.Pipelines is { Phase: PipelinePhase.Ready, Look: PipelineCache.Plain },
                $"not off: box {box.IsChecked}, setting {state.Settings.RayTracing}, {state.Pipelines}");
            Click(window, box);
            PumpTelling(state, () => state.Settings.RayTracing && state.Pipelines is { Phase: PipelinePhase.Ready, Look: PipelineCache.Rays },
                $"not on: box {box.IsChecked}, setting {state.Settings.RayTracing}, {state.Pipelines}, asking {main.Graphics.Asking}");
            Assert.False(main.Graphics.Asking);
        });
        Assert.Equal(2, BuildsRun);
    }

    [Fact]
    public async Task AnotherAuroraClearsDawnsCacheAndBuildsAgainSayingWhy()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the runner is a shell script here");
        var state = State();
        await OnTheLauncher(state, (window, main) =>
            PumpTelling(state, () => state.Pipelines.Phase == PipelinePhase.Ready, "the plain look was not built"));
        var dawn = Path.Combine(PipelineCache.Folder(state.GamePaths), "dawn_cache.db");
        File.WriteAllText(dawn, "compiled");
        File.WriteAllText(Path.Combine(_dir, "aurora-present.dll"), "aurora 2");   // an update of the game's picture
        File.WriteAllText(Hold, "");
        var again = AppState.Load(Data);
        again.CardRayTraces = () => true;
        again.PipelineDrivers = () => "Made-up Graphics 1.0";
        await OnTheLauncher(again, (window, main) =>
        {
            PumpTelling(again, () => again.Pipelines is { Phase: PipelinePhase.Building, Fraction: > 0.4 }, "the build did not start again");
            Assert.Equal(PipelineWhy.Aurora, again.Pipelines.Why);
            Assert.False(File.Exists(dawn));                                      // nothing of the old Aurora's is of use
            Pump(() => Column(window) is { Line: not null, Below: null });
            Shot(window, "7-stamp-change-build");
            File.Delete(Hold);
            PumpTelling(again, () => again.Pipelines.Phase == PipelinePhase.Ready, "the build did not end");
        });
        Assert.Equal(2, BuildsRun);
    }
}
