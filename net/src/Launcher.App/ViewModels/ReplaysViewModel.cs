using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>One replay as listed (like Slippi's rows): the racer it is seen from, the course, the result, when, and
/// the race's results when the file has them. The words are <see cref="ReplayWords"/>'s: whatever the file says,
/// a row can show it.</summary>
public sealed partial class ReplayItem(ReplayInfo info) : ObservableObject
{
    private const int StatsColumn = 15;

    public ReplayInfo Info { get; } = info;

    public string Path => Info.Path;

    public string FileName => Info.FileName;

    /// <summary>The course's number, for its picture from the disc.</summary>
    public int Course => Info.Course;

    public string Venue { get; } = ReplayWords.Course(info.CourseName).Venue;

    public string Track { get; } = ReplayWords.Course(info.CourseName).Track;

    /// <summary>Under the track: the venue, the laps, and the race's stat pack when it had one.</summary>
    public string Under { get; } = string.Join(" · ", new[]
    {
        ReplayWords.Course(info.CourseName).Venue, ReplayWords.Laps(info.Laps), info.StatPackLabel,
    }.Where(word => word.Length > 0));

    public string RacerName { get; } = ReplayWords.Racer(info);

    public string RacerMachine { get; } = info.Recorder is { } recorder ? Theme.GameSkin.NameOf(recorder.Raced) : "";

    public string RacerColorHex { get; } = ReplayWords.Hex(info.Recorder?.Color);

    public string Place { get; } = ReplayWords.Place(info);

    /// <summary>The racer was knocked out: the place, "KO", in red.</summary>
    public bool PlaceOut => Place == ReplayWords.Out;

    public string Of { get; } = ReplayWords.Of(info);

    public string Time { get; } = ReplayWords.Time(info);

    public string When { get; } = HomeViewModel.When(info.Recorded.ToLocalTime(), DateTimeOffset.Now);

    /// <summary>A practice run: its Result says "Practice" with the time, and it has no results to open.</summary>
    public bool IsPractice => Info.Practice;

    public bool IsRace => !Info.Practice;

    public bool HasStats => !Info.Practice && Info.Results is { Count: > 0 };

    /// <summary>The results in two columns, the first 15 places and the rest.</summary>
    public IReadOnlyList<ReplayResultLine> StatsLeft => [.. ReplayWords.Results(Info).Take(StatsColumn)];

    public IReadOnlyList<ReplayResultLine> StatsRight => [.. ReplayWords.Results(Info).Skip(StatsColumn)];

    /// <summary>The row's results are open under it.</summary>
    [ObservableProperty]
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

/// <summary>Replays: the player's recorded races, newest first, Slippi-style; play one (as the race), see its
/// results, show it in the folder or delete it; open a shared file. The list is read again whenever the page
/// shows.</summary>
public sealed partial class ReplaysViewModel : ViewModelBase
{
    private readonly AppState _state;

    public ReplaysViewModel(AppState state)
    {
        _state = state;
        Refresh();
    }

    public ObservableCollection<ReplayItem> Items { get; } = [];

    /// <summary>The tabs: every replay, the races, the practice runs.</summary>
    public IReadOnlyList<ReplayTab> Tabs { get; } =
        [new(ReplayKind.All, "All") { On = true }, new(ReplayKind.Races, "Races"), new(ReplayKind.Practice, "Practice")];

    /// <summary>The tab showing.</summary>
    [ObservableProperty]
    public partial ReplayKind Kind { get; set; } = ReplayKind.All;

    private List<ReplayInfo> _all = [];

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

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    [RelayCommand]
    public void Refresh()
    {
        _all = [.. ReplayFile.List(_state.Settings.ReplayFolder)];
        Show();
        Notice = "";
    }

    /// <summary>The rows of the tab showing, and every tab's count.</summary>
    private void Show()
    {
        Items.Clear();
        foreach (var r in _all.Where(r => Kind.Shows(r)))
        {
            Items.Add(new ReplayItem(r));
        }
        foreach (var tab in Tabs)
        {
            tab.Count = tab.Kind.Count(_all);
        }
        Empty = Items.Count == 0;
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
            // the row goes; the others stay as they are (their results open, the page where it was)
            _all.Remove(item.Info);
            Items.Remove(item);
            foreach (var tab in Tabs)
            {
                tab.Count = tab.Kind.Count(_all);
            }
            Empty = Items.Count == 0;
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
