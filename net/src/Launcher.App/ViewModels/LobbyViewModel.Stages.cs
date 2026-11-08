using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>The lobby's stages as the page shows them: the line of steps with its clock, the course vote with each
/// option's votes and chance, and the draw: a frame that hops from card to card and stops on the course drawn.</summary>
public sealed partial class LobbyViewModel
{
    private int _draws;   // counts draws begun and ended from outside, so a running one knows it is the last

    /// <summary>The clock the draw runs on (<see cref="DrawAsync"/>): the system's; a test's own, so that it sees
    /// every step of the frame however slowly its window's thread is let work (a draw on the system's clock goes
    /// where the time says and passes over the steps it had no turn for).</summary>
    public TimeProvider DrawClock { get; set; } = TimeProvider.System;

    /// <summary>The vote's cards in their two rows of three (the card drawn widens in its row).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<IReadOnlyList<CourseOption>> CourseRows { get; set; } = [];

    /// <summary>Where the draw stands on its strip (<see cref="CourseDraw"/>: the options side by side, each as wide
    /// as its chance), 0 to 1; not a number while there is no draw. The card under it carries the frame.</summary>
    [ObservableProperty]
    public partial double DrawPointer { get; set; } = double.NaN;

    /// <summary>Beside the line, while the lobby's race waits for its race server to be started: that, in a word
    /// or two.</summary>
    [ObservableProperty]
    public partial string StepNote { get; set; } = "";

    /// <summary>The line: this server's steps, the one the lobby is at marked, the ones before it passed.</summary>
    private void ShowSteps(QuickPlaySession session, LobbyState? lobby)
    {
        // (a custom lobby on a fixed or random course, its host's or its mode's, has no vote: the service's way)
        var votes = lobby is { Custom: true }
            ? CustomRules.Raced(lobby.Rules, HostRules.CourseSelect.Name, session.QuickPlayRules) == (int)CourseSelectMode.Vote
            : session.Rules is null or { CourseSelect: CourseSelectMode.Vote };
        // (the race runs once a game has reported it: until then the games are being started)
        var step = LobbyScreens.Step(lobby, votes || session.CourseVote is not null, !Race.Waiting);
        var steps = LobbyScreens.Steps(votes || step is LobbyStep.Vote)
            .Select(s => new StepMark(LobbyScreens.Name(s), s == step, s < step)).ToList();
        if (!Steps.SequenceEqual(steps))
        {
            Steps = steps;
        }
        Step = step;
        PackText = LobbyScreens.PackLabel(lobby?.StatPack);
        PackTip = LobbyScreens.PackTip(lobby?.StatPack, session.LobbyStatPack);
        // (a refused stat pack's sentence is the page's notice, which has the page's width and wraps: beside the
        // steps a sentence would be cut)
        StepNote = lobby is null ? session.Status
            : lobby.Waiting == LobbyState.WaitingForRelay ? "Starting race server"
            : !session.Racing && lobby.Stage is LobbyState.Choosing or LobbyState.Racing && !ChoiceShown ? "Watching" : "";
        ClockText = step switch
        {
            LobbyStep.Gathering when session.StartsAt is { } starts => Minutes(starts),
            LobbyStep.Vote when session is { CourseVote: { } vote, CourseChosen: null } => Minutes(vote.Ends),
            LobbyStep.Machines when session is { ChoiceOpen: true, Choice: { } choice } => Minutes(choice.Ends),
            _ => "",
        };
    }

    /// <summary>Under the steps once the race is known: its mode, course and laps (<see cref="RaceLine"/>); null
    /// before (gathering, the vote until its draw has landed) and where nothing names the race's mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLineShown), nameof(ModeLineRoom))]
    public partial RaceLine? ModeLine { get; set; }

    public bool ModeLineShown => ModeLine is not null;

    /// <summary>The line's room is kept all through the vote, so that nothing moves when it eases in at the draw's
    /// end.</summary>
    public bool ModeLineRoom => ModeLineShown || VoteShown;

    partial void OnDrawFinishedChanged(bool value)
    {
        if (_session is { } session)
        {
            ShowRaceLine(session, session.Lobby);
        }
    }

    // the last match start's mode package read (a tick reads the line four times a second)
    private (string? Hash, ModePackage? Mode) _racedMode;

    /// <summary>The line under the steps: from the card drawn (once the draw has landed: before, it would tell the
    /// draw's end), else the race's own mode package (the player's match start, a watcher's too), else a custom
    /// lobby's rules (a fixed course: no vote). The course and laps: the draw's, else the machine stage's and the
    /// race's.</summary>
    private void ShowRaceLine(QuickPlaySession session, LobbyState? lobby)
    {
        var line = lobby?.Stage is LobbyState.Choosing or LobbyState.Racing || (lobby?.Stage == LobbyState.Starting && DrawFinished)
            ? RaceLineOf(session, lobby!) : null;
        if (line != ModeLine)
        {
            ModeLine = line;
        }
        if (line?.Laps is { } laps)
        {
            // (the machine stage's band says the race's laps too: the same as the line)
            LapsText = laps > 0 ? $"{laps} {(laps == 1 ? "lap" : "laps")}" : "";
        }
    }

