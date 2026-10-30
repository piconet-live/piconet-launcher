using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Controls;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The race page: the lobby's race as it runs, then its results. While the player races, from
/// their own game (<see cref="AppState.LiveReceived"/>, every frame); while they wait in a racing lobby,
/// from a racer's game through the coordinator (<see cref="QuickPlaySession.Live"/>). Shows the player's own numbers,
/// the standings and the 3D track map; laid out for a narrow window beside the game too.
/// </summary>
public sealed partial class RaceViewModel : ViewModelBase
{
    private readonly AppState _state;
    private QuickPlaySession? _session;
    private RaceSnapshot? _last;
    // the match the page shows: a new one clears the last one's course, map and standings at once
    private string? _shownMatch;
    private long _shown;
    private long? _lostAt;

    /// <summary>How long the map stays on a followed racer's last place once it is knocked out or gone, before going
    /// back to the whole course: long enough to see where it happened.</summary>
    private const long LostMs = 2000;

    public RaceViewModel(AppState state)
    {
        _state = state;
        state.LiveReceived += s => OnUi(() => FromGame(s));
        state.Changed += () => OnUi(Attach);
        Attach();
    }

    [ObservableProperty]
    public partial string Course { get; set; } = "";

    /// <summary>Racing, Watching or Results.</summary>
    [ObservableProperty]
    public partial string Stage { get; set; } = "";

    [ObservableProperty]
    public partial string LapText { get; set; } = "";

    [ObservableProperty]
    public partial string TimeText { get; set; } = "";

    /// <summary>Nothing from the games yet (they are loading the race).</summary>
    [ObservableProperty]
    public partial bool Waiting { get; set; } = true;

    [ObservableProperty]
    public partial bool HasYou { get; set; }

    [ObservableProperty]
    public partial string YouPosition { get; set; } = "";

    /// <summary>The player is knocked out: their place reads "KO", in red.</summary>
    [ObservableProperty]
    public partial bool YouOut { get; set; }

    [ObservableProperty]
    public partial string YouOf { get; set; } = "";

    [ObservableProperty]
    public partial string YouLap { get; set; } = "";

    [ObservableProperty]
    public partial string YouLapTime { get; set; } = "";

    [ObservableProperty]
    public partial string YouBestLap { get; set; } = "";

    [ObservableProperty]
    public partial string YouSpeed { get; set; } = "";

    /// <summary>Energy, 0 to 1.</summary>
    [ObservableProperty]
    public partial double YouEnergy { get; set; }

