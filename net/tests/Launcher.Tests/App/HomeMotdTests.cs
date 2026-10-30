using System.Net;
using Avalonia.Controls;
using LobbyView = Online.Protocol.LobbyView;
using RacerView = Online.Protocol.RacerView;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The server's message of the day on Home, in the launcher's own window, from the public status the test answers
/// itself (the launcher at Home holds no connection, so no welcome sends it): it shows as a notice card while the
/// status carries it, a new one takes its place on the card and in the bell's list (without the dot), and it goes
/// from Home when the status has none. LOBBY_SHOTS=folder saves the page (home-motd-*.png: no lobby, a lobby with its
/// racers, at 1280x800 and the least window, 1280x720).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class HomeMotdTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A long one: about as long as the server takes (200 characters).</summary>
    private static readonly RecentNotice First = new("r1-1", Notices.Notice,
        "Weekly netplay sessions this week starting on 10/30 at 8:00 PM EST! This Saturday at 6:00 PM EST. This Sunday at 2:00 PM EST. Bring a friend, new racers welcome. Join us on Discord!",
        DateTimeOffset.UtcNow);

    private static readonly RecentNotice Second = new("r1-2", Notices.Notice, "Cup night Friday", DateTimeOffset.UtcNow);

    [Fact]
    public async Task TheMessageOfTheDayStandsOnHomeUntilCleared()
    {
        var cancel = TestContext.Current.CancellationToken;
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            "{\"server\": \"ws://127.0.0.1:9/ws\", \"recordReplays\": false, \"racer\": {\"name\": \"ANA\", \"machine\": 6, \"color\": {\"r\": 1, \"g\": 2, \"b\": 3}}, \"discPath\": " +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        using var answers = new StatusAnswers { Motd = First };
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                using var main = new MainViewModel(state, answers);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                window.Show();
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                var home = main.Home;
                PumpTelling(state, () => home.HasMotd, "Home showed no message of the day", 15);
                var place = Named<Control>(window, "Motd");
                Assert.True(place.IsEffectivelyVisible);
                Assert.Equal(First.Text, home.Motd);
                Assert.Equal(["r1-1"], state.Notices.Notices.Select(n => n.Id!));   // in the bell's list
                Assert.False(main.Notices.HasDot);                                   // Home shows it: no dot
                Shot(window, state, "home-motd-empty-1280x800");
                answers.Lobby = true;    // a lobby with its racers beside the map
                PumpTelling(state, () => home.HasPeople, "Home listed no racers", 15);
                Shot(window, state, "home-motd-lobby-1280x800");
                window.Height = 720;
                Dispatcher.UIThread.RunJobs();
                Shot(window, state, "home-motd-lobby-1280x720");
                answers.Lobby = false;
                PumpTelling(state, () => !home.HasPeople, "Home kept the racers", 15);
                Shot(window, state, "home-motd-empty-1280x720");

                answers.Motd = Second;   // the operator set another: it takes the first one's place
                PumpTelling(state, () => home.Motd == Second.Text, "Home kept the old message", 15);
                Assert.Equal(["r1-2"], state.Notices.Notices.Select(n => n.Id!));

                answers.Motd = null;     // cleared
                PumpTelling(state, () => !home.HasMotd, "Home kept a cleared message", 15);
                Assert.False(place.IsEffectivelyVisible);
                window.Close();
            }, cancel);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    private static void Shot(MainWindow window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(400, state);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Answers every ask for the status with an empty server and the message of the day the test sets.</summary>
    private sealed class StatusAnswers : HttpMessageHandler
    {
        public volatile RecentNotice? Motd;

        public volatile bool Lobby;

        private static readonly RacerView[] Racers =
            [.. new[] { "ANA", "BEN", "CID", "DOT", "EVE", "FOX", "GUS", "HAL" }.Select((n, i) => new RacerView(n, i + 1, $"#{(i * 37 + 80) % 256:X2}{(i * 91 + 40) % 256:X2}C8"))];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(
                    new ServiceStatus("test", DateTimeOffset.UtcNow, Lobby ? Racers.Length : 0,
                        Lobby ? [new LobbyView("l1", Racers, 30, false, 0)] : [], [], [], [], Motd: Motd), ControlProtocol.Json)),
            });
    }
}
