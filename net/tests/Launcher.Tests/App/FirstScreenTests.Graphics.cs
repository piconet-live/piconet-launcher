using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;

namespace Launcher.Tests;

// The first start with Aurora: once CONTINUE has prepared the game's files, the plain look's graphics cache is built at
// once, with no question, and nothing starts a game until it is done.
public sealed partial class FirstScreenTests
{
    /// <summary>The game program as a script (the tests' runner is otherwise an empty file): a build says 42% built,
    /// holds while <paramref name="hold"/> is there, says done, and is counted in <paramref name="builds"/>; the
    /// controller helper's start says nothing.</summary>
    private static void BuildingRunner(AppState state, string hold, string builds)
    {
        var runner = state.GamePaths.Runner;
        File.WriteAllText(runner, "#!/bin/sh\nif [ \"$1\" = --controllers ]; then cat > /dev/null; exit 0; fi\n"
            + "for a in \"$@\"; do last=\"$a\"; done\n"
            + $"if [ \"$last\" = {PipelineCache.BuildArgument} ]; then\n"
            + $"  echo \"$last\" >> '{builds}'\n"
            + "  echo '{\"progress\": 0.42}'\n"
            + $"  while [ -e '{hold}' ]; do sleep 0.05; done\n"
            + "  echo '{\"done\": true, \"shaders\": 15296, \"seconds\": 1}'\n"
            + "  exit 0\nfi\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runner)!, "aurora-present.dll"), "aurora 1");   // Aurora draws
        state.CardRayTraces = () => true;
        state.PipelineDrivers = () => "Made-up Graphics 1.0";
    }

    [Fact]
    public async Task AfterTheFirstScreenThePlainLookIsBuiltWithNothingStartingUntilItIsDone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the runner is a shell script here");
        var (hold, builds) = (Path.Combine(_dir, "hold"), Path.Combine(_dir, "builds.txt"));
        File.WriteAllText(hold, "");
        var image = _disc.Write(_dir);
        var state = State(stored: null, server: "ws://127.0.0.1:9/ws");
        BuildingRunner(state, hold, builds);
        try
        {
            await OnTheLauncher(state, screen =>
            {
                Assert.NotNull(screen.View);
                Assert.Equal(PipelinePhase.None, state.Pipelines.Phase);         // no game set up: nothing to build
                screen.Type(image);
                screen.Press(screen.Continue);
                Until(() => screen.Opened, "the launcher opens");
                Until(() => state.Pipelines is { Phase: PipelinePhase.Building, Fraction: > 0.4 }, "the plain look's build starts");
                Assert.Equal((PipelineCache.Plain, PipelineWhy.First), (state.Pipelines.Look, state.Pipelines.Why));
                Assert.False(screen.Main.Graphics.Asking);                       // the plain look asks nothing
                Until(() => Column(screen.Window) is { Bar: > 0.4 }, "the column shows the build");
                Assert.Equal(("Preparing graphics · 42%", (string?)null, 0.42), Column(screen.Window));

                // nothing starts a game meanwhile, and each says why
                Assert.Equal((false, AppState.PreparingText, false, null), Buttons(screen));   // (said once, under RACE)
                Assert.Equal(AppState.PreparingText, state.PlayOffline());
                Assert.Equal(AppState.PreparingText, state.StartPractice(screen.Main.Practice.Setup));
                Assert.Equal(AppState.PreparingText, state.WhyNotJoin);
                Shot(screen.Window, "1-first-start-plain-build");
                // the bar's green reaches its share (it grows from nothing: a fill of no width set never showed)
                var bar = screen.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Graphics").GetVisualDescendants().OfType<ProgressBar>().Single();
                var fill = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
                Assert.InRange(fill.Bounds.Width, 1, bar.Bounds.Width * 0.43);
                screen.Main.Current = screen.Main.Practice;
                Until(() => !screen.Main.Practice.StartCommand.CanExecute(null) && screen.Main.Practice.Notice == AppState.PreparingText,
                    "START PRACTICE off with the reason");
                Shot(screen.Window, "2-practice-blocked");
                screen.Main.Current = screen.Main.Replays;
                Until(() => !screen.Main.Replays.PlayCommand.CanExecute("x") && screen.Main.Replays.Notice == AppState.PreparingText,
                    "the replays' Play off with the reason");
                Shot(screen.Window, "3-replays-blocked");

                File.Delete(hold);
                Until(() => state.Pipelines.Phase == PipelinePhase.Ready, "the build ends");
                Until(() => Column(screen.Window) is null, "the column's block goes");
                Assert.True(screen.Main.Practice.StartCommand.CanExecute(null));
                Assert.True(screen.Main.Replays.PlayCommand.CanExecute("x"));
                Assert.Equal("", screen.Main.Replays.Notice);
                screen.Main.Current = screen.Main.Practice;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("", screen.Main.Practice.Notice);
                screen.Main.Current = screen.Main.Home;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal((true, null, true, null), Buttons(screen));
            });
        }
        finally
        {
            File.Delete(hold);
        }
        Assert.Single(File.ReadAllLines(builds));
        Assert.NotNull(PipelineCache.Built(state.GamePaths, PipelineCache.Plain));
    }

    /// <summary>The left column's block while the graphics cache is built: its words, why, and its bar; null: it does
    /// not show.</summary>
    private static (string? Line, string? Below, double Bar)? Column(Window window)
    {
        var block = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Graphics");
        if (!block.IsEffectivelyVisible)
        {
            return null;
        }
        var texts = block.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        return (texts.ElementAtOrDefault(0), texts.ElementAtOrDefault(1), block.GetVisualDescendants().OfType<ProgressBar>().Single().Value);
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("PIPELINE_SHOTS") is { Length: > 0 } folder)
        {
            for (var i = 0; i < 30; i++)
            {
                Thread.Sleep(20);   // (the bar's growth is a transition of half a second)
                Headless.Frame();
            }
            Headless.Aim();
            Directory.CreateDirectory(folder);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }
}
