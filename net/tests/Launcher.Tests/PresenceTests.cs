using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>Discord's status: the launcher's state as its two lines (<see cref="PresenceWords"/>), and the connection
/// that sends them (<see cref="DiscordPresence"/>, against a double of the link: no Discord here).</summary>
public sealed class PresenceTests
{
    private static readonly PresenceBrand Brand = Launcher.App.Branding.Discord;

    private static LobbyState Lobby(int players, string stage = LobbyState.Forming, string? waiting = null, LobbyRace? race = null) =>
        new("L1", [.. Enumerable.Range(0, players).Select(i => new PlayerInfo($"p{i}", $"P{i}", 1, new Rgb(1, 2, 3)))],
            30, 2, true, 0, 0, stage, race, waiting);

    private static RaceSetup Setup(int seats, int laps = 3) =>
        new(1, laps, [.. Enumerable.Range(0, seats).Select(i => new SeatSetup(i, i == 0 ? "p0" : null, $"R{i}", 1, null))], 0, 0);

    private static RacerLive Racer(int seat, float distance, int lap = 1, string state = RacerLive.Racing, int totalMs = 60_000) =>
        new(seat, 0, lap, 0, totalMs, null, 0, 0, 100, 0, 0, state, 0, 0, 0, distance);

    /// <summary>The player (seat 0) fifth of thirty, on lap 2 of 3.</summary>
    private static RaceSnapshot Fifth(string state = RacerLive.Racing) =>
        new(600, 0, [Racer(0, 500, state: state), .. Enumerable.Range(1, 29).Select(i => Racer(i, i <= 4 ? 1000 - i : 100 - i))]);

    [Fact]
    public void EachStateOfTheLauncherIsTwoShortLines()
    {
        var go = DateTimeOffset.UnixEpoch.AddDays(20_000);
        Assert.Equal(new PresenceCard("In the menus"), PresenceWords.Of(new PresenceInput()));
        Assert.Equal(new PresenceCard("In a lobby", "14 / 30 · gathering"), PresenceWords.Of(new PresenceInput(Lobby: Lobby(14))));
        Assert.Equal("14 / 30 · voting",
            PresenceWords.Of(new PresenceInput(Lobby: Lobby(14, LobbyState.Starting), CourseVote: true)).State);
        Assert.Equal("14 / 30 · choosing machines", PresenceWords.Of(new PresenceInput(Lobby: Lobby(14, LobbyState.Choosing))).State);
        Assert.Equal("14 / 30 · starting",
            PresenceWords.Of(new PresenceInput(Lobby: Lobby(14, LobbyState.Choosing, LobbyState.WaitingForRelay))).State);

        var racing = PresenceWords.Of(new PresenceInput(GameRun.Race, Race: Setup(30), Live: Fifth(), Go: go));
        Assert.Equal(new PresenceCard("Mute City: Twist Road", "Lap 2 / 3 · 5th of 30", "race", "Racing", go), racing);
        Assert.Equal("Finished · 1st of 30",   // (the only one finished)
            PresenceWords.Of(new PresenceInput(GameRun.Race, Race: Setup(30), Live: Fifth(RacerLive.Finished), Go: go)).State);
        Assert.Equal(new PresenceCard("Mute City: Twist Road", "Starting", "race", "Racing"),
            PresenceWords.Of(new PresenceInput(GameRun.Race, Race: Setup(30))));

        var watching = new PresenceCard("Watching", "Mute City: Twist Road", "watch", "Watching");
        Assert.Equal(watching, PresenceWords.Of(new PresenceInput(GameRun.Race, Race: Setup(30), Live: Fifth(RacerLive.Out), Go: go)));
        Assert.Equal(watching, PresenceWords.Of(new PresenceInput(GameRun.Watch, Race: Setup(30))));
        var race = new LobbyRace("m1", 1, 3, Setup(30).Seats, go);
        Assert.Equal(watching, PresenceWords.Of(new PresenceInput(Lobby: Lobby(14, LobbyState.Racing, race: race))));

        Assert.Equal(new PresenceCard("Practising", "Mute City: Twist Road", "practice", "Practising"),
            PresenceWords.Of(new PresenceInput(GameRun.Practice, Course: 1)));
        Assert.Equal("Watching a replay", PresenceWords.Of(new PresenceInput(GameRun.Replay, Lobby: Lobby(3))).Details);
    }

    [Fact]
    public void WithoutAnApplicationIdNothingConnects()
    {
        var links = new Links();
        using var presence = new DiscordPresence(Brand with { ApplicationId = "" }, _ => { }, links.Connect, gap: TimeSpan.Zero);
        presence.SetOn(true);
        presence.Show(new PresenceCard("In the menus"));
        Assert.False(presence.Available);
        Assert.Empty(links.Made);
        Assert.Null(presence.Shown);
        Assert.False(DiscordPresence.IsApplicationId("0"));
        Assert.True(DiscordPresence.IsApplicationId(Launcher.App.Branding.DiscordApplicationId));
    }

