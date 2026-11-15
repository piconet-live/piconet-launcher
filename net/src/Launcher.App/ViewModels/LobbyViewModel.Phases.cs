using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>The lobby as the redesigned interface shows it (Views/NextUi): one calm page a phase, in place of the
/// classic page's one page with its line of steps.</summary>
public enum LobbyPhase
{
    /// <summary>In the queue, or connecting: no lobby yet.</summary>
    Finding,

    /// <summary>The lobby gathers: its racers, the vote to start, the vote on CPUs.</summary>
    Gather,

    /// <summary>The course vote and its draw.</summary>
    Vote,

    /// <summary>The player chooses their machine.</summary>
    Machine,

    /// <summary>The race is being started (its race server, then the games), or the stage is another's.</summary>
    Starting,

    /// <summary>The race as it runs, raced or watched.</summary>
    Race,

    /// <summary>The race is over: the player's place and the podium.</summary>
    Results,

    /// <summary>A custom lobby while it gathers: its room (the host's rules, the start, a racer removed).</summary>
    Room,
}

/// <summary>
/// What the redesigned interface's lobby pages read (Views/NextUi/QuickPlayView and its phases): which phase the
/// lobby is in for this player, and the same state as the classic page's in the few words and shares those pages
/// show. Nothing here decides anything: the votes, the choice and the race are the classic page's, above.
/// </summary>
public sealed partial class LobbyViewModel
{
    private DateTimeOffset _clockEnds;
    private double _clockFull;
    private QuickPlaySession? _phaseSession;
    private int? _courseNow;

    /// <summary>The page the redesigned interface shows for the lobby now.</summary>
    [ObservableProperty]
    public partial LobbyPhase Phase { get; set; }

    /// <summary>The racers present, in the order they joined (no empty seats): a tile each while the lobby gathers,
    /// a mark each under the vote, a row each in the lobby's panel.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SeatRow> People { get; set; } = [];

    /// <summary>Under "Waiting for racers": what starts the race, in a sentence.</summary>
    [ObservableProperty]
    public partial string GatherLine { get; set; } = "";

    /// <summary>The start's vote as a line ("Start votes 2 of 3"; before it opens, "Racers 2 of 4").</summary>
    [ObservableProperty]
    public partial string StartLine { get; set; } = "";

    /// <summary>The start's vote as a share, 0 to 1 (the votes of the votes needed; before it opens, the racers of
    /// the racers it opens with).</summary>
    [ObservableProperty]
    public partial double StartShare { get; set; }

    /// <summary>What is left of the clock that runs (the course vote, the machine stage), 1 down to 0; 0 while none
    /// runs. The whole is what was left when the page first saw the clock.</summary>
    [ObservableProperty]
    public partial double ClockShare { get; set; }

    /// <summary>The clock is in its last five seconds.</summary>
    [ObservableProperty]
    public partial bool ClockEnding { get; set; }

    /// <summary>The course of the race that is next or runs, where the lobby knows it: "Mute City" over "Twist Road".</summary>
    [ObservableProperty]
    public partial string NextVenue { get; set; } = "";

    [ObservableProperty]
    public partial string NextTrack { get; set; } = "";

    /// <summary>Under the course: the laps and the racers ("3 laps · 6 racers").</summary>
    [ObservableProperty]
    public partial string NextLine { get; set; } = "";

    /// <summary>What the lobby waits for while a race is being started, in a line.</summary>
    [ObservableProperty]
    public partial string StartingLine { get; set; } = "";

    /// <summary>Over the vote: what to do, then that the draw runs, then the course drawn.</summary>
    [ObservableProperty]
    public partial string VoteTitle { get; set; } = "";

    [ObservableProperty]
    public partial string VoteLine { get; set; } = "";

    /// <summary>The vote's clock shows: the vote is open (not during the draw, nor after it).</summary>
    [ObservableProperty]
    public partial bool VoteClockShown { get; set; }

    /// <summary>The first three of the race's standings: the leaders while it runs, the podium once it is over.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StandingRow> Leaders { get; set; } = [];

    /// <summary>Who leads the race (a watcher's headline); "" before the games report.</summary>
    [ObservableProperty]
    public partial string LeaderName { get; set; } = "";

    /// <summary>The race's lap as a share of its laps, 0 to 1 (the player's own; a watcher's: the leader's).</summary>
    [ObservableProperty]
    public partial double LapShare { get; set; }

    /// <summary>The lap in words: "Lap 2 / 3", "Finished".</summary>
    [ObservableProperty]
    public partial string LapWords { get; set; } = "";