    [ObservableProperty]
    public partial CourseModel? Map { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapRacer> MapRacers { get; set; } = [];

    public ObservableCollection<StandingRow> Standings { get; } = [];

    /// <summary>The racer the map follows, by seat (a racer pressed in the standings is followed), or
    /// null for the whole course.</summary>
    [ObservableProperty]
    public partial int? Followed { get; set; }

    /// <summary>A click on a racer's line: follow them, or, when already followed, back to the whole course. A
    /// knocked-out racer has nothing left to watch.</summary>
    [RelayCommand]
    private void Follow(StandingRow row)
    {
        if (Followed == row.Seat)
        {
            Followed = null;
        }
        else if (!row.Out)
        {
            Followed = row.Seat;
        }
    }

    [RelayCommand]
    private void WholeCourse() => Followed = null;

    partial void OnFollowedChanged(int? value)
    {
        _lostAt = null;
        // the standings' lit line moves now, not at their next refresh
        for (var i = 0; i < Standings.Count; i++)
        {
            if (Standings[i].Followed != (Standings[i].Seat == value))
            {
                Standings[i] = Standings[i] with { Followed = Standings[i].Seat == value };
            }
        }
    }

    private void Attach()
    {
        if (_state.Session is { } s && s != _session)
        {
            _session = s;
            s.Changed += () => OnUi(FromSession);
        }
        FromSession();
    }

    /// <summary>The player's own game: the race as it runs here.</summary>
    private void FromGame(RaceSnapshot snapshot)
    {
        if (Setup() is { } setup)
        {
            Begin(setup);
            Show(snapshot, setup.Seats, setup.Course, setup.Laps, snapshot.LocalSeat, watching: false);
        }
    }

    /// <summary>The lobby changed, or a racer's report came through the coordinator.</summary>
    private void FromSession()
    {
        if (_session is not { } session)
        {
            return;
        }
        var lobby = session.Lobby;
        if (lobby is { Stage: LobbyState.Starting or LobbyState.Choosing } or null)
        {
            // the next match is forming: this race is done with
            if (!_state.Racing)
            {
                _last = null;
                Followed = null;
            }
        }
        if (Setup() is { } next)
        {
            Begin(next);   // before the early return: while the player's own game loads, the page shows its course
        }
        if (_state.Racing && session.Racing)
        {
            return;   // the player's own game has it
        }
        if (session.Live is { } live && Setup() is { } setup && live.MatchId == setup.MatchId)
        {
            Show(live.Snapshot, setup.Seats, setup.Course, setup.Laps, null, watching: true);
        }
        else if (lobby is { Stage: LobbyState.Racing, Race: { } race } && _last is null)
        {
            Course = CourseNames.Name(race.Course);
            Stage = session.Racing ? "Racing" : "Watching";
            Map = Theme.GameSkin.Course(race.Course);
            Waiting = true;
        }
    }

    private (string MatchId, IReadOnlyList<SeatSetup> Seats, int Course, int Laps)? Setup()
    {
        if (_session?.Lobby?.Race is { } race)
        {
            return (race.MatchId, race.Seats, race.Course, race.Laps);
        }
        if (_session?.Race?.Start is { Setup: { } setup } start)
        {
            return (start.MatchId, setup.Seats, setup.Course, setup.Laps);
        }
        return null;
    }

    /// <summary>A match the page has not shown yet: the last race's course, map, racers and standings go, and the
    /// new course is shown waiting for its first report. Without this, a player loading into a race sees the course of
    /// the race before until their game reports.</summary>
    private void Begin((string MatchId, IReadOnlyList<SeatSetup> Seats, int Course, int Laps) setup)
    {
        if (setup.MatchId == _shownMatch)
        {
            return;
        }
        _shownMatch = setup.MatchId;
        _last = null;
        _shown = 0;
        Followed = null;
        Course = CourseNames.Name(setup.Course);
        Map = Theme.GameSkin.Course(setup.Course);
        Stage = _session?.Racing == true ? "Racing" : "Watching";
        MapRacers = [];
        Standings.Clear();
        LapText = "";
        TimeText = "";
        HasYou = false;
        Waiting = true;
    }

    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",

        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    private void Show(RaceSnapshot snapshot, IReadOnlyList<SeatSetup> seats, int course, int laps, int? mySeat, bool watching)
    {
        _last = snapshot;
        Waiting = false;
        var racers = snapshot.Racers;
        // the map takes every snapshot (60 a second from the player's own game); the numbers and standings 10 times a
        // second, as fast as they can be read, and the last one when the race is over
        MapRacers = [.. racers.Select(r =>
        {
            var s = r.Seat < seats.Count ? seats[r.Seat] : null;
            var c = SeatColor(s);
            return new MapRacer(r.Seat, new Vector3(r.X, r.Y, r.Z), Color.FromRgb(c.R, c.G, c.B), r.Seat == mySeat,
                r.State == RacerLive.Out, r.State == RacerLive.Finished, r.Rank, RacerName(s, r.Seat));
        })];
        var over = racers.Count > 0 && racers.All(r => r.State != RacerLive.Racing);
        var now = Environment.TickCount64;
        if (Followed is { } followed)
        {
            // knocked out or gone: the map holds its last place a moment, then shows the whole course again (its
            // wreck is no longer in the race, and there is nothing more of it to watch)
            if (racers.FirstOrDefault(r => r.Seat == followed) is { State: not RacerLive.Out })
            {
                _lostAt = null;
            }
            else if (_lostAt is not { } lost)
            {
                _lostAt = now;
            }
            else if (now - lost > LostMs)
            {
                Followed = null;
            }
        }
        if (now - _shown < 100 && !over)
        {
            return;
        }
        _shown = now;
        Course = CourseNames.Name(course);
        Map = Theme.GameSkin.Course(course);
        Stage = over ? "Results" : watching ? "Watching" : "Racing";
        // the race's order from distances and finish times (the game's ranking byte is not the position)
        var ordered = RaceStandings.Order(racers);
        var places = RaceStandings.Places(racers);
        var leader = ordered.FirstOrDefault(r => r.State != RacerLive.Out);
        LapText = leader is null ? "" : $"Lap {Math.Min(leader.Lap + 1, laps)}/{laps}";
        TimeText = Time(racers.Count == 0 ? 0 : racers.Max(r => r.TotalMs));

        var me = mySeat is { } seat ? racers.FirstOrDefault(r => r.Seat == seat) : null;
        HasYou = me is not null;
        if (me is not null)
        {
            YouPosition = places.TryGetValue(me.Seat, out var myPlace) ? ReplayWords.Ordinal(myPlace) : ReplayWords.Out;
            YouOut = me.State == RacerLive.Out;
            YouOf = ReplayWords.Of(racers.Count);
            YouLap = me.State == RacerLive.Finished ? "Finished" : $"Lap {Math.Min(me.Lap + 1, laps)}/{laps}";
            YouLapTime = me.State == RacerLive.Finished ? Time(me.TotalMs) : Time(me.LapMs);
            YouBestLap = me.BestLapMs is { } best ? Time(best) : "-";
            YouSpeed = $"{me.Speed:0}";
            YouEnergy = me.MaxEnergy > 0 ? Math.Clamp(me.Energy / me.MaxEnergy, 0, 1) : 0;
        }

        // standings: finished by time, racing by distance, then the knocked out (furthest first)
        // only the lines that changed, not every line rebuilt 10 times a second
        var open = GamesOpen();
        var place = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            var s = r.Seat < seats.Count ? seats[r.Seat] : null;
            var detail = r.State switch
            {
                RacerLive.Finished => Time(r.TotalMs),
                RacerLive.Out => "KO",
                _ => $"Lap {Math.Min(r.Lap + 1, laps)}",
            };
            var row = new StandingRow(r.Seat, r.State == RacerLive.Out ? "-" : (++place).ToString(CultureInfo.InvariantCulture),
                RacerName(s, r.Seat), MachineName(s),
                Rgb.Hex(SeatColor(s)), detail, r.BestLapMs is { } b ? Time(b) : "", r.Seat == mySeat, r.State == RacerLive.Out,
                r.Seat == Followed, Phase(r, s, open), ModeTeams.Said(RaceTeams(), r.Seat));
            if (i == Standings.Count)
            {
                Standings.Add(row);
            }
            else if (Standings[i] != row)
            {
                Standings[i] = row;
            }
        }
        while (Standings.Count > ordered.Count)
        {
            Standings.RemoveAt(Standings.Count - 1);
        }
    }

