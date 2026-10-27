using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The lobby's chat follows its newest line while the player is at its foot; scrolled up, it stays where
/// the player is and offers More messages below, which takes them back to the foot (as a stream's chat does); a
/// selection holds it too, and the button clears it. With LOBBY_SHOTS naming a folder, the button is kept there as a PNG.</summary>
[Collection(HeadlessApp.Name)]
public sealed class LobbyChatScrollTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data, standIn: true);
    }

    private static T Page<T>(Window window) where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>().First(c => c.IsEffectivelyVisible);

    private static bool AtFoot(ScrollViewer lines) => lines.Offset.Y >= lines.Extent.Height - lines.Viewport.Height - 2;

    private int _said;

    /// <summary>Each line is waited for by its own text: past the 50 lines kept the count no longer grows, the lobby's
    /// CPU players chat too, and a line that came late would move the list after the test looked.</summary>
    private void Say(AppState state, LobbyViewModel lobby, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var text = $"line {_said++} of a chat that runs on past the foot of its list";
            lobby.Message = text;
            lobby.SendCommand.Execute(null);
            PumpTelling(state, () => lobby.Chat.Any(c => c.Text == text), $"a line was not shown: {text}");
        }
        PumpFor(200, state);
    }

    [Fact]
    public async Task TheChatFollowsItsFootAndScrolledUpOffersTheWayBack()
    {
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                main.Current = main.Lobbies;
                var lobbies = main.Lobbies;
                PumpTelling(state, () => lobbies.Rows.Count == 3, "the browser listed nothing");
                lobbies.ShowCreateCommand.Execute(null);
                lobbies.CreateCommand.Execute(null);
                PumpTelling(state, () => main.Current is LobbyViewModel, "the lobby was not made");
                var lobby = main.Lobby;
                PumpTelling(state, () => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<LobbyChat>()
                    .Any(c => c.IsEffectivelyVisible), "the lobby showed no chat");
                var chat = Page<LobbyChat>(window);
                var lines = Named<ScrollViewer>(chat, "Lines");

                Say(state, lobby, 30);
                Assert.True(lines.Extent.Height > lines.Viewport.Height, "the chat did not overflow its list");
                Assert.True(AtFoot(lines), $"the chat did not follow its newest line ({lines.Offset.Y} of {lines.Extent.Height - lines.Viewport.Height})");
                Say(state, lobby, 30);   // (past the 50 lines kept: the oldest go as new ones come)
                Assert.True(AtFoot(lines), $"the chat did not follow past its 50 lines ({lines.Offset.Y} of {lines.Extent.Height - lines.Viewport.Height})");
                var below = Named<Button>(chat, "MoreBelow");
                Assert.False(below.IsVisible);

                lines.Offset = new Avalonia.Vector(0, 0);
                PumpFor(100, state);
                Say(state, lobby, 3);
                Assert.Equal(0, lines.Offset.Y);
                Assert.True(below.IsVisible, "More messages below was not offered");
                if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is { Length: > 0 } folder)
                {
                    PumpFor(400, state);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Directory.CreateDirectory(folder);
                    window.CaptureRenderedFrame()?.Save(Path.Combine(folder, "chat-more-below.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }

                Click(window, below);
                PumpFor(200, state);
                Assert.True(AtFoot(lines));
                Assert.False(below.IsVisible);
                Say(state, lobby, 3);
                Assert.True(AtFoot(lines), "the chat did not follow again after More messages below");

                // a selection holds the list where it is too (the player copies a line); the button takes it away
                var line = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(chat).OfType<SelectableTextBlock>().Last();
                (line.SelectionStart, line.SelectionEnd) = (0, 3);
                // well above the foot: the CPU players' lines come when they will, and a list that loses a long line
                // at its top grows shorter and pulls an offset close to its foot back with it
                lines.Offset = new Avalonia.Vector(0, 100);
                PumpFor(100, state);
                var held = lines.Offset.Y;
                Say(state, lobby, 3);
                Assert.Equal(held, lines.Offset.Y);
                Assert.True(below.IsVisible, "More messages below was not offered over a selection");
                Click(window, below);
                PumpFor(200, state);
                Assert.True(AtFoot(lines));
                Assert.False(chat.Selecting);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }
}