    /// <summary>The player voted to start in this lobby's gathering (its own tile says "Wants to start"). The server
    /// counts the votes and names nobody, so only the player's own is known: it is this launcher's own word, until the
    /// lobby moves on or the player is in another.</summary>
    [ObservableProperty]
    public partial bool YouWantToStart { get; set; }

    /// <summary>The course the page stands on, by the game's number, once the lobby has one (the course drawn, the
    /// race being started, the race and its result): the redesigned interface shows its picture from the disc behind
    /// the page, darkened, in place of the flag. -1: none (the gathering, the vote before its draw, the machine stage).</summary>
    [ObservableProperty]
    public partial int BackdropCourse { get; set; } = -1;

    /// <summary>The player's own seat while they may change its team (a lobby with teams, while it forms); null: none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TeamShown))]
    public partial SeatRow? MyTeam { get; set; }

    public bool TeamShown => MyTeam is not null;

    /// <summary>The race page shows the whole course with every racer on it.</summary>
    [ObservableProperty]
    public partial bool MapShown { get; set; }

    /// <summary>"Whole course" and "Hide map".</summary>
    [RelayCommand]
    private void ToggleMap() => MapShown = !MapShown;

    /// <summary>The machine stage shows the whole Garage (opened from its page: "Open the Garage"); Back closes it.</summary>
    [ObservableProperty]
    public partial bool FullGarage { get; set; }

    /// <summary>"Open the Garage" on the machine stage's page: every machine, its colours and its setting.</summary>
    [RelayCommand]
    private void OpenFullGarage() => FullGarage = true;