    [Fact]
    public void OffClearsTheStatusAtOnceAndOnShowsItAgain()
    {
        var links = new Links();
        using var presence = new DiscordPresence(Brand, _ => { }, links.Connect, gap: TimeSpan.Zero);
        presence.Show(new PresenceCard("In the menus"));
        Assert.Empty(links.Made);                                   // off until the setting is read
        presence.SetOn(true);
        Assert.Equal(["In the menus"], links.Made.Single().Shown.Select(c => c.Details));

        presence.SetOn(false);
        Assert.True(links.Made.Single().Cleared);
        Assert.True(links.Made.Single().Disposed);
        Assert.Null(presence.Shown);
        presence.Show(new PresenceCard("In a lobby", "2 / 30 · gathering"));
        Assert.Single(links.Made);                                  // nothing while off

        presence.SetOn(true);
        Assert.Equal("In a lobby", links.Made[1].Shown.Single().Details);
    }

    [Fact]
    public void ALinkThatFailsNeverReachesThePlayer()
    {
        var links = new Links { Throw = true };
        var log = new List<string>();
        using var presence = new DiscordPresence(Brand, log.Add, links.Connect, gap: TimeSpan.Zero);
        presence.SetOn(true);
        presence.Show(new PresenceCard("In the menus"));
        presence.Show(new PresenceCard("In a lobby"));
        Assert.Null(presence.Shown);
        Assert.Single(log);                                         // said once, not at every card
        Assert.All(links.Made, l => Assert.True(l.Disposed));      // each dropped link closed

        links.Throw = false;                                        // Discord back: the next card goes through
        presence.Show(new PresenceCard("In the menus"));
        Assert.Equal("In the menus", presence.Shown?.Details);

        // the link's own word (Discord gone, then back) is a line each change, never an error
        links.Made[^1].Heard(PresenceLinkEvent.Lost);
        links.Made[^1].Heard(PresenceLinkEvent.Lost);
        links.Made[^1].Heard(PresenceLinkEvent.Ready);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void CardsGoAtMostOnceAGapAndTheLastOneWins()
    {
        var links = new Links();
        var time = new ManualTime();
        using var presence = new DiscordPresence(Brand, _ => { }, links.Connect, time, TimeSpan.FromSeconds(5));
        presence.SetOn(true);
        presence.Show(new PresenceCard("In the menus"));
        presence.Show(new PresenceCard("In a lobby", "1 / 30 · gathering"));
        presence.Show(new PresenceCard("In a lobby", "2 / 30 · gathering"));
        var link = links.Made.Single();
        Assert.Single(link.Shown);
        Assert.NotNull(time.Timer);

        time.Now += TimeSpan.FromSeconds(5);
        time.Fire();
        Assert.Equal(["In the menus", "In a lobby"], link.Shown.Select(c => c.Details));
        Assert.Equal("2 / 30 · gathering", link.Shown[^1].State);

        time.Now += TimeSpan.FromSeconds(10);
        presence.Show(new PresenceCard("In a lobby", "2 / 30 · gathering"));   // the same card: not sent again
        Assert.Equal(2, link.Shown.Count);
    }

    [Fact]
    public void TheSettingSwitchesTheLaunchersStatus()
    {
        var dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
        var links = new Links();
        var state = Launcher.App.AppState.Load(Path.Combine(dir, "data"));
        try
        {
            Assert.True(state.Settings.ShowOnDiscord);                         // on unless the player says
            state.StartPresence(Brand, links.Connect);
            Assert.Equal(new PresenceCard("In the menus"), state.PresenceShown);

            state.Update(s => s with { ShowOnDiscord = false });
            Assert.True(links.Made.Single().Cleared);
            Assert.Null(state.PresenceShown);
            Assert.False(Launcher.App.AppState.Load(Path.Combine(dir, "data")).Settings.ShowOnDiscord);   // kept
        }
        finally
        {
            state.Shutdown();
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class Links
    {
        public List<FakeLink> Made { get; } = [];

        public bool Throw { get; set; }

        public FakeLink Connect(PresenceBrand brand, Action<PresenceLinkEvent> heard)
        {
            var link = new FakeLink(this, heard);
            Made.Add(link);
            return link;
        }
    }

    private sealed class FakeLink(Links links, Action<PresenceLinkEvent> heard) : IPresenceLink
    {
        public List<PresenceCard> Shown { get; } = [];

        public bool Cleared { get; private set; }

        public bool Disposed { get; private set; }

        public void Heard(PresenceLinkEvent what) => heard(what);

        public void Show(PresenceCard card)
        {
            if (links.Throw)
            {
                throw new IOException("The pipe is broken.");
            }
            Shown.Add(card);
        }

        public void Clear() => Cleared = true;

        public void Dispose() => Disposed = true;
    }

    /// <summary>A clock moved by the test, and the one timer it fires by hand.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch.AddDays(20_000);

        public ManualTimer? Timer { get; private set; }

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Timer = new ManualTimer(() => callback(state));

        public void Fire() => Timer?.Fire();
    }

    private sealed class ManualTimer(Action fire) : ITimer
    {
        private bool _gone;

        public void Fire()
        {
            if (!_gone)
            {
                fire();
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => _gone = true;

        public ValueTask DisposeAsync()
        {
            _gone = true;
            return ValueTask.CompletedTask;
        }
    }
}
