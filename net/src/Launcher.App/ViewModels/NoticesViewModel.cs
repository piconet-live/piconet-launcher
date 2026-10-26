using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>What the window does to be noticed for a notice: its taskbar button flashes, or its tone sounds.</summary>
public enum NoticeAlert
{
    Flash,
    Sound,
}

/// <summary>
/// The server's notices, all in one place (the chat sidebar that is coming will take the list over as a tab): the
/// banner across the top of the window, the bell with its dot, and the list of the notices this PC received, kept
/// in the data folder (<see cref="NoticeLog"/>). By tier (<see cref="NoticeLevel"/>): info is a chat line and an
/// entry here; notice also a banner for <see cref="BannerTime"/>; warning a banner that stays until dismissed, and
/// the taskbar flashes; urgent also a short tone (<see cref="Alert"/>: the window flashes and sounds, the tone at the
/// launcher's volume and never when its sounds are muted). A notice the server sends again to a launcher that came
/// later (replayed) neither flashes nor sounds; one this PC has kept already does nothing at all.
/// </summary>
public sealed partial class NoticesViewModel : ViewModelBase, IDisposable
{
    /// <summary>How long a notice's banner shows (a warning's and an urgent one's stay until dismissed).</summary>
    public static readonly TimeSpan BannerTime = TimeSpan.FromSeconds(10);

    private readonly AppState _state;
    private readonly TimeProvider _time;
    private QuickPlaySession? _watched;
    private ITimer? _banner;
    private NoticeLevel _bannerLevel;
    private string? _bannerId;
    private long _showings;

    /// <param name="time">The clock a notice's banner is timed by (tests: their own).</param>
    public NoticesViewModel(AppState state, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _time = time ?? TimeProvider.System;
        Refresh();
        HasDot = state.Notices.Unseen;
        state.Changed += Watch;
        Watch();
    }

    /// <summary>The window flashes or sounds (on the window's thread).</summary>
    public event Action<NoticeAlert>? Alert;

    [ObservableProperty]
    public partial bool BannerShown { get; set; }

    /// <summary>The notice the banner shows (null before the first).</summary>
    [ObservableProperty]
    public partial NoticeItem? Banner { get; set; }

    /// <summary>The bell's dot: a notice came that the list has not shown since.</summary>
    [ObservableProperty]
    public partial bool HasDot { get; set; }

    [ObservableProperty]
    public partial bool ListOpen { get; set; }

    /// <summary>Newest first.</summary>
    public ObservableCollection<NoticeItem> List { get; } = [];

    public bool Empty => List.Count == 0;

    /// <summary>How long the server keeps a notice to send it again: a replayed one older than this is no banner.</summary>
    public static readonly TimeSpan ReplayedBannerAge = TimeSpan.FromHours(1);

    /// <summary>The server's notices of the last hour, sent again when the launcher came (or joined a lobby), on the
    /// window's thread: into the list, once each, with the bell's dot for any it did not have. The newest warning
    /// or urgent one that is still within its hour, and whose banner the player has not dismissed, shows as the
    /// banner, without a sound or a flash.</summary>
    public void ReceiveReplayed(IReadOnlyList<ServerNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        var added = false;
        foreach (var n in notices)
        {
            added |= _state.Notices.Add(n);
        }
        if (added)
        {
            Refresh();
            if (ListOpen)
            {
                _state.Notices.Seen();
            }
            HasDot = _state.Notices.Unseen;
        }
        var now = _time.GetUtcNow();
        if (notices.Where(n => n.Level >= NoticeLevel.Warning && now - n.Time < ReplayedBannerAge && !_state.Notices.Dismissed(n.Id))
                .MaxBy(n => n.Time) is { } newest &&
            (!BannerShown || newest.Level >= _bannerLevel))
        {
            ShowBanner(newest);
        }
    }

    /// <summary>A notice from the server that came while connected, on the window's thread.</summary>
    public void Receive(ServerNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (!_state.Notices.Add(notice))
        {
            return;   // kept already: a notice of the last hour sent again
        }
        Refresh();
        if (ListOpen)
        {
            _state.Notices.Seen();
        }
        HasDot = _state.Notices.Unseen;
        if (notice.Level >= NoticeLevel.Notice && (!BannerShown || notice.Level >= _bannerLevel))
        {
            ShowBanner(notice);
        }
        if (_state.Settings.NoticesSilent)
        {
            return;   // the player muted notices: shown, never flashed or sounded
        }
        if (notice.Level >= NoticeLevel.Warning)
        {
            Alert?.Invoke(NoticeAlert.Flash);
        }
        if (notice.Level == NoticeLevel.Urgent)
        {
            Alert?.Invoke(NoticeAlert.Sound);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (BannerShown)
        {
            _state.Notices.Dismiss(_bannerId);   // the player's own word: kept, and never this banner again
        }
        Close();
    }

    /// <summary>The banner goes (its time is up, or the player dismissed it).</summary>
    private void Close()
    {
        _banner?.Dispose();
        _banner = null;
        BannerShown = false;
    }

    /// <summary>The bell: the list opens (and its dot goes), or closes.</summary>
    [RelayCommand]
    private void ToggleList()
    {
        ListOpen = !ListOpen;
        if (ListOpen)
        {
            _state.Notices.Seen();
            HasDot = false;
        }
    }

    private void ShowBanner(ServerNotice notice)
    {
        _banner?.Dispose();
        _banner = null;
        (Banner, _bannerLevel, _bannerId) = (NoticeItem.Of(notice, _time.GetUtcNow()) with { Showing = ++_showings }, notice.Level, notice.Id);
        BannerShown = true;
        if (notice.Level == NoticeLevel.Notice)
        {
            ITimer? timer = null;
            timer = _time.CreateTimer(_ => OnUi(() =>
            {
                if (_banner == timer)
                {
                    Close();
                }
            }), null, BannerTime, Timeout.InfiniteTimeSpan);
            _banner = timer;
        }
    }

    private void Refresh()
    {
        List.Clear();
        foreach (var n in _state.Notices.Notices.Reverse())
        {
            List.Add(NoticeItem.Of(n, _time.GetUtcNow()));
        }
        OnPropertyChanged(nameof(Empty));
    }

    /// <summary>The session's notices are heard: a new session's (and the one before is no longer).</summary>
    private void Watch()
    {
        if (_state.Session == _watched)
        {
            return;
        }
        _watched?.NoticeReceived -= OnNotice;
        _watched?.NoticesReplayed -= OnReplayed;
        _watched = _state.Session;
        _watched?.NoticeReceived += OnNotice;
        _watched?.NoticesReplayed += OnReplayed;
    }

    private void OnNotice(ServerNotice notice) => OnUi(() => Receive(notice));

    private void OnReplayed(IReadOnlyList<ServerNotice> notices) => OnUi(() => ReceiveReplayed(notices));

    public void Dispose()
    {
        _state.Changed -= Watch;
        _watched?.NoticeReceived -= OnNotice;
        _watched?.NoticesReplayed -= OnReplayed;
        _banner?.Dispose();
    }
}
