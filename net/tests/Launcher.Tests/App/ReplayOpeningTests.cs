using Avalonia.Headless;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>A replay file double-clicked (<see cref="ReplayOpening"/>): handed to the open window, or given to this
/// start's window, it is played on the Replays page as its "Open a file…" plays it, which says why when it cannot
/// (here: no game files); while the first screen shows nothing is played. No game is started.</summary>
[Collection(HeadlessApp.Name)]
public sealed class ReplayOpeningTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File => Path.Combine(_dir, "20261108-201500-m1.fzrp");

    private async Task OnAWindow(bool firstScreen, string? startsWith, Action<MainViewModel, AppState> steps)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        System.IO.File.WriteAllText(Path.Combine(data, "settings.json"), """{"noServer": true, "soundMuted": true}""");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                if (!firstScreen)
                {
                    main.Current = main.Home;
                }
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                using var opening = ReplayOpening.Listen(state, main, window, startsWith);
                window.Show();
                try
                {
                    steps(main, state);
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
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    /// <summary>The Replays page took the file and said what its Play says.</summary>
    private void Played(MainViewModel main, AppState state)
    {
        Pump(() => main.Current is ReplaysViewModel && main.Replays.Notice.Length > 0, "the Replays page did not take the file");
        Assert.Same(main.Replays, main.Current);
        Assert.Equal(state.Play(File), main.Replays.Notice);   // (the same refusal: no game files here)
        Assert.False(state.Racing);
    }

    [Fact]
    public Task AFileHandedOverIsPlayedOnTheReplaysPage() => OnAWindow(firstScreen: false, startsWith: null, (main, state) =>
    {
        Assert.Same(main.Home, main.Current);
        Assert.True(ReplayHandOver.Send(ReplayHandOver.NameOf(state.DataFolder), File, TimeSpan.FromSeconds(10)));
        Played(main, state);
    });

    [Fact]
    public Task TheStartsOwnFileIsPlayedOnceTheWindowIsOpen() => OnAWindow(firstScreen: false, startsWith: File, Played);

    [Fact]
    public Task WhileTheFirstScreenShowsNothingIsPlayed() => OnAWindow(firstScreen: true, startsWith: null, (main, state) =>
    {
        Assert.IsType<DiscViewModel>(main.Current);
        Assert.True(ReplayHandOver.Send(ReplayHandOver.NameOf(state.DataFolder), File, TimeSpan.FromSeconds(10)));
        PumpFor(300);
        Assert.IsType<DiscViewModel>(main.Current);
        Assert.Equal("", main.Replays.Notice);
    });
}
