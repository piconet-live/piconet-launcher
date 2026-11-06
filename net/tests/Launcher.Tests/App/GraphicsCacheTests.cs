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
/// own build: FirstScreenTests.Graphics.cs). Ray tracing ticked in Settings asks first (Cancel: off, nothing built;
/// Build: on, and its look built while every start waits, the column saying so); once built, ticking it asks nothing.
/// Another Aurora (its library changed) clears Dawn's cache and builds again, saying why. With <c>PIPELINE_SHOTS</c> set
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
    private AppState State()
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
        state.AuroraOffer = () => (true, true);
        state.PipelineDrivers = () => "Made-up Graphics 1.0";
        state.Update(s => s with
        {
            PictureEngine = LauncherSettings.PictureEngines[1].Name,
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
    private static (string? Line, string? Why, double Bar)? Column(Window window)
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
            var box = Named<CheckBox>(window, "RayTracing");

            // ticked: the question, the box back off, nothing built
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

            // ticked again and Build: on, and its look built while every start waits
            File.WriteAllText(Hold, "");
            Click(window, box);
            Pump(() => main.Graphics.Asking, "the question was not asked again");
            Click(window, Named<Panel>(window, "RaysQuestion").GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Build")));
            PumpTelling(state, () => state.Pipelines is { Phase: PipelinePhase.Building, Look: PipelineCache.Rays, Fraction: > 0.4 }, "the rays' build did not start");
            Assert.True(state.Settings.RayTracing);
            Pump(() => box.IsChecked == true && Column(window)?.Bar > 0.4);
            Assert.Equal(("Preparing graphics · 42%", "For ray tracing, once", 0.42), Column(window));
            Assert.Equal(AppState.PreparingText, state.PlayOffline());
            Shot(window, "5-rays-progress");
            main.Current = main.Home;
            Pump(() => main.Control is { GameEnabled: false, GameNote: AppState.PreparingText }, "Play F-Zero GX on, or no reason");
            Shot(window, "6-rays-blocked-buttons");
            File.Delete(Hold);
            PumpTelling(state, () => state.Pipelines is { Phase: PipelinePhase.Ready, Look: PipelineCache.Rays }, "the rays' build did not end");
            Assert.Equal(2, BuildsRun);

            // built: off and on again asks nothing and builds nothing
            main.ShowSettings(SettingsTab.Game);
            Pump(() => Named<CheckBox>(window, "RayTracing").IsEffectivelyVisible);
            box = Named<CheckBox>(window, "RayTracing");
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
        again.AuroraOffer = () => (true, true);
        again.PipelineDrivers = () => "Made-up Graphics 1.0";
        await OnTheLauncher(again, (window, main) =>
        {
            PumpTelling(again, () => again.Pipelines is { Phase: PipelinePhase.Building, Fraction: > 0.4 }, "the build did not start again");
            Assert.Equal(PipelineWhy.Aurora, again.Pipelines.Why);
            Assert.False(File.Exists(dawn));                                      // nothing of the old Aurora's is of use
            Pump(() => Column(window)?.Why == "The game was updated");
            Shot(window, "7-stamp-change-build");
            File.Delete(Hold);
            PumpTelling(again, () => again.Pipelines.Phase == PipelinePhase.Ready, "the build did not end");
        });
        Assert.Equal(2, BuildsRun);
    }
}
