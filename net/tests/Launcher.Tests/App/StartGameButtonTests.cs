using Avalonia;
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
/// "Start F-Zero GX" at the left column's foot on every page (<see cref="StartGameView"/>): the column's width, just above
/// Settings, on Play and on another tab alike, and no longer on Quick Play's page; pressed, it is the same start as
/// before (offline play: here a launcher without the game, which says why under it). With INPUTS_SHOTS naming a folder,
/// the window on Play and on the Garage is kept there as PNGs.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class StartGameButtonTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Button Start(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartGame" && b.IsEffectivelyVisible);

    /// <summary>A game that is "installed" (a script stands for the runner).</summary>
    private void Installed(AppState state)
    {
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, "#!/bin/sh\nexit 0\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("INPUTS_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(300);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [Fact]
    public async Task StartFZeroGXIsAtTheColumnsFootOnEveryPage()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"noServer": true}""");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                try
                {
                    foreach (var (tab, shot) in new[] { ("play", "start-gx-play"), ("garage", "start-gx-garage") })
                    {
                        main.ShowTabCommand.Execute(tab);
                        Pump(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "StartGame" && b.IsEffectivelyVisible));
                        var start = Start(window);
                        var settings = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsButton");
                        Assert.Equal("Start F-Zero GX", start.Content);
                        Assert.Contains("startgame", start.Classes);
                        var at = start.TranslatePoint(new Point(0, 0), window)!.Value;
                        var settingsAt = settings.TranslatePoint(new Point(0, 0), window)!.Value;
                        // the column's width inside its margins, just above Settings, as wide as Settings
                        Assert.Equal(settings.Bounds.Width, start.Bounds.Width, 0.5);
                        Assert.Equal(settingsAt.X, at.X, 0.5);
                        Assert.InRange(settingsAt.Y - (at.Y + start.Bounds.Height), 0, 80);   // (the line under it between)
                        // without the game it is off, and the line under it says why
                        Assert.False(start.IsEffectivelyEnabled);
                        Shot(window, shot);
                    }
                    // with the game in place it is on, and nothing is said under it
                    if (OperatingSystem.IsLinux())
                    {
                        Installed(state);
                        var look = state.LookForTheGame();
                        main.ShowTabCommand.Execute("play");
                        PumpTelling(state, () => look.IsCompleted && Start(window).IsEffectivelyEnabled, "with the game in place the button stayed off");
                        Assert.False(window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GameNote").IsEffectivelyVisible);
                        Shot(window, "start-gx-on");
                    }
                    // Quick Play's page has RACE and no second start
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<PlayControlView>().SelectMany(v => v.GetVisualDescendants()).OfType<Button>(),
                        b => Equals(b.Content, "Start F-Zero GX") || Equals(b.Content, "Play F-Zero GX"));
                }
                finally
                {
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
}
