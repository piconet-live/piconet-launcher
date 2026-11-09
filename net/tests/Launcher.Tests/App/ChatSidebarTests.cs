using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The chat sidebar (ChatSidebarViewModel, ChatSidebar) on the stand-in service: the lobby's and the global
/// chat on tabs of their own, each without the other's lines (the server's notices in both); a player's line not in
/// view puts a dot on its tab and on the button beside the bell, the player's own puts none, and seeing the tab takes
/// it away; unpinned it closes on a press elsewhere and Escape, pinned it stays and the page narrows beside it; the
/// pin, a pinned sidebar's being open and the width are kept for the next start, the width within its least and
/// most. With CHAT_SHOTS naming a folder, the sidebar's states are kept there as PNGs (1280 x 720).</summary>
[Collection(HeadlessApp.Name)]
public sealed class ChatSidebarTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private AppState State(string more = "")
    {
        Directory.CreateDirectory(Data);
        var settings = Path.Combine(Data, "settings.json");
        if (!File.Exists(settings) || more.Length > 0)
        {
            File.WriteAllText(settings,
                """{"noServer": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, """ +
                more + "\"discPath\": " + System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        }
        return AppState.Load(Data, standIn: true);
    }

    /// <summary>The window on the launcher's state, at the least size, and the work done in it; everything closed
    /// after.</summary>
    private static async Task InWindow(AppState state, Action<MainWindow, MainViewModel> work, double height = 720)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 1280, Height = height };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                work(window, main);
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

    /// <summary>Into a custom lobby of the stand-in's, made by the player, once two racers have come in (they chat
    /// back a moment after a line).</summary>
    private static void IntoLobby(AppState state, MainViewModel main)
    {
        main.Current = main.Lobbies;
        PumpTelling(state, () => main.Lobbies.Rows.Count == 3, "the browser listed nothing");
        main.Lobbies.ShowCreateCommand.Execute(null);
        main.Lobbies.CreateCommand.Execute(null);
        PumpTelling(state, () => main.Current is LobbyViewModel && main.Chat.InLobby, "the lobby was not made");
        // the two stand-ins who come in after it (nobody answers in an empty lobby)
        PumpTelling(state, () => main.Lobby.SeatColumns.SelectMany(c => c.Seats).Count(r => r.Id is not null) >= 3,
            "the stand-ins did not come in");
    }

    private static void Say(AppState state, MainViewModel main, string text, bool global)
    {
        main.Lobby.Message = text;
        (global ? main.Lobby.SendGlobalCommand : main.Lobby.SendCommand).Execute(null);
        PumpTelling(state, () => (global ? main.Lobby.GlobalChat : main.Lobby.Chat).Any(c => c.Text == text), $"the line was not shown: {text}");
    }

    private static void Shot(Window window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("CHAT_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        window.MouseMove(new Point(120, 700));   // (the pointer off the lines: no tag shows)
        PumpFor(500, state);
        Aim();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static Rect InWindow(Window window, Control control) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    [Fact]
    public async Task TheLobbyAndTheGlobalChatKeepTheirLinesApart()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Assert.Equal((false, ChatSidebarViewModel.NotConnectedText), (main.Chat.Open, main.Chat.Standby));
            IntoLobby(state, main);
            Say(state, main, "hello lobby", global: false);
            Say(state, main, "hello everyone", global: true);
            // the stand-in's racer answers each on its own channel
            PumpTelling(state, () => main.Lobby.GlobalChat.Any(c => !c.You && !c.Server), "nobody answered on the global chat");
            PumpTelling(state, () => main.Lobby.Chat.Any(c => !c.You && !c.Server), "nobody answered on the lobby's chat");
            Assert.DoesNotContain(main.Lobby.Chat, c => c.Text == "hello everyone");
            Assert.DoesNotContain(main.Lobby.GlobalChat, c => c.Text == "hello lobby");
            // the server's notice (the stand-in's word that it is one) is in both
            Assert.Contains(main.Lobby.Chat, c => c.Server);
            Assert.Contains(main.Lobby.GlobalChat, c => c.Server);
            Assert.Equal("", main.Chat.Standby);
        });
    }

    [Fact]
    public async Task ALineNotInViewPutsADotOnItsTabAndTheButtonAndSeeingItTakesItAway()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            IntoLobby(state, main);
            var dot = Named<Avalonia.Controls.Shapes.Ellipse>(window, "ChatDot");
            // the player's own line and a server's notice put none (each as the lobby page adds it; the stand-ins
            // speak when they will, so the rule is shown on lines of the test's own)
            main.Chat.Open = true;
            main.Chat.Open = false;
            Assert.False(main.Chat.HasDot);
            main.Lobby.Chat.Add(new ChatLine("me", "ANA", "#E8432E", "my own", you: true));
            main.Lobby.Chat.Add(main.Lobby.Chat.First(c => c.Server));
            Assert.False(main.Chat.LobbyDot, "the player's own line or a notice put a dot on");
            main.Lobby.Chat.Add(new ChatLine("p9", "BEN", "#4DA3FF", "hello", you: false));
            Assert.True(main.Chat.LobbyDot, "another player's line with the sidebar closed put no dot on its tab");
            main.Chat.Open = true;
            main.Chat.Open = false;
            Say(state, main, "anyone here?", global: false);
            PumpTelling(state, () => main.Chat.LobbyDot, "an answer with the sidebar closed put no dot on its tab");
            Assert.True(main.Chat.HasDot);
            Assert.True(dot.IsEffectivelyVisible, "the button showed no dot");
            Assert.False(main.Chat.GlobalDot);

            Click(window, Named<Button>(window, "ChatToggle"));
            Assert.True(main.Chat.Open);
            Assert.Equal(ChatTab.Lobby, main.Chat.Tab);
            Assert.False(main.Chat.LobbyDot, "the tab in view kept its dot");
            Assert.False(dot.IsEffectivelyVisible);

            // the global tab in view: a lobby line puts the lobby's dot on, the global tab's own lines none
            Click(window, Named<Button>(window, "GlobalTab"));
            Assert.Equal(ChatTab.Global, main.Chat.Tab);
            Say(state, main, "one more", global: false);
            PumpTelling(state, () => main.Chat.LobbyDot, "a line of the tab not in view put no dot on it");
            Assert.True(Named<Avalonia.Controls.Shapes.Ellipse>(window, "LobbyDot").IsEffectivelyVisible);
            Say(state, main, "hi all", global: true);
            PumpTelling(state, () => main.Lobby.GlobalChat.Any(c => !c.You && !c.Server), "nobody answered on the global chat");
            Assert.False(main.Chat.GlobalDot, "a line of the tab in view put a dot on it");
            Click(window, Named<Button>(window, "LobbyTab"));
            Assert.False(main.Chat.LobbyDot);
            Assert.False(main.Chat.HasDot);
        });
    }

    [Fact]
    public async Task UnpinnedItClosesOnAPressElsewhereAndPinnedThePageNarrowsBesideIt()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var toggle = Named<Button>(window, "ChatToggle");
            var sidebar = Named<ChatSidebar>(window, "ChatSidebar");
            var page = Named<ScrollViewer>(window, "PageScroll");
            var full = InWindow(window, page).Width;
            Click(window, toggle);
            Assert.True(main.Chat.Overlay);
            Aim();
            // over the page: the page keeps its width, the sidebar stands at the window's right end
            Assert.Equal(full, InWindow(window, page).Width);
            Assert.Equal(window.Bounds.Width, InWindow(window, sidebar).Right, 0.5);
            Click(window, Named<Button>(window, "PlayersTab"));
            Assert.True(main.Chat.Open, "a press in the sidebar closed it");
            Click(window, Named<Control>(window, "PageHost"));
            Assert.False(main.Chat.Open, "a press on the page left the unpinned sidebar open");
            Click(window, toggle);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Chat.Open, "Escape left the unpinned sidebar open");

            Click(window, toggle);
            Click(window, Named<Button>(window, "Pin"));
            Assert.True(main.Chat.Column);
            Click(window, Named<Control>(window, "PageHost"));
            Assert.True(main.Chat.Open, "a press on the page closed the pinned sidebar");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(main.Chat.Open, "Escape closed the pinned sidebar");
            Aim();
            // a column of its own: the page ends where the sidebar begins
            Assert.Equal(full - main.Chat.ShownWidth, InWindow(window, page).Width, 0.5);
            Assert.True(InWindow(window, page).Right <= InWindow(window, sidebar).Left + 0.5, "the pinned sidebar covered the page");
            Assert.True(main.Chat.Open);
            main.Current = main.Garage;
            Aim();
            Assert.True(sidebar.IsEffectivelyVisible, "the pinned sidebar went with the page");
            Click(window, Named<Button>(window, "CloseChat"));
            Assert.False(main.Chat.Open);
        });
    }

    [Fact]
    public async Task TheEdgeDraggedSetsTheWidthWithinItsEnds()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Click(window, Named<Button>(window, "ChatToggle"));
            var grip = Named<Border>(window, "Grip");
            Aim();
            var at = InWindow(window, grip).Center;
            var before = main.Chat.Width;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at - new Point(60, 0));
            window.MouseUp(at - new Point(60, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 60, main.Chat.Width);
            Assert.Equal((int)(before + 60), state.Settings.ChatWidth);
            main.Chat.Drag(5000);
            Assert.Equal(LauncherSettings.MostChatWidth, main.Chat.Width);
            main.Chat.Drag(10);
            Assert.Equal(LauncherSettings.LeastChatWidth, main.Chat.Width);
        });
    }

    [Fact]
    public async Task ThePinAPinnedSidebarsBeingOpenAndTheWidthAreKeptForTheNextStart()
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(Starts, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    private void Starts()
    {
        var first = State();
        var chat = new ChatSidebarViewModel(first, new LobbyViewModel(first, () => { }));
        Assert.Equal((false, false, (double)LauncherSettings.DefaultChatWidth), (chat.Pinned, chat.Open, chat.Width));
        chat.Open = true;
        chat.Drag(400);
        chat.KeepWidth();
        first.Shutdown();
        // unpinned: open is not kept
        var second = State();
        chat = new ChatSidebarViewModel(second, new LobbyViewModel(second, () => { }));
        Assert.Equal((false, false, 400d), (chat.Pinned, chat.Open, chat.Width));
        chat.Open = true;
        chat.Pinned = true;
        second.Shutdown();
        var third = State();
        chat = new ChatSidebarViewModel(third, new LobbyViewModel(third, () => { }));
        Assert.Equal((true, true, 400d), (chat.Pinned, chat.Open, chat.Width));
        Assert.True(chat.Column);
        chat.Open = false;
        third.Shutdown();
        var fourth = State();
        chat = new ChatSidebarViewModel(fourth, new LobbyViewModel(fourth, () => { }));
        Assert.Equal((true, false), (chat.Pinned, chat.Open));
        fourth.Shutdown();
    }

    [Theory]
    [InlineData("9999", LauncherSettings.MostChatWidth)]
    [InlineData("12", LauncherSettings.LeastChatWidth)]
    [InlineData("\"wide\"", LauncherSettings.DefaultChatWidth)]
    public void AWidthAHandWroteIsHeldToTheSidebarsEnds(string written, int kept)
    {
        var state = State($"\"chatWidth\": {written}, ");
        Assert.Equal(kept, state.Settings.ChatWidth);
        state.Shutdown();
    }

    /// <summary>The sidebar's states as the owner sees them (CHAT_SHOTS keeps them), each checked for what would show
    /// wrong: the sidebar at the window's right end, under the title bar, the page beside a pinned one.</summary>
    [Fact]
    public async Task TheSidebarInEachOfItsStates()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var sidebar = Named<ChatSidebar>(window, "ChatSidebar");
            var page = Named<ScrollViewer>(window, "PageScroll");
            void Fits()
            {
                Aim();
                var box = InWindow(window, sidebar);
                Assert.Equal(window.Bounds.Width, box.Right, 0.5);
                Assert.True(box.Top >= 32, "the sidebar ran over the title bar");
                Assert.Equal(window.Bounds.Height, box.Bottom, 0.5);
                if (main.Chat.Column)
                {
                    Assert.True(InWindow(window, page).Right <= box.Left + 0.5, "the pinned sidebar covered the page");
                }
            }
            Shot(window, state, "01-closed-home");
            Click(window, Named<Button>(window, "ChatToggle"));
            Click(window, Named<Button>(window, "GlobalTab"));
            Fits();
            Shot(window, state, "02-open-home-not-connected");
            main.Chat.Open = false;

            IntoLobby(state, main);
            Say(state, main, "anyone up for Big Blue?", global: false);
            PumpTelling(state, () => main.Chat.LobbyDot, "no dot");
            Shot(window, state, "03-closed-lobby-dot");
            Click(window, Named<Button>(window, "ChatToggle"));
            Click(window, Named<Button>(window, "LobbyTab"));
            Fits();
            Shot(window, state, "04-unpinned-open-lobby-tab");
            Click(window, Named<Button>(window, "Pin"));
            Fits();
            Shot(window, state, "05-pinned-lobby-tab");
            Say(state, main, "good luck out there", global: true);
            Click(window, Named<Button>(window, "GlobalTab"));
            PumpTelling(state, () => main.Lobby.GlobalChat.Any(c => !c.You && !c.Server), "nobody answered on the global chat");
            Fits();
            Shot(window, state, "06-pinned-global-tab");
            Click(window, Named<Button>(window, "PlayersTab"));
            Fits();
            Shot(window, state, "07-pinned-players-tab");
            main.Current = main.Garage;
            Click(window, Named<Button>(window, "GlobalTab"));
            Fits();
            Shot(window, state, "08-pinned-global-on-garage");
            main.Current = main.Lobby;

            Click(window, Named<Button>(window, "LobbyTab"));
            for (var i = 0; i < 40; i++)
            {
                Say(state, main, $"line {i + 1}: a longer conversation, to scroll back through", global: false);
            }
            var chat = Named<LobbyChat>(window, "LobbyChat");
            var lines = Named<ScrollViewer>(chat, "Lines");
            lines.Offset = new Vector(0, 200);
            PumpFor(100, state);
            Say(state, main, "a line that came while scrolled up", global: false);
            PumpTelling(state, () => Named<Button>(chat, "MoreBelow").IsVisible, "the way back to the newest line was not offered");
            Assert.Equal(200, lines.Offset.Y);
            Fits();
            Shot(window, state, "09-scrolled-up-more-below");
            Click(window, Named<Button>(chat, "MoreBelow"));

            main.Chat.Drag(LauncherSettings.LeastChatWidth);
            Fits();
            Shot(window, state, "10-pinned-narrow");
            // pinned at the least window, the page keeps its 740 px: no wider than 300
            main.Chat.Drag(LauncherSettings.MostChatWidth);
            Fits();
            Assert.Equal(1280 - 240 - ChatSidebarViewModel.PageLeast, main.Chat.ShownWidth, 0.5);
            Shot(window, state, "11-pinned-widest-at-1280");
            // unpinned it may be as wide as it goes, over the page
            Click(window, Named<Button>(window, "Pin"));
            main.Chat.Drag(LauncherSettings.MostChatWidth);
            Fits();
            Assert.Equal(LauncherSettings.MostChatWidth, main.Chat.ShownWidth);
            Shot(window, state, "12-unpinned-wide");
            main.Current = main.Garage;
            Click(window, Named<Button>(window, "Pin"));
            Fits();
            Shot(window, state, "13-pinned-garage");
        });
    }
}