    private RaceLine? RaceLineOf(QuickPlaySession session, LobbyState lobby)
    {
        var start = session.Race?.Start;
        var race = lobby.Race;
        if (DrawnCard(session, race) is { } drawn)
        {
            return RaceLine.OfCard(drawn.Card, drawn.Course);
        }
        var course = session.CourseChosen?.Course ?? session.Choice?.Choose.Course ?? race?.Course ?? start?.Setup.Course;
        if (course is not { } c)
        {
            return null;
        }
        int? laps = race?.Laps ?? start?.Setup.Laps;
        if (start?.Setup.Mode is { } mode && (race is null || race.MatchId == start.MatchId) && Package(mode) is { } package)
        {
            return RaceLine.OfPackage(package, c, laps);
        }
        if (lobby.Custom)
        {
            return RaceLine.OfCustom(lobby.Rules, session.QuickPlayRules, c, laps ?? CustomRules.Raced(lobby.Rules, HostRules.Laps.Name, session.QuickPlayRules));
        }
        if (session.CourseVote is null && session.Rules is { CourseSelect: not CourseSelectMode.Vote } rules)
        {
            // a Quick Play server without a vote (no modes): its own laps
            return RaceLine.Plain(c, laps ?? rules.Laps);
        }
        return null;   // (a player who came in after the vote: the race's mode is named nowhere)
    }

    /// <summary>The card drawn for this match, and its course: the vote's result's card, else the vote's card for the
    /// option drawn; null without a vote of cards, or for a race the result is not this one's.</summary>
    private static (VoteCard Card, int Course)? DrawnCard(QuickPlaySession session, LobbyRace? race)
    {
        if (session.CourseChosen is not { } chosen || (race is not null && race.MatchId != chosen.MatchId))
        {
            return null;
        }
        var card = chosen.Card ?? (session.CourseVote?.Vote is { } vote && vote.MatchId == chosen.MatchId && vote.Cards is { } cards &&
            chosen.Winner < cards.Count ? cards[chosen.Winner] : null);
        return card is null ? null : (card, chosen.Course);
    }

    private ModePackage? Package(ModeStart mode)
    {
        if (_racedMode.Hash != mode.Hash)
        {
            ModePackage? read;
            try
            {
                read = ModePackage.Read(Convert.FromBase64String(mode.Package));
            }
            catch (Exception e) when (e is FormatException or InvalidDataException or ArgumentException or EndOfStreamException or InvalidOperationException)
            {
                read = null;
            }
            _racedMode = (mode.Hash, read);
        }
        return _racedMode.Mode;
    }

    /// <summary>What is left, the series' way: 0'29".</summary>
    private static string Minutes(DateTimeOffset ends) => ReplayWords.Seconds(SecondsLeft(ends));

    /// <summary>The countdown runs while the lobby counts down to its start, the vote is open or the machines are
    /// being chosen.</summary>
    private void Clock(QuickPlaySession session)
    {
        var ticking = session is { CourseVote: not null, CourseChosen: null } || session.Choice is not null ||
            session.StartsAt is not null;
        if (ticking && !_clock.IsEnabled)
        {
            _clock.Start();
        }
        else if (!ticking && _clock.IsEnabled)
        {
            _clock.Stop();
        }
    }