    /// <summary>Each seat's team in the race the page shows, from the lobby's race or the player's own match start.</summary>
    private IReadOnlyList<int>? RaceTeams() => _session?.Lobby?.Race?.Teams ?? _session?.Race?.Start?.Setup.Mode?.Teams;

    private static string RacerName(SeatSetup? s, int seat) => s?.Name ?? $"Racer {seat + 1}";

    /// <summary>A seat's machine as the standings write it: the custom machine the seat races (named by the game's rule,
    /// <see cref="Theme.GameSkin.NameOf"/>), else its stock machine.</summary>
    public static string MachineName(SeatSetup? s) =>
        Theme.GameSkin.NameOf(new RacedMachine(Math.Clamp(s?.Machine ?? 0, 0, Machines.Names.Count - 1), s?.Custom));

    /// <summary>The players of the lobby whose game is still open (the server's Racing: set at the race's start,
    /// cleared when their launcher says the game closed); null: no lobby to tell.</summary>
    private HashSet<string>? GamesOpen() =>
        _session?.Lobby?.Players is { } players ? [.. players.Where(p => p.Racing).Select(p => p.Id)] : null;

    /// <summary>Where a player is once their race is run (finished or out): "Game open" while their game still shows
    /// the race or its results, "In lobby" once it closed; nothing for a racer still racing, a CPU, or no lobby.</summary>
    public static string Phase(RacerLive r, SeatSetup? s, HashSet<string>? open) =>
        r.State == RacerLive.Racing || s?.PlayerId is not { } id || open is null ? ""
        : open.Contains(id) ? "Game open" : "In lobby";

    /// <summary>A seat's colour: its stock colour's swatch, its painted colour, or (a CPU) its machine's own.</summary>
    private static Rgb SeatColor(SeatSetup? s)
    {
        if (s is null)
        {
            return new Rgb(140, 150, 170);
        }
        var swatches = Theme.GameSkin.StockSwatches(s.Machine);
        return s.StockColor is { } k && swatches is { } list ? list[Math.Clamp(k, 0, 3)]
            : s.Color ?? swatches?[0] ?? new Rgb(140, 150, 170);
    }

    /// <summary>A race's time in milliseconds the game's way, 1'23"456 (<see cref="ReplayWords.Clock"/>).</summary>
    private static string Time(int ms) => ReplayWords.Clock(TimeSpan.FromMilliseconds(ms));

}

/// <summary>A line of the standings: its racer's seat (which line is which across refreshes), and whether the map
/// follows them.</summary>
public sealed record StandingRow(int Seat, string Rank, string Name, string Machine, string ColorHex, string Detail,
    string Best, bool You, bool Out, bool Followed, string Phase = "", string Team = "")
{
    /// <summary>The race is a mode's with teams.</summary>
    public bool HasTeam => Team.Length > 0;

    /// <summary>The player's race is run and their game is still open: the lobby waits for it.</summary>
    public bool GameOpen => Phase == "Game open";
}
