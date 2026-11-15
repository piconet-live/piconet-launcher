using System.Net;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned interface's Play out of a lobby ("Experimental UI" on), in the launcher's own window with the
/// servers' status answered by the test: Quick Play's server choice (a row a server with its ping, the quickest
/// marked "Best connection", the one last used "Last used" and the pad's landing; a server of the player's own added
/// and removed; no button beside the rows, a row pressed joins its server; the server set's lobbies from the hub, and
/// Back the way it came); the hub's count of who races now, the message of the
/// day's panel and "Hide for today"; the question before the game is ended. And by themselves: the lobby's call back
/// from a practice (<see cref="LobbyRecall"/>), and the settings that keep the servers and the hidden message. With
/// <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs (1280 x 720).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextPlayTests : IDisposable
{
    private const string Quick = "ws://127.0.0.1:9/ws", Slow = "ws://127.0.0.2:9/ws", Third = "ws://127.0.0.3:9/ws";

    /// <summary>A server panel's three ways, by their names.</summary>
    private static readonly string[] Ways = ["Join a race", "Host a lobby", "See its lobbies"];

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(string more)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, """ +
            more + """, "discPath": """ + System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data);
    }

    [Fact]
    public async Task QuickPlayAsksWhichServerAndTheQuickestIsTheBestConnection()
    {
        var state = State($$"""
            "server": "{{Slow}}", "servers": ["{{Quick}}", "{{Slow}}"]
            """);
        using var answers = new Servers();
        await InWindow(state, answers, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Said(shell, "Quick Play"));
            var servers = Assert.IsType<ServersViewModel>(main.Current);
            var page = Page<ServersView>(window);
            Assert.Equal("CHOOSE A SERVER", Named<TextBlock>(page, "Title").Text);
            Assert.Equal(["Join", "Back"], Hints(main));
            // this build's official server, then the server set (the one last used), then the player's own; each
            // asked while the page shows
            var official = LauncherSettings.OnlineServer;
            Assert.Equal([.. new[] { official }.OfType<string>(), Slow, Quick], servers.Rows.Select(r => r.Address));
            var (slow, quick) = (servers.Rows[^2], servers.Rows[^1]);
            PumpTelling(state, () => servers.Rows.All(r => r.Ping.Length > 0), "the servers' pings did not come", 15);
            Assert.Equal(["Slow", "Quick"], new[] { slow, quick }.Select(r => r.Name));      // what each calls itself
            if (official is not null)
            {
                Assert.Equal(StackStamp.Current.ServerLabel, servers.Rows[0].Name);         // the official one by its label
                Assert.False(servers.Rows[0].Own);
            }
            Assert.Equal(["Last used", "Best connection"], new[] { slow, quick }.Select(r => r.Tag));
            Assert.Equal("1 online · 1 lobby", quick.Line);
            Assert.Equal("good", quick.Quality);
            Assert.Same(quick, servers.Best());
            // the pad lands on the server last used
            var landing = Assert.IsType<Button>(page.Landing());
            Assert.Same(slow, landing.DataContext);
            Shot(window, state, "next-servers");

            // a server of the player's own: a wrong address is said, a right one is added and kept
            Click(window, Said(page, "Add a server"));
            Assert.Equal(["Select", "Cancel"], Hints(main));
            Named<TextBox>(page, "Address").Text = "not a server";
            Click(window, Said(page, "Add this server"));
            Assert.Contains("no server address", servers.AddProblem, StringComparison.Ordinal);
            Named<TextBox>(page, "Address").Text = Quick;
            Click(window, Said(page, "Add this server"));
            Assert.Contains("on the list already", servers.AddProblem, StringComparison.Ordinal);
            Named<TextBox>(page, "Address").Text = Third;
            Click(window, Said(page, "Add this server"));
            Assert.False(servers.Adding);
            Assert.Equal([Quick, Slow, Third], state.Settings.Servers);
            Assert.Equal(Third, servers.Rows[^1].Address);
            Shot(window, state, "next-servers-added");
            // and off the list again
            var count = servers.Rows.Count;
            servers.RemoveCommand.Execute(servers.Rows[^1]);
            Assert.Equal([Quick, Slow], state.Settings.Servers);
            Assert.Equal(count - 1, servers.Rows.Count);

            // no button beside the rows (a row joins): neither "Best connection" nor a server's lobbies
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible
                && (AutomationProperties.GetName(b) is "Best connection" || (AutomationProperties.GetName(b) ?? "").StartsWith("Lobbies", StringComparison.Ordinal)));
            Click(window, Named<Button>(shell, "Back"));
            Assert.IsType<HubViewModel>(main.Current);
            // nobody is asked once the page is gone
            Assert.False(servers.Watching);

            // a server's lobbies, from its panel ("See its lobbies"; the count beside the profile is only shown): the
            // server set, its lobbies' page names it; Back goes back to the servers, and to the hub
            Click(window, Said(shell, "Quick Play"));
            Click(window, Said(Page<ServersView>(window), "Quick"));
            Click(window, Said(Page<ServerSheetView>(window), "See its lobbies"));
            Assert.Equal(Quick, state.Settings.Server);
            Assert.Same(main.Home, main.Current);
            var lobbies = Page<LobbiesView>(window);
            Assert.Equal("· QUICK", Named<TextBlock>(lobbies, "ServerName").Text);
            Assert.True(Said(lobbies, "Change server").IsEffectivelyVisible);
            Click(window, Named<Button>(shell, "Back"));
            Assert.IsType<ServersViewModel>(main.Current);
            Click(window, Named<Button>(shell, "Back"));
            Assert.IsType<HubViewModel>(main.Current);

            // a server pressed: its two ways onto it in a panel, nothing joined yet; Join a race sets it, and joins
            Click(window, Said(shell, "Quick Play"));
            state.Update(s => s with { Server = Slow });
            Click(window, Said(Page<ServersView>(window), "Quick"));
            Assert.IsType<ServerSheetViewModel>(main.Shell.Sheet);
            Assert.Equal(Slow, state.Settings.Server);
            var sheet = Page<ServerSheetView>(window);
            Assert.True(Said(sheet, "Host a lobby").IsEffectivelyVisible);
            Shot(window, state, "next-server-sheet");
            Click(window, Said(sheet, "Join a race"));
            Assert.Equal(Quick, state.Settings.Server);
        });
    }

    /// <summary>"Join a race" where no Quick Play lobby is open (the owner, 2026-11-15): the server's panel has its
    /// three ways alike, their titles alone; the press says "No race found" with OK alone, and joins nothing and makes nothing; RACE on the lobbies
    /// page alike, its line saying there is no race. "Host a lobby" is the way to a lobby then, a Quick Play one.
    /// (In dev mode, where the stand-in's own lobby needs no game, its status from the test.)</summary>
    [Fact]
    public async Task JoiningARaceWhereNoLobbyIsOpenSaysNoRaceFound()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        var state = AppState.Load(data, standIn: true);
        using var answers = new Servers { NoLobbies = true };
        await InWindow(state, answers, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Said(shell, "Quick Play"));
            var servers = Assert.IsType<ServersViewModel>(main.Current);
            var row = servers.Rows[0];
            PumpTelling(state, () => row.Ping.Length > 0, "the server gave no answer", 15);
            Click(window, Said(Page<ServersView>(window), row.Name));
            // the panel: its three ways, alike, their titles alone
            var sheet = Page<ServerSheetView>(window);
            var ways = Ways.Select(name => Said(sheet, name)).ToList();
            Assert.All(ways, w => Assert.Contains("way", w.Classes));
            Assert.All(ways, w => Assert.Single(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { Length: > 0 }));
            Click(window, Said(Page<ServerSheetView>(window), "Join a race"));
            PumpTelling(state, () => main.Shell.AskingConfirm, "joining found no lobby and said nothing", 15);
            Assert.Equal((JoinCheck.NoRaceTitle, "OK"), (main.Shell.ConfirmTitle, main.Shell.KeepWord));
            Assert.False(Named<Button>(shell, "ConfirmDo").IsEffectivelyVisible);   // (OK alone: no way to a custom lobby)
            Assert.Null(state.Session);
            Assert.False(state.Joining);
            Click(window, Said(shell, "OK"));
            Assert.False(main.Shell.AskingConfirm);
            Assert.Null(state.Session);

            // RACE on the lobbies page: its line says there is no race, and a press is the same answer
            Click(window, Said(Page<ServerSheetView>(window), "See its lobbies"));
            Assert.Same(main.Home, main.Current);
            PumpTelling(state, () => main.Control.Line1 == PlayControlViewModel.NoOpenLobbyText, "RACE's line did not say there is no race", 15);
            Click(window, Named<Button>(Page<LobbiesView>(window), "Race"));
            PumpTelling(state, () => main.Shell.AskingConfirm, "RACE found no lobby and said nothing", 15);
            Assert.Equal(JoinCheck.NoRaceTitle, main.Shell.ConfirmTitle);
            Click(window, Said(shell, "OK"));
            Assert.Null(state.Session);
            // and Host a lobby there: a Quick Play lobby
            Click(window, Said(Page<LobbiesView>(window), "Host a lobby"));
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Lobby.Phase == LobbyPhase.Gather, "Host a lobby joined no Quick Play lobby", 15);
            var session = Assert.IsType<QuickPlaySession>(state.Session);
            Assert.False(session.Custom);
            Assert.False(session.Lobby?.Custom ?? true);
        });
    }

    [Fact]
    public async Task TheHubSaysWhoRacesAndTheMessageOfTheDayHidesForToday()
    {
        var state = State($$"""
            "server": "{{Quick}}"
            """);
        using var answers = new Servers { Motd = new RecentNotice("m-7", Notices.Notice, "Friday is Death Race night.", DateTimeOffset.Now) };
        await InWindow(state, answers, (window, main) =>
        {
            var hub = Assert.IsType<HubViewModel>(main.Current);
            PumpTelling(state, () => hub.MotdShown && hub.PlayersOnline >= 0, "the hub showed no message or count", 15);
            // how many are online: the top bar's count beside the profile, its tip with who races; no row over the
            // hub's entries any more
            Assert.Equal(1, hub.PlayersOnline);
            Assert.Equal("1 player online on Quick · 3 racing now.", hub.OnlineTip);
            var shell = Shell(window);
            var view = Page<HubView>(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1", Named<TextBlock>(Named<Border>(shell, "Online"), "Figures").Text);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "All lobbies");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "RacingNow");
            // the message's panel: the whole of it, and "Hide for today"
            Click(window, Said(view, "Message of the day"));
            var sheet = Assert.IsType<MotdSheetViewModel>(main.Shell.Sheet);
            Assert.Same(hub, sheet.Hub);
            var panel = Page<MotdSheetView>(window);
            Assert.Equal("Friday is Death Race night.", Named<TextBlock>(panel, "Text").Text);
            Assert.StartsWith("Today ", Named<TextBlock>(panel, "When").Text, StringComparison.Ordinal);
            Shot(window, state, "next-hub-motd");
            Click(window, Said(panel, "Hide for today"));
            Assert.Null(main.Shell.Sheet);
            Assert.False(hub.MotdShown);
            Assert.False(Named<Button>(view, "Motd").IsEffectivelyVisible);
            Assert.Equal(LauncherSettings.MotdHiddenFor("m-7", DateOnly.FromDateTime(DateTime.Now)), state.Settings.MotdHidden);
            // another message shows again
            answers.Motd = new RecentNotice("m-8", Notices.Notice, "Cup night", DateTimeOffset.Now);
            PumpTelling(state, () => hub.MotdShown, "the next message did not show", 15);

            // "End game" shows only while a game runs, and asks first; keeping it open is the green answer
            Assert.False(Named<Button>(view, "EndGame").IsEffectivelyVisible);
            main.Shell.AskEndGameCommand.Execute(null);
            Assert.False(main.Shell.AskingConfirm);                  // (no game: nothing to ask)
            main.Shell.GameRunning = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<Button>(view, "EndGame").IsEffectivelyVisible);
            Click(window, Named<Button>(view, "EndGame"));
            Assert.True(main.Shell.AskingConfirm);
            Assert.True(Named<Panel>(shell, "ConfirmQuestion").IsEffectivelyVisible);
            Assert.Equal(("End the game?", "Keep it open"), (main.Shell.ConfirmTitle, main.Shell.KeepWord));
            Assert.Equal(["Select", "Cancel"], Hints(main));
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.IsType<HubViewModel>(main.Current);
        });
    }

    [Fact]
    public async Task TheLobbyCallsAPlayerBackFromTheirPractice()
    {
        var stops = 0;
        var recall = new LobbyRecall(TimeSpan.FromMilliseconds(60), () => Interlocked.Increment(ref stops));
        var recalled = 0;
        recall.Recalled += () => recalled++;
        var gathering = new LobbyState("l1", [], 30, 2, false, 0, 0);
        var voting = gathering with { Stage = LobbyState.Starting };
        var now = DateTimeOffset.UtcNow;

        // nobody practises: nothing to call back
        Assert.False(recall.Heard(voting, now));
        recall.Started();
        Assert.False(recall.Heard(gathering, now));                 // the lobby still gathers
        Assert.Null(recall.At);
        Assert.True(recall.Heard(voting, now));                     // it moved on: the call back begins, once
        Assert.Equal(now + recall.Warning, recall.At);
        Assert.False(recall.Heard(voting, now));
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.Equal(1, stops);                                     // the practice game was ended after the warning
        Assert.True(recall.Ended());
        Assert.Equal(1, recalled);
        Assert.Null(recall.At);

        // the player left the lobby while warned: the practice goes on
        recall.Started();
        Assert.True(recall.Heard(voting, now));
        recall.Left();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(1, stops);
        Assert.False(recall.Ended());

        // "Back now"; and a practice that ended by itself was no recall
        recall.Started();
        recall.Now();
        Assert.Equal(2, stops);
        Assert.False(recall.Ended());
        Assert.Equal(1, recalled);

        // with the lobby's start countdown: the call back begins its last seconds and ends the practice as it runs out
        var counted = new LobbyRecall(TimeSpan.FromMilliseconds(150), () => Interlocked.Increment(ref stops));
        var began = 0;
        counted.Began += () => Interlocked.Increment(ref began);
        counted.Started();
        now = DateTimeOffset.UtcNow;
        Assert.True(counted.Heard(gathering, now, now.AddMilliseconds(100)));   // less than the warning left: at once
        Assert.Equal(now.AddMilliseconds(100), counted.At);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.Equal(3, stops);
        counted.Ended();
        // a countdown further away: nothing yet, then its last seconds by themselves
        counted.Started();
        now = DateTimeOffset.UtcNow;
        Assert.False(counted.Heard(gathering, now, now.AddMilliseconds(400)));
        Assert.Null(counted.At);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        Assert.Equal(2, began);
        Assert.Equal(4, stops);

        // a practice starts out of Quick Play, in its queue, or while the lobby gathers; never once it moved on
        Assert.True(LobbyRecall.MayPractise(false, voting));
        Assert.True(LobbyRecall.MayPractise(true, null));
        Assert.True(LobbyRecall.MayPractise(true, gathering));
        Assert.False(LobbyRecall.MayPractise(true, voting));
        Assert.Equal("The lobby starts in 8 s. Your practice ends and you are brought back.", LobbyRecall.Line(8));
        Assert.Equal("Your lobby is starting: back to it in 10 seconds.", new LobbyRecall(LobbyRecall.Standard, () => { }).GameLine);
    }

    [Fact]
    public void TheSettingsKeepThePlayersServersAndTheHiddenMessage()
    {
        var read = new LauncherSettings
        {
            Servers = [Quick, "nonsense", " " + Slow + " ", Quick, .. Enumerable.Range(10, 20).Select(i => $"ws://127.0.0.{i}:9/ws")],
            MotdHidden = "m-7@2026-11-14",
        };
        var (sound, repaired) = SettingsCheck.Sound(read);
        Assert.True(repaired);
        Assert.Equal(LauncherSettings.MostServers, sound.Servers!.Count);
        Assert.Equal([Quick, Slow], sound.Servers.Take(2));
        Assert.True(sound.MotdHiddenOn("m-7", new DateOnly(2026, 11, 14)));
        Assert.False(sound.MotdHiddenOn("m-7", new DateOnly(2026, 11, 15)));
        Assert.False(sound.MotdHiddenOn("m-8", new DateOnly(2026, 11, 14)));
        // a sound list stays the same list; none is left out of the file
        var kept = new LauncherSettings { Servers = [Quick] };
        Assert.Same(kept.Servers, SettingsCheck.Sound(kept).Settings.Servers);
        var file = System.Text.Json.JsonSerializer.Serialize(new LauncherSettings(), Web);
        Assert.DoesNotContain("servers", file, StringComparison.Ordinal);
        Assert.DoesNotContain("motdHidden", file, StringComparison.Ordinal);
        Assert.Contains("\"servers\"", System.Text.Json.JsonSerializer.Serialize(kept, Web), StringComparison.Ordinal);
        // the hub's moments
        var now = new DateTimeOffset(2026, 11, 14, 20, 0, 0, TimeSpan.Zero).ToLocalTime();
        Assert.StartsWith("Today ", HubViewModel.When(now.AddHours(-1), now), StringComparison.Ordinal);
        Assert.StartsWith("Yesterday ", HubViewModel.When(now.AddDays(-1), now), StringComparison.Ordinal);
    }

    /// <summary>Answers each server's status by its host: 127.0.0.1 is "Quick" (at once), the others are "Slow" (after
    /// a while); a lobby, a race of three, and the message of the day the test sets.</summary>
    private sealed class Servers : HttpMessageHandler
    {
        public volatile RecentNotice? Motd;

        /// <summary>The servers have no lobby: nobody is in one.</summary>
        public volatile bool NoLobbies;

        private static readonly Online.Protocol.RacerView[] Racers = [new("ANA", 1, "#E8432E"), new("BEN", 2, "#2E64E8"), new("CID", 3, "#2EE85A")];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var quick = request.RequestUri?.Host == "127.0.0.1";
            await Task.Delay(quick ? 5 : 150, cancellationToken).ConfigureAwait(false);
            var status = new ServiceStatus(quick ? "Quick" : "Slow", DateTimeOffset.UtcNow, 1,
                NoLobbies ? [] : [new Online.Protocol.LobbyView("l1", Racers[..1], 30, false, 0)], [],
                [new Online.Protocol.RaceView("r1", "relay", "eu", 1, "Mute City: Twist Road", 3, Racers, 0, DateTimeOffset.UtcNow)], [], Motd: Motd);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(status, ControlProtocol.Json)),
            };
        }
    }
}
