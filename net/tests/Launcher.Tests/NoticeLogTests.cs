using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A server's notices in the launcher's core: what a notice's message must be to be taken (its tier bounded, one it
/// does not know read as info; the last hour's list bounded), the server known by its id and never its name, the
/// tier's word taken off the text, the notices kept in the data folder (across a restart, the last 100, once each by
/// id, a damaged file read as none), and a session that comes after a restart: the last hour's notices are no chat
/// lines, and a live one is a chat line once.
/// </summary>
public sealed class NoticeLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File => Path.Combine(_dir, "notices.json");

    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static ServerNotice Notice(int i, NoticeLevel level = NoticeLevel.Info) => new($"n-{i}", level, $"text {i}", Noon.AddMinutes(i));

    /// <summary>The clock the log is read by: the afternoon of the notices' day (they age by it).</summary>
    private static readonly TimeProvider Afternoon = new At(Noon.AddHours(4));

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void NoticesAreKeptAcrossARestartOnceEachAndTheOldestGoPastAHundred()
    {
        var log = NoticeLog.Load(File, Afternoon);
        Assert.Empty(log.Notices);
        Assert.False(log.Unseen);
        Assert.True(log.Add(Notice(0, NoticeLevel.Urgent)));
        Assert.False(log.Add(Notice(0, NoticeLevel.Urgent) with { Text = "again" }));   // the same id: kept once
        Assert.True(log.Unseen);

        var again = NoticeLog.Load(File, Afternoon);                                              // the launcher started again
        var kept = Assert.Single(again.Notices);
        Assert.Equal(("n-0", NoticeLevel.Urgent, "text 0", Noon), (kept.Id, kept.Level, kept.Text, kept.Time));
        Assert.True(again.Unseen);
        again.Seen();
        Assert.False(NoticeLog.Load(File, Afternoon).Unseen);

        for (var i = 1; i <= 100; i++)
        {
            again.Add(Notice(i));
        }
        var full = NoticeLog.Load(File, Afternoon);
        Assert.Equal(NoticeLog.Most, full.Notices.Count);
        Assert.Equal("n-1", full.Notices[0].Id);                                       // n-0, the oldest, has gone
        Assert.Equal("n-100", full.Notices[^1].Id);
    }

    [Fact]
    public void ADismissedBannerStaysDismissedAcrossARestart()
    {
        var log = NoticeLog.Load(File, Afternoon);
        log.Add(Notice(1, NoticeLevel.Warning));
        log.Dismiss("n-1");
        log.Dismiss("not kept");                                                     // only a notice kept is marked
        var again = NoticeLog.Load(File, Afternoon);
        Assert.True(again.Dismissed("n-1"));
        Assert.False(again.Dismissed("not kept"));
        Assert.False(again.Dismissed(null));
        for (var i = 2; i <= 101; i++)
        {
            again.Add(Notice(i));
        }
        Assert.False(NoticeLog.Load(File, Afternoon).Dismissed("n-1"));                        // gone with its notice
    }

    [Fact]
    public void ANoticeOlderThanTwoWeeksGoesAtTheNextStart()
    {
        var log = NoticeLog.Load(File, Afternoon);
        log.Add(Notice(0, NoticeLevel.Warning));
        log.Dismiss("n-0");
        log.Add(new ServerNotice("late", NoticeLevel.Info, "a day later", Noon.AddDays(1)));
        Assert.Equal(2, NoticeLog.Load(File, new At(Noon + NoticeLog.KeptFor - TimeSpan.FromMinutes(1))).Notices.Count);
        var later = NoticeLog.Load(File, new At(Noon + NoticeLog.KeptFor + TimeSpan.FromHours(1)));
        Assert.Equal(["late"], later.Notices.Select(n => n.Id!));
        Assert.False(later.Dismissed("n-0"));
    }

    [Fact]
    public void AStandingNoticeTakesThePlaceOfTheOneBeforeWithoutTheDot()
    {
        var log = NoticeLog.Load(File, Afternoon);
        log.Add(Notice(1));
        log.Seen();
        Assert.True(log.AddStanding(Notice(2, NoticeLevel.Notice)));
        Assert.False(log.AddStanding(Notice(2, NoticeLevel.Notice)));                  // the same: nothing changes
        Assert.False(log.Unseen);
        var again = NoticeLog.Load(File, Afternoon);                                   // kept as standing across a restart
        Assert.Equal("n-2", again.Standing);
        Assert.True(again.AddStanding(Notice(3, NoticeLevel.Notice)));
        Assert.Equal(["n-1", "n-3"], again.Notices.Select(n => n.Id!));                // n-2 no longer stands: gone
        // the same notice came at a welcome first (a chat line, kept as any): it becomes the standing one, once
        Assert.True(again.Add(Notice(4, NoticeLevel.Notice)));
        Assert.True(again.AddStanding(Notice(4, NoticeLevel.Notice)));
        Assert.Equal(["n-1", "n-4"], again.Notices.Select(n => n.Id!));
        Assert.False(again.AddStanding(new ServerNotice(null, NoticeLevel.Notice, "no id", Noon)));
    }

    [Fact]
    public void TheStatusMessageOfTheDayIsCheckedAsANotice()
    {
        static RecentNotice? Read(RecentNotice? motd) =>
            ServerMessages.Checked(new ServiceStatus("s", Noon, 0, [], [], [], [], Motd: motd))!.Motd;
        Assert.Equal(new RecentNotice("a-1", Notices.Notice, "Cup night", Noon), Read(new("a-1", Notices.Notice, "Cup\a night", Noon)));
        Assert.Equal(Notices.Info, Read(new("a-1", "doom", "x", Noon))!.Level);
        Assert.Null(Read(new(null!, Notices.Notice, "no id", Noon)));
        Assert.Null(Read(new("a-1", Notices.Notice, "\a", Noon)));
        Assert.Null(Read(null));
        var older = System.Text.Json.JsonSerializer.Deserialize<ServiceStatus>(
            """{"name":"s","time":"2026-10-14T12:00:00Z","playersOnline":0,"lobbies":[],"starting":[],"races":[],"relays":[]}""",
            ControlProtocol.Json)!;
        Assert.Null(ServerMessages.Checked(older)!.Motd);                              // a server from before: none
    }

    [Theory]
    [InlineData("not JSON at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"unseen": true, "notices": 5}""")]
    [InlineData("null")]
    [InlineData("""{"unseen": true, "notices": [], "dismissed": 7}""")]
    [InlineData("""{"unseen": true, "notices": [{"id": "x"}, {"time": "never", "text": "t"}]}""")]
    public void ADamagedFileIsReadAsNoNotices(string text)
    {
        System.IO.File.WriteAllText(File, text);
        var log = NoticeLog.Load(File, Afternoon);
        Assert.Empty(log.Notices);
        Assert.False(log.Unseen);
        Assert.True(log.Add(Notice(1)));                                              // and the launcher goes on
        Assert.Single(NoticeLog.Load(File, Afternoon).Notices);
    }

    [Fact]
    public void AnEntryThatIsNotSoundIsLeftOutAndAnUnknownTierIsInfo()
    {
        System.IO.File.WriteAllText(File, $$"""
            {"unseen": true, "notices": [
              {"id": "a", "time": "2026-10-14T12:00:00Z", "level": "warning", "text": "kept"},
              {"id": "b", "time": "2026-10-14T12:01:00Z", "level": "doom", "text": "an unknown tier"},
              {"id": "c", "time": "2026-10-14T12:02:00Z", "level": "urgent", "text": ""},
              {"id": "{{new string('d', 41)}}", "time": "2026-10-14T12:03:00Z", "text": "an id too long"},
              null
            ]}
            """);
        var log = NoticeLog.Load(File, Afternoon);
        Assert.Equal([("a", NoticeLevel.Warning), ("b", NoticeLevel.Info)], log.Notices.Select(n => (n.Id!, n.Level)));
        Assert.True(log.Unseen);
    }

    [Fact]
    public void ANoticeIsKnownByTheServersIdAndItsTierWordIsTakenOff()
    {
        var notice = ServerNotice.Of(new ChatMessage("global", Notices.ServerId, "SYSTEM", "URGENT: Restart now", Noon, "urgent", "x-1"))!;
        Assert.Equal(("x-1", NoticeLevel.Urgent, "Restart now", false), (notice.Id, notice.Level, notice.Text, notice.Replayed));
        Assert.Null(ServerNotice.Of(new ChatMessage("global", "p3", "SYSTEM", "URGENT: I am the server", Noon)));   // a name is nobody
        Assert.Null(ServerNotice.Of(new ChatMessage("lobby", Notices.ServerId, "SYSTEM", "x", Noon)));
        var unknown = Assert.IsType<ChatMessage>(ServerMessages.Checked(
            """{"type":"chat","channel":"global","fromId":"server","fromName":"SYSTEM","text":"DOOM: x","time":"2026-10-14T12:00:00Z","level":"doom"}"""));
        Assert.Equal(Notices.Info, unknown.Level);
        Assert.Equal((NoticeLevel.Info, "DOOM: x"), (ServerNotice.Of(unknown)!.Level, ServerNotice.Of(unknown)!.Text));
        var plain = Assert.IsType<ChatMessage>(ServerMessages.Checked(
            $$"""{"type":"chat","channel":"global","fromId":"p1","fromName":"ANA","text":"hi","time":"2026-10-14T12:00:00Z","noticeId":"{{new string('n', 41)}}"}"""));
        Assert.Null(plain.NoticeId);
        Assert.Null(plain.Level);
    }

    [Fact]
    public void TheLastHoursNoticesAreCheckedAsAnyServerMessage()
    {
        var many = string.Join(",", Enumerable.Range(0, 150).Select(i => $$"""{"id":"n{{i}}","level":"warning","text":"t","time":"2026-10-14T12:00:00Z"}"""));
        var read = Assert.IsType<RecentNotices>(ServerMessages.Checked($$"""{"type":"recentNotices","notices":[{{many}}]}"""));
        Assert.Equal(NoticeLog.Most, read.Notices.Count);
        var odd = Assert.IsType<RecentNotices>(ServerMessages.Checked(
            """{"type":"recentNotices","notices":[null,{"level":"urgent","text":"no id"},{"id":"k","level":"doom","text":"x"},{"id":"t"}]}"""));
        var only = Assert.Single(odd.Notices);
        Assert.Equal(("k", Notices.Info), (only.Id, only.Level));
        Assert.Null(ServerMessages.Checked("""{"type":"recentNotices"}"""));
    }

    /// <summary>A launcher restart must not fill the chat with notices.</summary>
    [Fact]
    public async Task ARestartedLaunchersChatHoldsNoReplayedNoticeAndALiveOneOnce()
    {
        var cancel = TestContext.Current.CancellationToken;
        const string live = """{"type":"chat","channel":"global","fromId":"server","fromName":"SYSTEM","text":"WARNING: live","time":"2026-10-14T12:30:00Z","level":"warning","noticeId":"r-3"}""";
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome,
            """{"type":"recentNotices","notices":[{"id":"r-1","level":"urgent","text":"earlier","time":"2026-10-14T12:00:00Z"},{"id":"r-2","level":"info","text":"also","time":"2026-10-14T12:10:00Z"}]}""",
            live, live,
            """{"type":"chat","channel":"global","fromId":"p2","fromName":"SYSTEM","text":"URGENT: fake","time":"2026-10-14T12:31:00Z"}""",
            """{"type":"chat","channel":"global","fromId":"p2","fromName":"BEA","text":"last","time":"2026-10-14T12:32:00Z"}""");
        var replayed = new List<ServerNotice>();
        var heard = new List<ServerNotice>();
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => { });
        session.NoticesReplayed += replayed.AddRange;
        session.NoticeReceived += heard.Add;
        await session.JoinAsync(server.Address, cancel: cancel);
        await Headless.Until(() => session.Chat.Any(c => c.Text == "last"), cancel);
        Assert.Equal(["WARNING: live", "URGENT: fake", "last"], session.Chat.Select(c => c.Text));
        Assert.Equal([("r-1", true), ("r-2", true)], replayed.Select(n => (n.Id!, n.Replayed)));
        Assert.Equal(["r-3"], heard.Select(n => n.Id!));
    }
}
