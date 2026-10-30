using System.Net;
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
/// from Home when the status has none. LOBBY_SHOTS=folder saves the page as home-motd.png.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class HomeMotdTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly RecentNotice First = new("r1-1", Notices.Notice,
        "Weekly netplay sessions this week: Thursday at 8:00 PM EST, Saturday at 6:00 PM EST, Sunday at 2:00 PM EST. Join us!",
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
                var card = Named<NoticeCard>(window, "Motd");
                Assert.True(card.IsEffectivelyVisible);
                Assert.Equal(("notice", First.Text), (card.Tier, home.Motd!.Text));
                Assert.Equal(["r1-1"], state.Notices.Notices.Select(n => n.Id!));   // in the bell's list
                Assert.False(main.Notices.HasDot);                                   // Home shows it: no dot
                Shot(window, state);

                answers.Motd = Second;   // the operator set another: it takes the first one's place
                PumpTelling(state, () => home.Motd?.Text == Second.Text, "Home kept the old message", 15);
                Assert.Equal(["r1-2"], state.Notices.Notices.Select(n => n.Id!));

                answers.Motd = null;     // cleared
                PumpTelling(state, () => !home.HasMotd, "Home kept a cleared message", 15);
                Assert.False(card.IsEffectivelyVisible);
                window.Close();
            }, cancel);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    private static void Shot(MainWindow window, AppState state)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(400, state);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, "home-motd.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Answers every ask for the status with an empty server and the message of the day the test sets.</summary>
    private sealed class StatusAnswers : HttpMessageHandler
    {
        public volatile RecentNotice? Motd;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(
                    new ServiceStatus("test", DateTimeOffset.UtcNow, 0, [], [], [], [], Motd: Motd), ControlProtocol.Json)),
            });
    }
}
