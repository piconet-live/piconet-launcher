using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>One replay as listed (like Slippi's rows): the racer it is seen from, the course, the result, when, and
/// the race's results when the file has them. From the replay index's listing (<see cref="ReplayListing"/>): the file
/// is read only when the row's results open. The words are <see cref="ReplayWords"/>'s: whatever the file says, a
/// row can show it.</summary>
public sealed partial class ReplayItem : ObservableObject
{
    private const int StatsColumn = 15;

    private ReplayInfo? _info;

    public ReplayItem(ReplayListing listing, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        Listing = listing;
        var (venue, track) = ReplayWords.Course(listing.CourseName);
        (Venue, Track) = (venue, track);
        Under = string.Join(" · ", new[] { venue, ReplayWords.Laps(listing.Laps), listing.StatPackLabel }.Where(word => word.Length > 0));
        RacerMachine = listing.Raced is { } raced ? Theme.GameSkin.NameOf(raced) : "";
        When = ReplayDates.When(listing.Recorded, now ?? DateTimeOffset.Now);
    }

    /// <summary>A replay already read whole (a file opened from elsewhere, a test).</summary>
    public ReplayItem(ReplayInfo info) : this(ReplayListing.From(info)) => _info = info;

    public ReplayListing Listing { get; }

    public string Path => Listing.Path;

    public string FileName => Listing.FileName;

    /// <summary>The course's number, for its picture from the disc.</summary>
    public int Course => Listing.Course;

    public string Venue { get; }

    public string Track { get; }

    /// <summary>Under the track: the venue, the laps, and the race's stat pack when it had one.</summary>
    public string Under { get; }

    public string RacerName => Listing.Racer;

    public string RacerMachine { get; }

    public string RacerColorHex => ReplayWords.Hex(Listing.Color);

    public string Place => Listing.Place;

    /// <summary>The racer was knocked out: the place, "KO", in red.</summary>
    public bool PlaceOut => Listing.Out;

    public string Of => Listing.Of;

    public string Time => Listing.Time;

    public string When { get; }

    /// <summary>A practice run: its Result says "Practice" with the time, and it has no results to open.</summary>
    public bool IsPractice => Listing.Practice;

    public bool IsRace => !Listing.Practice;

    public bool HasStats => Listing.HasResults;

    /// <summary>The results in two columns, the first 15 places and the rest (the file read the first time).</summary>
    public IReadOnlyList<ReplayResultLine> StatsLeft => [.. Results().Take(StatsColumn)];

    public IReadOnlyList<ReplayResultLine> StatsRight => [.. Results().Skip(StatsColumn)];

    private IReadOnlyList<ReplayResultLine> Results()
    {
        _info ??= ReplayFile.Read(Path);
        return _info is null ? [] : ReplayWords.Results(_info);
    }

    /// <summary>The row's results are open under it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsLeft), nameof(StatsRight))]
    public partial bool ShowingStats { get; set; }

    /// <summary>What the row's right end offers: "actions" (stats, more, play), "more" (show the file, delete) or
    /// "delete" (the question before deleting).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Acting), nameof(More), nameof(ConfirmingDelete))]
    public partial string Offer { get; set; } = "actions";

    public bool Acting => Offer == "actions";

    public bool More => Offer == "more";

    public bool ConfirmingDelete => Offer == "delete";
}

/// <summary>A group's heading in the Replays page's list: "Today", "Last week", "September", with how many replays it
/// holds.</summary>
public sealed partial class ReplayGroup(string title, long order) : ObservableObject
{
    public string Title { get; } = title;

    public long Order { get; } = order;

    [ObservableProperty]
    public partial int Count { get; set; }
}

/// <summary>One of the Replays page's tabs over the table: All, Races, Practice, each with its count.</summary>
public sealed partial class ReplayTab(ReplayKind kind, string label) : ObservableObject
{
    public ReplayKind Kind { get; } = kind;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool On { get; set; }
}

/// <summary>Replays: the player's recorded races, newest first in groups by date (Today, Yesterday, ... a month),
/// Slippi-style; play one (as the race), see its results, show it in the folder or delete it; open a shared file.
/// The list comes from the replay index (<see cref="ReplayIndex.Listings"/>), read off the window's thread whenever
/// the page shows: only replays that are new or changed are opened, so thousands list as fast as a few. The view
/// draws only the rows on the screen.</summary>
public sealed partial class ReplaysViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly Func<DateTimeOffset> _now;
    private List<ReplayListing> _all = [];
    private int _reads;   // the newest read asked for: an older read's answer is not shown over a newer one's

    public ReplaysViewModel(AppState state, Func<DateTimeOffset>? now = null)
    {
        _state = state;
        _now = now ?? (() => DateTimeOffset.Now);
        Refresh();
    }

    /// <summary>The list as the page draws it: each group's heading (<see cref="ReplayGroup"/>), then its replays
    /// (<see cref="ReplayItem"/>).</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>The replays of the tab showing, in the list's order.</summary>
    public IReadOnlyList<ReplayItem> Items => [.. Rows.OfType<ReplayItem>()];

    /// <summary>The tabs: every replay, the races, the practice runs.</summary>
    public IReadOnlyList<ReplayTab> Tabs { get; } =
        [new(ReplayKind.All, "All") { On = true }, new(ReplayKind.Races, "Races"), new(ReplayKind.Practice, "Practice")];

    /// <summary>The tab showing.</summary>
    [ObservableProperty]
    public partial ReplayKind Kind { get; set; } = ReplayKind.All;

    partial void OnKindChanged(ReplayKind value)
    {
        foreach (var tab in Tabs)
        {
            tab.On = tab.Kind == value;
        }
        Show();
    }

    [RelayCommand]
    private void ShowTab(ReplayTab tab) => Kind = tab.Kind;

    [ObservableProperty]
    public partial bool Empty { get; set; }

    /// <summary>The folder is being read (the first time: the list says so in its place).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstLoad))]
    public partial bool Loading { get; set; }

    /// <summary>The first read, with nothing listed yet.</summary>
    public bool FirstLoad => Loading && _all.Count == 0;

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The folder read again, off the window's thread; the list follows when the read is done
    /// (<see cref="Loading"/> until then). The rows shown stay as they are meanwhile.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var read = ++_reads;
        Loading = true;
        Notice = "";
        var (folder, index) = (_state.Settings.ReplayFolder, ReplayIndex.FileIn(_state.DataFolder));
        _ = Task.Run(() => ReplayIndex.Listings(folder, index)).ContinueWith(done =>
        {
            var listings = done.IsCompletedSuccessfully ? done.Result : [];
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (read != _reads)
                {
                    return;   // a newer read is on its way
                }
                Take(listings);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The listings of a read: rows already shown keep their state (results open, the file's actions), so a
    /// read after a race only adds the new replay.</summary>
    private void Take(IReadOnlyList<ReplayListing> listings)
    {
        _all = [.. listings];
        Show();
        Loading = false;
    }

    /// <summary>The rows of the tab showing in their groups, and every tab's count. A row already shown for the same
    /// file is kept as it is.</summary>
    private void Show()
    {
        var kept = Rows.OfType<ReplayItem>().ToDictionary(i => i.Path, StringComparer.Ordinal);
        var now = _now();
        var rows = new List<object>();
        ReplayGroup? group = null;
        foreach (var r in _all.Where(r => Kind.Shows(r)))
        {
            var (order, title) = ReplayDates.Group(r.Recorded, now);
            if (group is null || group.Order != order)
            {
                group = new ReplayGroup(title, order);
                rows.Add(group);
            }
            group.Count++;
            rows.Add(kept.TryGetValue(r.Path, out var item) && item.Listing == r ? item : new ReplayItem(r, now));
        }
        if (!rows.SequenceEqual(Rows))
        {
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
            }
        }
        foreach (var tab in Tabs)
        {
            tab.Count = tab.Kind.Count(_all);
        }
        Empty = !Rows.OfType<ReplayItem>().Any();
        OnPropertyChanged(nameof(Items));
    }

    [RelayCommand]
    private void Play(string path)
    {
        try
        {
            Notice = _state.Play(path) ?? "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Notice = "Game did not start";
        }
    }

    /// <summary>Stats: the row's results open under it, or close.</summary>
    [RelayCommand]
    private static void ToggleStats(ReplayItem item) => item.ShowingStats = !item.ShowingStats;

    /// <summary>"•••": the row offers its file's actions, or its usual ones again; one row at a time, so a row
    /// opened before goes back to its usual actions.</summary>
    [RelayCommand]
    private void ToggleMore(ReplayItem item)
    {
        var opening = item.Offer == "actions";
        foreach (var other in Items.Where(i => i != item && i.Offer != "actions"))
        {
            other.Offer = "actions";
        }
        item.Offer = opening ? "more" : "actions";
    }

    [RelayCommand]
    private void Reveal(ReplayItem item)
    {
        item.Offer = "actions";
        Notice = Replays.TryReveal(item.Path) ? "" : "File could not be shown";
    }

    [RelayCommand]
    private static void AskDelete(ReplayItem item) => item.Offer = "delete";

    [RelayCommand]
    private static void KeepReplay(ReplayItem item) => item.Offer = "actions";

    [RelayCommand]
    private void Delete(ReplayItem item)
    {
        if (ReplayFile.Delete(item.Path))
        {
            // the row goes, and its group's heading with its last row; the others stay as they are (their results
            // open, the page where it was)
            _all.Remove(item.Listing);
            var at = Rows.IndexOf(item);
            Rows.Remove(item);
            if (at > 0 && Rows[at - 1] is ReplayGroup group && --group.Count == 0)
            {
                Rows.RemoveAt(at - 1);
            }
            foreach (var tab in Tabs)
            {
                tab.Count = tab.Kind.Count(_all);
            }
            Empty = !Rows.OfType<ReplayItem>().Any();
            OnPropertyChanged(nameof(Items));
        }
        else
        {
            item.Offer = "actions";
            Notice = $"Could not delete {ReplayWords.Name(item.FileName)}";
        }
    }

    [RelayCommand]
    private void OpenFolder() =>
        Notice = DolphinTools.TryOpenFolder(_state.Settings.ReplayFolder) ? "" : "Folder could not be opened";
}
