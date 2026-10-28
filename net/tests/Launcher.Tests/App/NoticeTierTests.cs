using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// A server's notice in the launcher's own window, by tier, without a screen: info is an entry in the list (and the
/// bell's dot), no banner; notice a banner that goes after ten seconds (the test's own clock); warning a banner that
/// stays until × is pressed, and the taskbar's flash; urgent also its tone, at the launcher's volume, and none when
/// its sounds are muted. The last hour's notices sent again neither flash nor sound; the newest urgent one is the
/// banner. The bell opens the list and its dot goes; a press on the page, Escape or another page closes it, a press
/// inside it does not. No sound is made: <see cref="Attention.Played"/> and
/// <see cref="Attention.Flashed"/> are counted, and nothing plays on Linux.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NoticeTierTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock the test moves, with its timers.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<(DateTimeOffset Due, TimerCallback Call, object? State, Timer Self)> _timers = [];

        public DateTimeOffset Now { get; private set; } = Noon;

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this);
            _timers.Add((Now + dueTime, callback, state, timer));
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            Now += by;
            foreach (var t in _timers.Where(t => t.Due <= Now && !t.Self.Disposed).ToList())
            {
                _timers.Remove(t);
                t.Call(t.State);
            }
            Dispatcher.UIThread.RunJobs();
        }

        public sealed class Timer(ManualTime time) : ITimer
        {
            public bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed && time is not null;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed record Seen(MainWindow Window, MainViewModel Main, ManualTime Time, List<(LobbyMoment Moment, int Volume)> Sounds, List<Window> Flashes);

    /// <summary>The launcher's window with its tabs showing, its sounds as set, and what it asks of Attention.</summary>
    private async Task Run(bool muted, Action<Seen> test, bool silent = false)
    {
        var data = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), $$"""{"noServer": true, "soundMuted": {{(muted ? "true" : "false")}}, "soundVolume": 40, "noticesSilent": {{(silent ? "true" : "false")}}}""");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var sounds = new List<(LobbyMoment, int)>();
                var flashes = new List<Window>();
                void Played(LobbyMoment m, int v) => sounds.Add((m, v));
                void Flashed(Window w) => flashes.Add(w);
                Attention.Played += Played;
                Attention.Flashed += Flashed;
                try
                {
                    GameSkin.Load(null);
                    var time = new ManualTime();
                    var main = new MainViewModel(state, noticeTime: time);
                    var window = new MainWindow { DataContext = main, Width = 1100, Height = 720 };
                    window.Show();
                    main.ShowSettings(SettingsTab.Online);                     // where Notices is
                    Dispatcher.UIThread.RunJobs();
                    test(new Seen(window, main, time, sounds, flashes));
                    window.Close();
                }
                finally
                {
                    Attention.Played -= Played;
                    Attention.Flashed -= Flashed;
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    private static ServerNotice Notice(NoticeLevel level, string id, int minutes = 0) => new(id, level, $"{level} text", Noon.AddMinutes(minutes));

    private static bool BandShows(Seen s) => Named<Panel>(s.Window, "Band").IsEffectivelyVisible;

    private static List<(LobbyMoment, int)> Urgent(Seen s) => [.. s.Sounds.Where(x => x.Moment == LobbyMoment.UrgentNotice)];

    private static int FlashesOf(Seen s) => s.Flashes.Count(w => w == s.Window);

    [Fact]
    public Task InfoIsAnEntryInTheListWithTheDotAndNoBanner() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Info, "i-1"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(BandShows(s));
        Assert.True(Named<Avalonia.Controls.Shapes.Ellipse>(s.Window, "NoticeDot").IsEffectivelyVisible);
        Assert.Single(s.Main.Notices.List);
        Assert.Equal((0, 0), (FlashesOf(s), Urgent(s).Count));
    });

    [Fact]
    public Task ANoticesBannerGoesAfterTenSeconds() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Notice, "n-1"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(BandShows(s));
        Assert.Equal("notice", Named<NoticeCard>(s.Window, "BannerCard").Tier);
        s.Time.Advance(TimeSpan.FromSeconds(9.5));
        Assert.True(BandShows(s));
        s.Time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.False(BandShows(s));
        Assert.Equal((0, 0), (FlashesOf(s), Urgent(s).Count));
    });

    /// <summary>A second notice with the first's text, tier and minute is a new banner: its bar starts full again,
    /// with its ten seconds (the bar starts again when the banner changes).</summary>
    [Fact]
    public Task ASecondLikeNoticeStartsTheBannersBarAgain() => Run(muted: false, s =>
    {
        var changed = 0;
        s.Main.Notices.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(NoticesViewModel.Banner) ? 1 : 0;
        s.Main.Notices.Receive(Notice(NoticeLevel.Notice, "n-1"));
        Dispatcher.UIThread.RunJobs();
        var first = s.Main.Notices.Banner;
        s.Time.Advance(TimeSpan.FromSeconds(6));
        s.Main.Notices.Receive(Notice(NoticeLevel.Notice, "n-2"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((first!.Text, first.Level, first.When), (s.Main.Notices.Banner!.Text, s.Main.Notices.Banner.Level, s.Main.Notices.Banner.When));
        Assert.NotEqual(first, s.Main.Notices.Banner);
        Assert.Equal(2, changed);
        s.Time.Advance(TimeSpan.FromSeconds(9.5));
        Assert.True(BandShows(s));
        s.Time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.False(BandShows(s));
    });

    [Fact]
    public Task AWarningStaysUntilDismissedAndFlashes() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Warning, "w-1"));
        Dispatcher.UIThread.RunJobs();
        s.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(BandShows(s));
        Assert.Equal("warning", Named<NoticeCard>(s.Window, "BannerCard").Tier);
        Assert.Equal((1, 0), (FlashesOf(s), Urgent(s).Count));
        Click(s.Window, Named<Button>(s.Window, "DismissNotice"));
        Assert.False(BandShows(s));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnUrgentNoticeFlashesAndSoundsUnlessMuted(bool muted) => Run(muted, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Urgent, "u-1"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(BandShows(s));
        Assert.Equal("urgent", Named<NoticeCard>(s.Window, "BannerCard").Tier);
        Assert.Equal(1, FlashesOf(s));
        Assert.Equal(muted ? [] : [(LobbyMoment.UrgentNotice, 40)], Urgent(s));
        s.Main.Notices.Receive(Notice(NoticeLevel.Urgent, "u-1"));   // the same notice again: nothing more
        Assert.Equal(1, FlashesOf(s));
        Assert.Single(s.Main.Notices.List);
    });

    /// <summary>Players can mute notifications. Silent on the Settings page: an urgent notice
    /// shows as ever (the banner, the list) and neither flashes nor sounds; Alert again, and it does both.</summary>
    [Fact]
    public Task ANoticeSetSilentOnTheSettingsPageNeitherFlashesNorSounds() => Run(muted: false, s =>
    {
        var notices = Named<ListBox>(s.Window, "Notices");
        Assert.True(notices.IsEffectivelyVisible);                                  // the Online page's
        Assert.Equal(0, notices.SelectedIndex);                                     // Alert unless chosen
        notices.SelectedIndex = 1;                                                  // Silent
        Dispatcher.UIThread.RunJobs();
        s.Main.Notices.Receive(Notice(NoticeLevel.Urgent, "u-1"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(BandShows(s));
        Assert.Single(s.Main.Notices.List);
        Assert.Equal((0, 0), (FlashesOf(s), Urgent(s).Count));
        notices.SelectedIndex = 0;                                                  // Alert
        Dispatcher.UIThread.RunJobs();
        s.Main.Notices.Receive(Notice(NoticeLevel.Urgent, "u-2"));
        Assert.Equal((1, 1), (FlashesOf(s), Urgent(s).Count));
    });

    [Fact]
    public Task ASilentSettingIsReadFromTheFile() => Run(muted: false, s =>
    {
        Assert.Equal(1, Named<ListBox>(s.Window, "Notices").SelectedIndex);
        s.Main.Notices.Receive(Notice(NoticeLevel.Warning, "w-1"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(BandShows(s));
        Assert.Equal(0, FlashesOf(s));
    }, silent: true);

    [Fact]
    public Task TheLastHoursNoticesNeitherFlashNorSoundAndTheNewestUrgentIsTheBanner() => Run(muted: false, s =>
    {
        s.Time.Advance(TimeSpan.FromMinutes(30));
        s.Main.Notices.ReceiveReplayed([Notice(NoticeLevel.Urgent, "r-1", 0), Notice(NoticeLevel.Warning, "r-2", 10), Notice(NoticeLevel.Info, "r-3", 20)]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(BandShows(s));
        Assert.Equal(("warning", "Warning text"), (s.Main.Notices.Banner!.Level, s.Main.Notices.Banner.Text));   // the newest of the two
        Assert.Equal((0, 0), (FlashesOf(s), Urgent(s).Count));
        Assert.Equal(3, s.Main.Notices.List.Count);
        s.Main.Notices.ReceiveReplayed([Notice(NoticeLevel.Urgent, "r-1", 0)]);   // sent again: kept once
        Assert.Equal(3, s.Main.Notices.List.Count);
        Assert.Equal((0, 0), (FlashesOf(s), Urgent(s).Count));
    });

    /// <summary>A banner the player dismissed stays dismissed, also when the server sends the
    /// notice again on a reconnect within its hour (it stays in the list).</summary>
    [Fact]
    public Task ADismissedBannerIsNoBannerAgainOnAReconnect() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Urgent, "u-1"));
        Dispatcher.UIThread.RunJobs();
        Click(s.Window, Named<Button>(s.Window, "DismissNotice"));
        Assert.False(BandShows(s));
        s.Time.Advance(TimeSpan.FromMinutes(10));
        s.Main.Notices.ReceiveReplayed([Notice(NoticeLevel.Urgent, "u-1")]);   // the reconnect's last hour
        Dispatcher.UIThread.RunJobs();
        Assert.False(BandShows(s));
        Assert.Single(s.Main.Notices.List);
        Assert.True(s.Main.Notices.List[0] is { Level: "urgent" });
    });

    [Fact]
    public Task TheBellOpensTheListAndItsDotGoes() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Info, "i-1"));
        s.Main.Notices.Receive(Notice(NoticeLevel.Warning, "w-1", 1));
        Dispatcher.UIThread.RunJobs();
        var dot = Named<Avalonia.Controls.Shapes.Ellipse>(s.Window, "NoticeDot");
        Assert.True(dot.IsEffectivelyVisible);
        Click(s.Window, Named<Button>(s.Window, "NoticeBell"));
        Assert.False(dot.IsEffectivelyVisible);
        var list = Named<NoticesList>(s.Window, "Notices");
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(["warning", "info"], list.GetVisualDescendants().OfType<NoticeCard>().Select(c => c.Tier));   // newest first
        Assert.False(s.Main.Notices.HasDot);
        Click(s.Window, Named<Button>(s.Window, "CloseNotices"));
        Assert.False(list.IsEffectivelyVisible);
    });

    [Fact]
    public Task TheListClosesOnAPressOutsideEscapeOrAnotherPage() => Run(muted: false, s =>
    {
        s.Main.Notices.Receive(Notice(NoticeLevel.Info, "i-1"));
        Dispatcher.UIThread.RunJobs();
        var bell = Named<Button>(s.Window, "NoticeBell");
        var list = Named<NoticesList>(s.Window, "Notices");
        Click(s.Window, bell);
        Click(s.Window, list.GetVisualDescendants().OfType<NoticeCard>().First());   // inside: it stays
        Assert.True(list.IsEffectivelyVisible);
        var page = new Avalonia.Point(200, s.Window.Bounds.Height - 40);             // the left column, away from the list
        s.Window.MouseDown(page, Avalonia.Input.MouseButton.Left);
        s.Window.MouseUp(page, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(s.Main.Notices.ListOpen);
        Click(s.Window, bell);
        Press(s.Window, Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        Assert.False(s.Main.Notices.ListOpen);
        Click(s.Window, bell);
        s.Main.ShowTabCommand.Execute("garage");
        Dispatcher.UIThread.RunJobs();
        Assert.False(list.IsEffectivelyVisible);
    });
}