    private void ShowVote(QuickPlaySession session)
    {
        if (session.CourseVote is not { } vote)
        {
            VoteShown = false;
            return;
        }
        if (_voteMatch != vote.Vote.MatchId)
        {
            _voteMatch = vote.Vote.MatchId;
            _draws++;   // (a draw of the match before, still running, is over)
            DrawFinished = false;
            DrawPointer = double.NaN;
            CourseOptions.Clear();
            foreach (var (course, i) in vote.Vote.Options.Select((c, i) => (c, i)))
            {
                var option = new CourseOption(i, course == CourseVote.RandomCourse ? "Random course" : Courses.Name(course), course);
                option.Show(vote.Vote.Cards?[i]);   // (a lobby of game modes: the card's laps, a spicy mode's badge)
                CourseOptions.Add(option);
            }
            CourseRows = [.. CourseOptions.Chunk(3).Select(row => (IReadOnlyList<CourseOption>)row)];
            if (!Garage.MidEdit)
            {
                GarageShown = false;   // the vote is the lobby's face (a change not saved in the builder keeps its page)
            }
            Moment?.Invoke(LobbyMoment.CourseVote);
        }
        VoteShown = true;
        // the votes as they stand; once the course is drawn, the result's. Each option's chance is its share of them
        // (the draw has one ticket a vote; every option alike while nobody has voted)
        var counts = session.CourseChosen?.Votes ?? vote.Standing?.Votes;
        var chances = CourseDraw.Chances(counts ?? [.. CourseOptions.Select(_ => 0)]);
        foreach (var o in CourseOptions)
        {
            o.IsMine = vote.Mine == o.Index;
            var n = counts is not null && o.Index < counts.Count ? counts[o.Index] : 0;
            o.Counted = counts is not null;
            o.Votes = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            o.Chance = o.Index < chances.Count ? CourseDraw.Percent(chances[o.Index]) : "";
            o.Voted = n > 0;
            // a pip a vote, the player's own first and in green
            if (o.Pips.Count != n || (o.Pips.Count > 0 && o.Pips[0].Mine != o.IsMine))
            {
                o.Pips = [.. Enumerable.Range(0, n).Select(k => new VotePip(k == 0 && o.IsMine))];
            }
        }
        if (session.CourseChosen is { } chosen && _drawnMatch != chosen.MatchId)
        {
            _drawnMatch = chosen.MatchId;
            _ = DrawAsync(chosen);
        }
    }

    private void Tick()
    {
        if (_session is { } session)
        {
            ShowSteps(session, session.Lobby);
            ShowRaceLine(session, session.Lobby);
            Buzzer(session);
        }
    }

    private static double SecondsExactlyLeft(DateTimeOffset ends) => (ends - DateTimeOffset.UtcNow).TotalSeconds;

    private static int SecondsLeft(DateTimeOffset ends) => Math.Max(0, (int)Math.Ceiling(SecondsExactlyLeft(ends)));

    /// <summary>The draw, as the lottery it is (<see cref="CourseDraw"/>): a frame steps through the votes of the
    /// courses that can be drawn (Overwatch's way: a course with three votes is three steps, each ticking and
    /// lighting that vote's pip), ever slower, and stops on the course the server drew, which widens while the
    /// others step back. It stands on a card as long as that course's chance (the strip's pointer under it runs
    /// over parts as wide as the chances), so a course with three votes is framed three times as long as one with
    /// one.</summary>
    private async Task DrawAsync(CourseChosen chosen)
    {
        var draw = ++_draws;
        var parts = CourseDraw.Parts(chosen.Votes);
        var landing = CourseDraw.Landing(parts, chosen.Winner, StringComparer.Ordinal.GetHashCode(chosen.MatchId) ^ chosen.Course);
        var clock = DrawClock;
        var started = clock.GetUtcNow();
        _frame = new DrawFrame();
        while (draw == _draws)   // (a draw ended from outside leaves the frame and its ticks to the next one)
        {
            var t = (clock.GetUtcNow() - started).TotalMilliseconds / CourseDraw.RunMs;
            DrawPointer = CourseDraw.Position(landing, t);
            Light(CourseDraw.Step(parts, DrawPointer));
            if (t >= 1)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(16), clock).ConfigureAwait(true);
        }
        if (draw != _draws)
        {
            return;   // another match's vote has opened since, or the session is over
        }
        Light(null);
        Sounded?.Invoke(LauncherSound.CourseDrawn);
        if (CourseOptions.FirstOrDefault(o => o.Index == chosen.Winner) is { } won)
        {
            won.IsWon = true;
            // "Random course" won: the card turns into the course it drew
            won.Course = chosen.Course;
            won.Name = Courses.Name(chosen.Course);
            if (chosen.Card is { } card)
            {
                won.Show(card);   // (it may have drawn a spicy option)
            }
            foreach (var o in CourseOptions)
            {
                o.IsLost = o != won;
            }
        }
        DrawFinished = true;
    }

    /// <summary>The step the draw's frame is on.</summary>
    private DrawFrame _frame = new();

    /// <summary>The draw's frame on the step (null: off every card), its vote's pip lit; every step blips, two votes
    /// of one card two blips (<see cref="Sounded"/>: <see cref="DrawSounds"/>, at the steps' own pace, slowing as
    /// the draw does; at most one a frame).</summary>
    private void Light(DrawStep? step)
    {
        if (_frame.Move(step))
        {
            Sounded?.Invoke(LauncherSound.DrawHop);
        }
        foreach (var o in CourseOptions)
        {
            o.IsLit = o.Index == step?.Option;
            var pip = o.IsLit ? step!.Vote : -1;
            if (o.Pips.Select((p, k) => p.Lit != (k == pip)).Any(changed => changed))
            {
                o.Pips = [.. o.Pips.Select((p, k) => p with { Lit = k == pip })];
            }
        }
    }
}
