using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>One figure of the Stats page: its label and its value ("Wins", "12").</summary>
public sealed record StatTile(string Label, string Value);

/// <summary>A best time as listed: the track, the venue and laps under it, the machine, the time, when, and how often
/// that course was raced.</summary>
public sealed record BestTimeLine(string Track, string Under, string Machine, string Time, string When, string Raced);

/// <summary>A course raced most: the track, its venue, how often.</summary>
public sealed record CourseLine(string Track, string Venue, string Count);

/// <summary>One of the last races: the place ("2nd", "KO"), the field ("of 30"), a win in green, a knock-out in
/// red.</summary>
public sealed record FormMark(string Place, string Of, bool Win, bool Out);

/// <summary>
/// Stats, the Replays tab's second page: the player's figures from their own replays (<see cref="ReplayStats"/>,
/// through the replay index, <see cref="ReplayIndex"/>), of the online races only: practice has no stats (owner
/// 2026-11-06). Read again, off the window's thread, whenever the page shows.
/// </summary>
public sealed partial class StatsViewModel : ViewModelBase
{
    private readonly AppState _state;
    private IReadOnlyList<ReplayRow> _rows = [];
    private int _reading;

    public StatsViewModel(AppState state)
    {
        _state = state;
    }

    /// <summary>How many online races the figures are of, as the heading's count.</summary>
    [ObservableProperty]
    public partial string RaceCount { get; set; } = "0";

    /// <summary>The best times by machine (each course's best in each machine), not by course.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Best))]
    public partial bool ByMachine { get; set; }

    public ObservableCollection<StatTile> Tiles { get; } = [];

    public ObservableCollection<FormMark> Form { get; } = [];

    public ObservableCollection<CourseLine> MostRaced { get; } = [];

    private IReadOnlyList<BestTimeLine> _byCourse = [];
    private IReadOnlyList<BestTimeLine> _byMachine = [];

    public IReadOnlyList<BestTimeLine> Best => ByMachine ? _byMachine : _byCourse;

    [ObservableProperty]
    public partial bool Empty { get; set; }

    [ObservableProperty]
    public partial bool HasForm { get; set; }

    [ObservableProperty]
    public partial bool HasTimes { get; set; }

    /// <summary>The replays are being read (the first start after many races reads every file once).</summary>
    [ObservableProperty]
    public partial bool Reading { get; set; }

    [RelayCommand]
    private void ShowBy(string by) => ByMachine = by == "machine";

    /// <summary>The replays read again (only the new and changed ones: the index), then shown.</summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
        {
            return;
        }
        Reading = _rows.Count == 0;
        var (folder, index) = (_state.Settings.ReplayFolder, ReplayIndex.FileIn(_state.DataFolder));
        _ = Task.Run(() =>
        {
            IReadOnlyList<ReplayRow> rows;
            try
            {
                rows = ReplayIndex.Rows(folder, index);
            }
            finally
            {
                Volatile.Write(ref _reading, 0);
            }
            OnUi(() =>
            {
                _rows = rows;
                Reading = false;
                Show();
            });
        });
    }

    private void Show()
    {
        var part = ReplayStats.Of(_rows);
        RaceCount = Count(part.Count);
        var now = DateTimeOffset.Now;
        Tiles.Clear();
        foreach (var tile in TilesOf(part))
        {
            Tiles.Add(tile);
        }
        Form.Clear();
        foreach (var row in part.Recent)
        {
            Form.Add(new FormMark(row.Out ? ReplayWords.Out : ReplayWords.Ordinal(row.Place ?? 0),
                row.Racers > 0 ? ReplayWords.Of(row.Racers) : "", row.Placed == 1, row.Out));
        }
        MostRaced.Clear();
        foreach (var course in part.MostRaced)
        {
            var (venue, track) = ReplayWords.Course(Courses.Name(course.Course));
            MostRaced.Add(new CourseLine(track, venue, Count(course.Count)));
        }
        _byCourse = [.. part.BestByCourse.Select(b => Line(b, now))];
        _byMachine = [.. part.BestByMachine.Select(b => Line(b, now))];
        OnPropertyChanged(nameof(Best));
        HasForm = Form.Count > 0;
        HasTimes = _byCourse.Count > 0;
        Empty = part.Count == 0;
    }

    private static IEnumerable<StatTile> TilesOf(StatsPart part)
    {
        var p = part.Places;
        yield return new StatTile("Races", Count(part.Count));
        yield return new StatTile("Wins", Count(p.Wins));
        yield return new StatTile("Podiums", Count(p.Podiums));
        yield return new StatTile("Average place", p.AveragePlace is { } average ? average.ToString("0.0", CultureInfo.InvariantCulture) : "-");
        yield return new StatTile("Knocked out", Count(p.KnockedOut));
    }

    private static BestTimeLine Line(BestTime best, DateTimeOffset now)
    {
        var (venue, track) = ReplayWords.Course(Courses.Name(best.Course));
        return new BestTimeLine(track, string.Join(" · ", new[] { venue, ReplayWords.Laps(best.Laps) }.Where(w => w.Length > 0)),
            Theme.GameSkin.NameOf(best.Machine.Raced), ReplayWords.Clock(best.Time),
            HomeViewModel.When(best.When.ToLocalTime(), now), Count(best.Raced));
    }

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
}
