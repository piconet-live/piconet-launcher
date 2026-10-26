using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;
using LobbyView = Launcher.App.Views.LobbyView;

namespace Launcher.Tests;

/// <summary>
/// The lobby's stat pack and notice (handbook/stat-packs.md), in the launcher's own window without a screen, on a
/// server the test leads message by message (<see cref="LedServer"/>): the pack's label with a name and an author of
/// forty characters each, on the machine stage's course band beside the laps (never on the lobby's top line), cut,
/// leaving the band's other things their place, and "Stock" for none; and a match
/// start whose pack is refused, whose sentence is the page's notice, whole and wrapped, until its cause is gone,
/// and hides neither what the lobby is doing nor a server's own word.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class LobbyPackLineTests : IDisposable
{
    private const string LongName = "A Stat Pack With A Name Forty Letters On";
    private const string LongAuthor = "An Author Whose Name Is Forty Letters Ok";
    private const string Refused = "Stat pack not taken · machine 6 Weight is the bits 00000001, which is no number " +
                                   "a pack may hold (a denormal, -0, an infinity or none at all).";

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly StatPack Good = StatPack.Of(LongName, [StatEntry.Of(6, 0x04, 1620)], LongAuthor).Pack!;

    private static LobbyState Lobby(string stage, StatPackName? pack, string? waiting = null, string id = "l1") =>
        LedServer.Lobby(stage, waiting: waiting) with { LobbyId = id, StatPack = pack };

    /// <summary>A match start whose pack holds a denormal weight, with the fingerprint of its own values.</summary>
    private static MatchStart BadStart()
    {
        byte[] values = [6, 4, 0, 0, 0, 1];
#pragma warning disable CA5350   // the fingerprint is a name, not a guard
        var print = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([1, .. values]));
#pragma warning restore CA5350
        return new MatchStart("m1", new RelayEndpoint("r", "127.0.0.1", 47811), "1f2e", 0,
            new RaceSetup(8, 3, [new SeatSetup(0, "p1", "ANA", 6, null)], 1, 2, Stats: new StatPackWire(LongName, print, Convert.ToBase64String(values))));
    }

    [Fact]
    public async Task ALongLabelIsCutAndARefusedPacksSentenceIsTheNoticeUntilItsCauseIsGone()
    {
        Assert.Equal((40, 40), (LongName.Length, LongAuthor.Length));
        var cancel = TestContext.Current.CancellationToken;
        var named = new StatPackName(LongName, Good.Fingerprint, LongAuthor);
        await using var server = new LedServer();
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        void Send(params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Connections == 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
                Send(LedServer.Welcome(), new StatPackValues(Good.ToWire()), Lobby(LobbyState.Forming, named));
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = main.Lobby;
                var (lobby, session) = (main.Lobby, state.Session!);
                PumpTelling(state, () => lobby.PackShown);
                var view = window.GetVisualDescendants().OfType<LobbyView>().Single();

                TheLineHoldsNoPack(view);
                ARefusedPacksSentenceIsTheNotice(view, lobby, session, state, Send, named);
                TheSentenceGoesWithItsCause(lobby, session, state, Send, named);

                // the machine stage: the pack on the course band, beside the laps, cut to its room
                Send(new ChooseMachine("m2", 21, [.. Enumerable.Range(0, 41)], 40_000), Lobby(LobbyState.Choosing, named));
                PumpTelling(state, () => lobby.ChoiceShown && lobby.PackShown);
                PumpFor(200, state);
                TheLabelIsOnTheCourseBandCutToItsRoom(view, lobby);
                // on the game's own stats: "Stock"
                Send(Lobby(LobbyState.Choosing, null));
                PumpTelling(state, () => !lobby.PackShown);
                Assert.Equal(("Stock", true), (Named<TextBlock>(view, "Pack").Text, Named<TextBlock>(view, "Pack").IsEffectivelyVisible));
                TheLineHoldsNoPack(view);
            }, cancel);
        }
        finally
        {
            // (the session ends first, while the window's thread is still there: LobbyStageRulesTests says why)
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

    /// <summary>The lobby's top line (its steps, the clock, the buttons) holds no pack: it is a race rule, on the
    /// machine stage's course band (on the crowded line it was cut off, or dropped).</summary>
    private static void TheLineHoldsNoPack(LobbyView view)
    {
        var line = Named<Grid>(view, "Line");
        Assert.DoesNotContain(line.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Pack");
        var buttons = Named<StackPanel>(view, "LineButtons");
        Assert.InRange(buttons.TranslatePoint(new Point(buttons.Bounds.Width, 0), line)!.Value.X, line.Bounds.Width - 1, line.Bounds.Width + 0.5);
    }

    /// <summary>The label on the course band: name and author beside the laps, in the text's colour, cut to its most
    /// with the whole label when pointed at; the band's other things in their places beside it.</summary>
    private static void TheLabelIsOnTheCourseBandCutToItsRoom(LobbyView view, LobbyViewModel lobby)
    {
        var label = Named<TextBlock>(view, "Pack");
        var band = Named<Grid>(view, "Band");
        Assert.True(band.IsVisualAncestorOf(label) && label.IsEffectivelyVisible);
        Assert.Equal($"{LongName} · {LongAuthor}", lobby.PackText);
        Assert.Equal((lobby.PackText, lobby.PackText, TextTrimming.CharacterEllipsis),
            (label.Text, ToolTip.GetTip(label) as string, label.TextTrimming));
        // (83 characters are wider than its 280 px: cut, and the laps and who is ready keep their places)
        Assert.True(label.Bounds.Width <= 280.5 && label.TextLayout.TextLines[0].HasCollapsed, $"the label is {label.Bounds.Width} px, not cut");
        double Right(Control part) => part.TranslatePoint(new Point(part.Bounds.Width, 0), band)!.Value.X;
        double Left(Control part) => part.TranslatePoint(default, band)!.Value.X;
        var course = Named<TextBlock>(view, "Course");
        var ready = Named<TextBlock>(view, "ReadyCount");
        Assert.True(Right(course) <= Left(label) && Right(label) <= Left(ready), "the label lies over its neighbours");
    }

    /// <summary>A match start whose pack is refused: no game, and the sentence whole in the page's notice; what the
    /// lobby is doing keeps its place beside the steps, and a server's own word is the status.</summary>
    private static void ARefusedPacksSentenceIsTheNotice(LobbyView view, LobbyViewModel lobby, QuickPlaySession session, AppState state,
        Action<ServerMessage[]> send, StatPackName named)
    {
        void Send(params ServerMessage[] messages) => send(messages);
        // a match start whose pack is refused: no game, and the sentence whole in the page's notice
        Send(BadStart());
        PumpTelling(state, () => lobby.Notice.Length > 0);
        // a label, the pack as its value, the whole sentence in its tooltip (labels and values, no sentences)
        Assert.Equal(("Stat pack not taken", Refused, Refused, Refused), (lobby.Notice, lobby.NoticeWhole, session.StatPackProblem, session.Status));
        Assert.Equal([new NoticeValue("pack", lobby.PackText)], lobby.NoticeValues);
        var notice = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Stat pack not taken");
        Assert.True(notice.IsEffectivelyVisible);
        Assert.Equal(Refused, ToolTip.GetTip(Named<WrapPanel>(view, "Notice")));
        Assert.Equal("", lobby.StepNote);
        Assert.False(state.Racing);

        // what the lobby is doing stands beside the steps, and a server's own word is the status
        Send(Lobby(LobbyState.Starting, named, LobbyState.WaitingForRelay));
        PumpTelling(state, () => lobby.StepNote == "Starting race server");
        Assert.Equal(Refused, lobby.NoticeWhole);
        Send(new ErrorMessage("not_ready", "The server says no."));
        PumpTelling(state, () => session.Status == "The server says no.");
        Send(new Requeued("m1", "latency"));
        PumpTelling(state, () => session.Status != "The server says no.");
        Assert.NotEqual(Refused, session.Status);                      // the requeue's own words
        Send(Lobby(LobbyState.Forming, named));
        PumpTelling(state, () => session.Status == Refused);             // the lobby's everyday word does not word it away
        Assert.Equal(Refused, lobby.NoticeWhole);
    }

    /// <summary>Its cause gone: the lobby on another pack, on none, or another lobby.</summary>
    private static void TheSentenceGoesWithItsCause(LobbyViewModel lobby, QuickPlaySession session, AppState state,
        Action<ServerMessage[]> send, StatPackName named)
    {
        void Send(params ServerMessage[] messages) => send(messages);
        // its cause gone: the lobby on another pack, on none, or another lobby
        Send(Lobby(LobbyState.Forming, named with { Fingerprint = new string('a', 40) }));
        PumpTelling(state, () => lobby.Notice == "");
        Assert.Null(session.StatPackProblem);
        Assert.StartsWith("Lobby", session.Status, StringComparison.Ordinal);
        foreach (var next in new[] { Lobby(LobbyState.Forming, null), Lobby(LobbyState.Forming, named, id: "l2") })
        {
            Send(Lobby(LobbyState.Forming, named), BadStart());
            PumpTelling(state, () => lobby.NoticeWhole == Refused);
            Send(next);
            PumpTelling(state, () => lobby.Notice == "");
            Assert.Null(session.StatPackProblem);
        }
    }
}
