using System.Net;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>When the settings cannot be read, the launcher has no server, and says so; it
/// never puts the official service's address in the place of the one it could not read. So nothing is asked of
/// anyone (the status feed, the connection dot) and nothing is joined until the player chooses a server or puts a
/// readable settings file back. A first start, with no settings file at all, is the defaults: the official
/// server.</summary>
public sealed class NoServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsFile => Path.Combine(_dir, "settings.json");

    private SettingsStore Stored(string text)
    {
        File.WriteAllText(SettingsFile, text);
        return new SettingsStore(SettingsFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"server\": \"ws://192.168.1.20:47810/ws\", \"racer\": 5}")]      // the player's own server, in a file that is no settings
    [InlineData("{\"server\": 7}")]
    public void SettingsThatCannotBeReadGiveNoServerFromStartToStart(string text)
    {
        var store = Stored(text);

        var loaded = store.Load();

        Assert.Equal(SettingsRead.Unreadable, store.LastRead);
        Assert.True(loaded.NoServer);
        Assert.Null(loaded.QuickPlayServer);
        Assert.Contains("No server", store.Notice, StringComparison.Ordinal);
        Assert.EndsWith(SettingsStore.ChooseServer, store.Notice, StringComparison.Ordinal);
        // the launcher writes its own file over it (AppState.Load): the next start reads that, and still has none
        store.Save(loaded);
        var next = new SettingsStore(SettingsFile);
        Assert.Null(next.Load().QuickPlayServer);
        Assert.Equal(SettingsRead.Fine, next.LastRead);
    }

    [Theory]
    [InlineData("http://example.com/ws")]
    [InlineData("192.168.1.20")]
    [InlineData("ws://")]
    [InlineData("wss://piconet-coordinator.fly.dev/ws\u0007")]
    public void AServersAddressThatCannotBeReadGivesNoServerAndTheRestStays(string server)
    {
        var store = Stored(System.Text.Json.JsonSerializer.Serialize(new { server, discPath = "C:/games/gx.iso" }));

        var loaded = store.Load();

        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        Assert.Null(loaded.Server);
        Assert.True(loaded.NoServer);
        Assert.Null(loaded.QuickPlayServer);
        Assert.Equal("C:/games/gx.iso", loaded.DiscPath);
        Assert.Equal(SettingsStore.RepairedNotice + " · " + SettingsStore.ServerLostNotice, store.Notice);
        // sound now: said once, and there is still no server at the next start
        store.Save(loaded);
        var next = new SettingsStore(SettingsFile);
        Assert.Null(next.Load().QuickPlayServer);
        Assert.Null(next.Notice);
    }

    [Fact]
    public void AFirstStartAndABlankServerAreTheOfficialServerAsBefore()
    {
        var store = new SettingsStore(SettingsFile);
        var first = store.Load();                                              // no file yet
        Assert.False(first.NoServer);
        Assert.Equal(LauncherSettings.OnlineServer, first.QuickPlayServer);
        Assert.Null(store.Notice);
        store.Save(first);
        Assert.DoesNotContain("noServer", File.ReadAllText(SettingsFile), StringComparison.Ordinal);   // written only when set

        Assert.Equal(LauncherSettings.OnlineServer, Stored("{\"server\": \"  \"}").Load().QuickPlayServer);
        Assert.Equal(LauncherSettings.OnlineServer, Stored("{\"server\": null}").Load().QuickPlayServer);
        Assert.Equal("ws://192.168.1.20:47810/ws", Stored("{\"server\": \"ws://192.168.1.20:47810/ws\"}").Load().QuickPlayServer);
    }

    [Fact]
    public void TheServerThePlayerThenChoosesIsKept()
    {
        var store = Stored("{ not json");
        var loaded = store.Load();
        Assert.Null(loaded.QuickPlayServer);

        // the Settings page's button: the official server
        store.Save(loaded with { Server = null, NoServer = false });
        Assert.Equal(LauncherSettings.OnlineServer, new SettingsStore(SettingsFile).Load().QuickPlayServer);
        // or an address typed there
        store.Save(loaded with { Server = "ws://192.168.1.20:47810/ws", NoServer = false });
        Assert.Equal("ws://192.168.1.20:47810/ws", new SettingsStore(SettingsFile).Load().QuickPlayServer);
        // a file that says "no server" beside an address has none (the page clears the one with the other)
        Assert.Null(Stored("{\"server\": \"ws://192.168.1.20:47810/ws\", \"noServer\": true}").Load().QuickPlayServer);
    }

    /// <summary>What the launcher starts by itself when its window opens (the status feed, every five seconds; the
    /// connection dot, every thirty): with no server, neither sends anything, and neither calls that a failure.</summary>
    [Fact]
    public async Task WithNoServerNobodyIsAskedUntilThePlayerChoosesOne()
    {
        var cancel = TestContext.Current.CancellationToken;
        var settings = Stored("{ not json").Load();
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var feed = new ServerStatusFeed(() => settings.QuickPlayServer, answers) { Watching = true };
        using var health = new ServerHealth(() => settings.QuickPlayServer, () => null, answers);
        var changes = 0;
        feed.Changed += () => Interlocked.Increment(ref changes);
        Assert.Equal(Connectivity.NoServer, health.State);

        // as the window does: both started, each asking at once and then on its timer
        feed.Start();
        health.Start();
        await feed.AskAsync(cancel);
        await feed.AskAsync(cancel);
        await health.CheckAsync(cancel);
        health.Refresh();
        await Task.Delay(300, cancel);

        Assert.Empty(answers.Asked);
        Assert.True(feed.NoServer);
        Assert.False(feed.Failed);
        Assert.Null(feed.Latest);
        Assert.Equal(1, changes);                                              // said once, not every five seconds
        Assert.Equal(Connectivity.NoServer, health.State);
        Assert.Null(health.Server);
        Assert.Null(health.LastCheck);
        Assert.False(health.InQuickPlay);

        // the player chooses a server: from then on it is asked, and only it
        settings = settings with { Server = "ws://coordinator.test:47810/ws", NoServer = false };
        await feed.AskAsync(cancel);
        health.Refresh();
        await health.CheckAsync(cancel);

        Assert.False(feed.NoServer);
        Assert.NotEmpty(answers.Asked);
        Assert.All(answers.Asked, uri => Assert.Equal("coordinator.test", uri.Host));
        Assert.Equal(Connectivity.Online, health.State);
    }

    /// <summary>The Settings page's Server box (<see cref="ServerChoice"/>): the review's sequence. With settings
    /// that could not be read, an address typed and cleared again made the official server the player's server,
    /// asked at once and joined at the next start, the button never pressed; and every prefix of the address that is
    /// an address was asked while it was typed. Now nothing is set, and so nobody is asked, until the player
    /// commits an address; a blank box is no server.</summary>
    [Fact]
    public async Task TypingInTheServerBoxAsksNobodyAndClearingItIsNotTheOfficialServer()
    {
        var cancel = TestContext.Current.CancellationToken;
        const string address = "ws://192.168.1.20:47810/ws";
        var store = Stored("{ not json");
        var settings = store.Load();
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var feed = new ServerStatusFeed(() => settings.QuickPlayServer, answers);
        using var health = new ServerHealth(() => settings.QuickPlayServer, () => null, answers);
        // what the window does whenever the settings may have changed
        async Task Look()
        {
            health.Refresh();
            await health.CheckAsync(cancel);
            await feed.AskAsync(cancel);
        }

        // typed letter by letter, then deleted letter by letter: prefixes such as ws://192.168.1.2 and :4, :47, :478
        var typed = Enumerable.Range(1, address.Length).Select(n => address[..n]).ToList();
        foreach (var text in typed.Concat(typed.AsEnumerable().Reverse().Skip(1)).Append(""))
        {
            settings = ServerChoice.After(settings, text, ServerAct.Typing);
            await Look();
        }
        Assert.Empty(answers.Asked);
        Assert.Null(settings.QuickPlayServer);
        // the blank box left: still no server, now and at the next start
        settings = ServerChoice.After(settings, "", ServerAct.Commit);
        await Look();
        store.Save(settings);
        Assert.Null(new SettingsStore(SettingsFile).Load().QuickPlayServer);
        Assert.Empty(answers.Asked);

        // the whole address, committed: that server and no other
        settings = ServerChoice.After(settings, address, ServerAct.Commit);
        await Look();
        Assert.Equal(address, settings.QuickPlayServer);
        Assert.NotEmpty(answers.Asked);
        Assert.All(answers.Asked, uri => Assert.Equal("192.168.1.20:47810", uri.Authority));
        var asked = answers.Asked.Count;

        // a player on a server of their own who clears the box has no server, and nobody is asked
        foreach (var text in typed.AsEnumerable().Reverse().Skip(1).Append(""))
        {
            settings = ServerChoice.After(settings, text, ServerAct.Typing);
        }
        Assert.Equal(address, settings.QuickPlayServer);                      // nothing changes while they type
        settings = ServerChoice.After(settings, "", ServerAct.Commit);
        await Look();
        Assert.True(settings.NoServer);
        Assert.Null(settings.QuickPlayServer);
        Assert.Equal(asked, answers.Asked.Count);
        store.Save(settings);
        Assert.Null(new SettingsStore(SettingsFile).Load().QuickPlayServer);

        // the official server is the button's
        settings = ServerChoice.After(settings, "", ServerAct.Official);
        Assert.Equal(LauncherSettings.OnlineServer, settings.QuickPlayServer);
    }

    [Fact]
    public void AServerIsSetOnlyByAWholeAddressCommittedOrTheButton()
    {
        var official = new LauncherSettings();
        var own = new LauncherSettings { Server = "ws://10.0.0.5:47810/ws" };
        var none = new LauncherSettings { NoServer = true };
        foreach (var settings in new[] { official, own, none })
        {
            // typing sets nothing, whatever is typed
            Assert.Same(settings, ServerChoice.After(settings, "ws://192.168.1.20:47810/ws", ServerAct.Typing));
            Assert.Same(settings, ServerChoice.After(settings, "", ServerAct.Typing));
            // what is no address sets nothing when committed either
            Assert.Same(settings, ServerChoice.After(settings, "my-server", ServerAct.Commit));
            Assert.Same(settings, ServerChoice.After(settings, "http://192.168.1.20/ws", ServerAct.Commit));
            Assert.Same(settings, ServerChoice.After(settings, "ws://" + new string('h', SettingsCheck.MostServer) + "/ws", ServerAct.Commit));
            // an address does, trimmed
            var set = ServerChoice.After(settings, "  ws://192.168.1.20:47810/ws ", ServerAct.Commit);
            Assert.Equal(("ws://192.168.1.20:47810/ws", false), (set.Server, set.NoServer));
            Assert.Equal(LauncherSettings.OnlineServer, ServerChoice.After(settings, "", ServerAct.Official).QuickPlayServer);
        }
        // a blank box: the official server for a player on it, none for the others
        Assert.Same(official, ServerChoice.After(official, "  ", ServerAct.Commit));
        Assert.Same(none, ServerChoice.After(none, null, ServerAct.Commit));
        Assert.Equal(none, ServerChoice.After(own, "", ServerAct.Commit));
        // the line under the box: an address that is not the one set
        Assert.True(ServerChoice.Waiting(official, "ws://192.168.1.20:47810/ws"));
        Assert.True(ServerChoice.Waiting(none, "ws://192.168.1.20:47810/ws"));
        Assert.False(ServerChoice.Waiting(own, " ws://10.0.0.5:47810/ws"));
        Assert.True(ServerChoice.Waiting(own, "ws://10"));                    // a prefix is an address too: of another computer
        Assert.False(ServerChoice.Waiting(own, "ws://"));
        Assert.True(ServerChoice.Waiting(own, ""));                           // a blank that would mean no server
        Assert.False(ServerChoice.Waiting(official, ""));
        Assert.False(ServerChoice.Waiting(none, " "));
    }

    /// <summary>The second review's two cases. The box's placeholder said "Official server" over a cleared box on a
    /// server of the player's own, where Enter gives no server: what a blank box means is asked of the rule. And a
    /// click on "Use the official server" leaves the box first: leaving used to set the typed address, which was
    /// asked once before the official one. Leaving the box sets nothing now, so the button's choice is the only one
    /// made, and the only server asked.</summary>
    [Fact]
    public async Task ABlankBoxSaysWhatItMeansAndTheOfficialButtonWinsOverWhatIsTyped()
    {
        var cancel = TestContext.Current.CancellationToken;
        var official = new LauncherSettings();
        var own = new LauncherSettings { Server = "ws://10.0.0.5:47810/ws" };
        var none = new LauncherSettings { NoServer = true };
        // the placeholder: what a blank box, committed, would be
        Assert.True(ServerChoice.BlankIsOfficial(official));
        Assert.False(ServerChoice.BlankIsOfficial(own));
        Assert.False(ServerChoice.BlankIsOfficial(none));
        Assert.True(ServerChoice.BlankIsOfficial(ServerChoice.After(none, "anything typed", ServerAct.Official)));
        // leaving the box is no choice, whatever it holds
        foreach (var settings in new[] { official, own, none })
        {
            Assert.Same(settings, ServerChoice.After(settings, "ws://192.168.1.20:47810/ws", ServerAct.Leaving));
            Assert.Same(settings, ServerChoice.After(settings, "", ServerAct.Leaving));
        }

        // the click on the button as the page meets it, with a request counter: an address typed, the box left
        // (the click), the button
        var current = Stored("{ not json").Load();
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });   // (no request leaves the test)
        using var feed = new ServerStatusFeed(() => current.QuickPlayServer, answers);
        using var health = new ServerHealth(() => current.QuickPlayServer, () => null, answers);
        async Task Did(string typed, ServerAct act)
        {
            current = ServerChoice.After(current, typed, act);
            health.Refresh();
            await health.CheckAsync(cancel);
            await feed.AskAsync(cancel);
        }
        const string typed = "ws://192.168.1.20:47810/ws";
        await Did(typed, ServerAct.Typing);
        await Did(typed, ServerAct.Leaving);
        Assert.Empty(answers.Asked);
        await Did(typed, ServerAct.Official);

        Assert.Equal(LauncherSettings.OnlineServer, current.QuickPlayServer);
        Assert.Null(current.Server);                                           // the box is emptied: blank stands for it
        Assert.NotEmpty(answers.Asked);
        Assert.All(answers.Asked, uri => Assert.Equal("piconet-coordinator.fly.dev", uri.Host));
    }

    /// <summary>A first start: no settings file, so the official server is set, and the window started asking it
    /// for its status and its health while the choose-your-disc screen was still showing, the player having done
    /// nothing. Nobody is asked until that screen is passed; from then on as before.</summary>
    [Fact]
    public async Task AFirstStartAsksNobodyUntilTheDiscScreenIsPassed()
    {
        var cancel = TestContext.Current.CancellationToken;
        var store = new SettingsStore(SettingsFile);
        var settings = store.Load();                                           // no file
        Assert.Equal(LauncherSettings.OnlineServer, settings.QuickPlayServer);
        var past = false;
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });   // (no request leaves the test)
        using var feed = new ServerStatusFeed(() => ServerChoice.AskedUnprompted(settings, past), answers) { Watching = true };
        using var health = new ServerHealth(() => ServerChoice.AskedUnprompted(settings, past), () => null, answers);

        // as the window does while its first screen shows: both started, the settings saved once (AppState.Load)
        feed.Start();
        health.Start();
        store.Save(settings);
        health.Refresh();
        await health.CheckAsync(cancel);
        await feed.AskAsync(cancel);
        await Task.Delay(300, cancel);
        Assert.Empty(answers.Asked);

        // the disc accepted: the launcher's pages show, and the server that is set is asked at once
        settings = settings with { DiscPath = "C:/games/gx.iso" };
        past = true;
        health.Refresh();
        await health.CheckAsync(cancel);
        await feed.AskAsync(cancel);
        Assert.Contains(new Uri("https://piconet-coordinator.fly.dev/health"), answers.Asked);
        Assert.Contains(new Uri("https://piconet-coordinator.fly.dev/api/status"), answers.Asked);
        Assert.Equal(Connectivity.Online, health.State);
        // and with no server set there is nobody to ask after it either
        Assert.Null(ServerChoice.AskedUnprompted(new LauncherSettings { NoServer = true }, pastFirstScreen: true));
    }

    /// <summary>A player on a server of their own whose settings then become unreadable: the feed and the dot stop
    /// asking that server too, and do not move to another.</summary>
    [Fact]
    public async Task AServerThatIsNoLongerSetIsNoLongerAsked()
    {
        var cancel = TestContext.Current.CancellationToken;
        var settings = new LauncherSettings { Server = "ws://coordinator.test:47810/ws" };
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var feed = new ServerStatusFeed(() => settings.QuickPlayServer, answers);
        using var health = new ServerHealth(() => settings.QuickPlayServer, () => null, answers);
        await health.CheckAsync(cancel);
        Assert.Equal(Connectivity.Online, health.State);
        var asked = answers.Asked.Count;

        settings = settings with { Server = null, NoServer = true };
        health.Refresh();
        await health.CheckAsync(cancel);
        await feed.AskAsync(cancel);

        Assert.Equal(asked, answers.Asked.Count);
        Assert.Equal(Connectivity.NoServer, health.State);
        Assert.True(feed.NoServer);
    }

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Asked)
            {
                Asked.Add(request.RequestUri!);
            }
            return Task.FromResult(answer(request));
        }
    }
}