    /// <summary>The page hears what changes its phase between two of the session's own reports: the step (the clock
    /// ticks it), the race's first report and its end.</summary>
    private void WatchPhase()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Step) or nameof(ChoiceShown) or nameof(ClockText) or nameof(DrawFinished))
            {
                ShowPhase();
            }
        };
        Race.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RaceViewModel.Stage) or nameof(RaceViewModel.Waiting))
            {
                ShowPhase();
            }
            else if (e.PropertyName is nameof(RaceViewModel.YouLap) or nameof(RaceViewModel.LapText))
            {
                ShowLap();
            }
        };
        Race.Standings.CollectionChanged += (_, _) => ShowLeaders();
    }

    private void ShowLeaders()
    {
        var leaders = Race.Standings.Take(3).ToList();
        if (!Leaders.SequenceEqual(leaders))
        {
            Leaders = leaders;
        }
        LeaderName = leaders.Count > 0 ? leaders[0].Name : "";
    }

    /// <summary>The lap from the race's own words ("Lap 2/3", "Finished"): the player's, else the leader's.</summary>
    private void ShowLap()
    {
        var text = Race.HasYou && Race.YouLap.Length > 0 ? Race.YouLap : Race.LapText;
        (LapWords, LapShare) = LapOf(text);
    }

    /// <summary>"Lap 2/3" as its words and its share: ("Lap 2 / 3", 2/3); "Finished": all of it; anything else as it is.</summary>
    public static (string Words, double Share) LapOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (text.StartsWith("Lap ", StringComparison.Ordinal) && slash > 4
            && int.TryParse(text.AsSpan(4, slash - 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var lap)
            && int.TryParse(text.AsSpan(slash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var laps))
        {
            return ($"Lap {lap} / {laps}", Share(lap, laps));
        }
        return (text, text == "Finished" ? 1 : 0);
    }

    private void ShowPhase()
    {
        if (_session is not { } session)
        {
            return;
        }
        var lobby = session.Lobby;
        Phase = PhaseOf(lobby);
        if (Phase != LobbyPhase.Machine)
        {
            FullGarage = false;
        }
        if (session != _phaseSession || Phase != LobbyPhase.Gather)
        {
            YouWantToStart = false;   // another lobby, or this one moved on: the vote is spent
            _phaseSession = session;
        }
        var people = SeatColumns.SelectMany(c => c.Seats).Where(s => !s.Open).ToList();
        if (!People.SequenceEqual(people))
        {
            People = people;
        }
        MyTeam = people.FirstOrDefault(p => p is { You: true, TeamButton: true });
        ShowStart(session, lobby);
        ShowClock(session);
        ShowNext(session, lobby);
        ShowVoteWords(session);
        ShowLap();
        ShowLeaders();
        BackdropCourse = _courseNow is { } course && (Phase is LobbyPhase.Starting or LobbyPhase.Race or LobbyPhase.Results
            || (Phase == LobbyPhase.Vote && session.CourseChosen is not null && DrawFinished)) ? course : -1;
    }

    private void ShowVoteWords(QuickPlaySession session)
    {
        (VoteTitle, VoteLine, VoteClockShown) = session.CourseChosen is null
            ? ("Vote for the next course", Watching ? "You watch this race: its racers vote." : "More votes, better odds.", true)
            : !DrawFinished ? ("Drawing the course", "Every vote is one ticket in the draw.", false)
            : (NextTrack, NextVenue.Length > 0 ? $"{NextVenue}. Next: your machine." : "Next: your machine.", false);
    }

    private LobbyPhase PhaseOf(LobbyState? lobby)
    {
        if (lobby is null)
        {
            return LobbyPhase.Finding;
        }
        return Step switch
        {
            LobbyStep.Gathering => lobby.Custom ? LobbyPhase.Room : LobbyPhase.Gather,
            LobbyStep.Vote => LobbyPhase.Vote,
            // (the stage is open for this player; for one who watches, or came in after it opened, it is a wait)
            LobbyStep.Machines => ChoiceShown ? LobbyPhase.Machine : LobbyPhase.Starting,
            LobbyStep.Starting => LobbyPhase.Starting,
            _ => Race.Stage == "Results" ? LobbyPhase.Results : LobbyPhase.Race,
        };
    }

    private void ShowStart(QuickPlaySession session, LobbyState? lobby)
    {
        if (lobby is null)
        {
            (StartLine, StartShare, GatherLine) = ("", 0, "");
            return;
        }
        var needed = LobbyScreens.StartVotesNeeded(lobby);
        var racers = LobbyScreens.Racers(lobby);
        (StartLine, StartShare) = lobby.VotingOpen
            ? ($"Start votes {lobby.StartVotes} of {needed}", Share(lobby.StartVotes, needed))
            : ($"Racers {racers} of {lobby.VotingMinimum}", Share(racers, lobby.VotingMinimum));
        GatherLine = session.StartsAt is not null ? "The race is about to start."
            : lobby.VotingOpen ? "The race starts when more than half of the racers vote to start."
            : $"The vote to start opens with {lobby.VotingMinimum} racers.";
    }

    private static double Share(int part, int whole) => whole <= 0 ? 0 : Math.Clamp(part / (double)whole, 0, 1);

    /// <summary>The clock that runs as a share of itself: the course vote's until the course is drawn, the machine
    /// stage's while it is open.</summary>
    private void ShowClock(QuickPlaySession session)
    {
        DateTimeOffset? ends = Step switch
        {
            LobbyStep.Vote when session is { CourseVote: { } vote, CourseChosen: null } => vote.Ends,
            LobbyStep.Machines when session is { ChoiceOpen: true, Choice: { } choice } => choice.Ends,
            _ => null,
        };
        if (ends is not { } at)
        {
            (_clockEnds, _clockFull, ClockShare, ClockEnding) = (default, 0, 0, false);
            return;
        }
        var left = Math.Max(0, SecondsExactlyLeft(at));
        if (at != _clockEnds || left > _clockFull)
        {
            (_clockEnds, _clockFull) = (at, left);
        }
        ClockShare = _clockFull <= 0 ? 0 : Math.Clamp(left / _clockFull, 0, 1);
        ClockEnding = left <= 5;
    }

    /// <summary>The race that is next or runs, as far as the lobby knows it: the course drawn or chosen, the laps,
    /// the racers; and what it waits for.</summary>
    private void ShowNext(QuickPlaySession session, LobbyState? lobby)
    {
        var course = session.CourseChosen?.Course ?? session.Choice?.Choose.Course ?? lobby?.Race?.Course ?? session.Race?.Start.Setup.Course;
        _courseNow = course is >= 0 ? course : null;   // (a random course before its draw has no picture)
        if (course is { } c)
        {
            var name = Courses.Name(c);
            var colon = name.IndexOf(": ", StringComparison.Ordinal);
            (NextVenue, NextTrack) = colon < 0 ? ("", name) : (name[..colon], name[(colon + 2)..]);
        }
        else
        {
            (NextVenue, NextTrack) = ("", "");
        }
        var racers = lobby is null ? 0 : lobby.Players.Count(p => !p.Watching && !p.NextRace);
        var parts = new List<string>();
        if (ModeLine is { Badged: true } mode)
        {
            parts.Add(mode.Title);
        }
        if (LapsText.Length > 0)
        {
            parts.Add(LapsText);
        }
        if (racers > 0)
        {
            parts.Add(racers == 1 ? "1 racer" : $"{racers} racers");
        }
        NextLine = string.Join(" · ", parts);
        StartingLine = StepNote.Length > 0 ? StepNote : lobby?.Stage == LobbyState.Racing ? "Starting F-Zero GX" : "Waiting for the other racers";
    }
}
