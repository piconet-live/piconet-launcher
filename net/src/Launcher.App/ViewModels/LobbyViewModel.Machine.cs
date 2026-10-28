using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The lobby's machine stage, which is the Garage: the machine in 3D, its colour, the stats
/// and the order, under the lobby's line, with the course drawn, who is ready, and Ready. The Garage is open all
/// through the lobby (<see cref="LobbyScreens.GarageOpen"/>), and the machine selected in it is the one the player
/// races. What the page shows is what races, with no new message:
/// <list type="bullet">
/// <item>until the player is ready nothing is sent; Ready sends the machine selected in its look; for a player who
/// never presses it, it is sent as the stage's clock runs out (<see cref="BuzzerLead"/> before it; the server on its
/// own would race the machine queued with);</item>
/// <item>once ready, the player stays ready: every pick and every change of colour or setting is sent at once (a
/// colour being dragged, at most every <see cref="LookSendGap"/>);</item>
/// <item>the server closes the stage when every racer is ready, and says nothing of a choice that comes after: so
/// the stage is closed here the moment the lobby says it waits for its race server or the race is on, nothing more
/// is sent, and the machine selected and shown is put to the one the server has for this player
/// (<see cref="Raced"/>).</item>
/// </list>
/// </summary>
public sealed partial class LobbyViewModel
{
    /// <summary>How long before the machine stage's end the selected machine is sent for a player who is not ready:
    /// room for a clock that started late (the server's message a second on its way) and a window that stalled.</summary>
    internal static readonly TimeSpan BuzzerLead = TimeSpan.FromSeconds(3);

    /// <summary>The least time between two sends of a look that is being changed (a colour dragged over the wheel):
    /// a server counts a connection's messages.</summary>
    internal static readonly TimeSpan LookSendGap = TimeSpan.FromMilliseconds(100);

    private QuickPlaySession? _garageFor;
    private string? _buzzedMatch;
    private string? _raceMatch;
    private string? _settledMatch;
    private DateTimeOffset _lookSent;
    private bool _lookWaiting;

    /// <summary>The page shows the Garage (the machine stage; or opened by the player), under the lobby's line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RosterShown), nameof(LobbyShown), nameof(FaceText))]
    public partial bool GarageShown { get; set; }

    /// <summary>The page shows the lobby's own part (the votes, the racers, the race).</summary>
    public bool LobbyShown => !GarageShown;

    /// <summary>The Garage can be opened from the lobby's line (<see cref="LobbyScreens.GarageOpen"/>).</summary>
    [ObservableProperty]
    public partial bool GarageOpen { get; set; }

    /// <summary>The button that turns the page: to the Garage, or back to the lobby.</summary>
    public string FaceText => GarageShown ? "Lobby" : "Garage";

    /// <summary>This player's machine choice is on and open at the server (<see cref="QuickPlaySession.ChoiceOpen"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReady), nameof(IsReady))]
    public partial bool ChoiceShown { get; set; }

    // in the machine stage READY is the Garage's primary: its own (USE THIS MACHINE) gives way
    partial void OnChoiceShownChanged(bool value) => Garage.ReadyIsPrimary = value;

    /// <summary>When the machine stage last opened on this page: for a moment after it, Enter is not Ready (it may
    /// have been meant for the chat's message, whose box the stage's page took away).</summary>
    public DateTimeOffset StageOpenedAt { get; private set; }

    /// <summary>The machine the player is ready with (null until they press Ready).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReady), nameof(IsReady))]
    public partial int? ReadyMachine { get; set; }

    /// <summary>Ready can be pressed: the stage is open and the player is not ready yet, or the Garage's PREVIEW shows
    /// another machine than the one they are ready with (READY is its primary there: ready with this machine).</summary>
    public bool CanReady => ChoiceShown && Garage.MachineIndex >= 0
        && (ReadyMachine is null || (Garage.Previewing && (Garage.CustomShown || ReadyMachine != Garage.MachineIndex)));

    /// <summary>The player is ready, with the machine selected (a machine used after Ready is sent at once).</summary>
    public bool IsReady => ChoiceShown && ReadyMachine is not null && !CanReady;

    /// <summary>This player as the server races them, once it has spoken: while the lobby waits for its race server
    /// with the stage closed, and while the player's race is on. The left column shows this machine in this colour
    /// then, whatever the Garage has selected since. Null otherwise.</summary>
    [ObservableProperty]
    public partial PlayerInfo? Raced { get; set; }

    /// <summary>The course is decided: its picture, name and laps stand on the lobby's line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CourseHasPicture), nameof(ChoiceHasGem))]
    public partial bool CourseShown { get; set; }

    /// <summary>The course of the race being set up, for its picture and name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CourseHasPicture), nameof(ChoiceCup), nameof(ChoiceHasGem))]
    public partial int ChoiceCourse { get; set; }

    /// <summary>The course's cup, for its gem beside the venue on the stage's line ("" for none).</summary>
    public string ChoiceCup => CourseCups.CupOf(ChoiceCourse);

    public bool ChoiceHasGem => CourseShown && ChoiceCup.Length > 0;

    /// <summary>The course drawn has a picture on the disc (a stage of the story has none: its name alone).</summary>
    public bool CourseHasPicture => CourseShown && Theme.GameSkin.Venue(ChoiceCourse) is not null;

    [ObservableProperty]
    public partial string ChoiceCourseName { get; set; } = "";

    [ObservableProperty]
    public partial string ChoiceVenue { get; set; } = "";

    [ObservableProperty]
    public partial string ChoiceTrack { get; set; } = "";

    /// <summary>"3 laps".</summary>
    [ObservableProperty]
    public partial string LapsText { get; set; } = "";

    /// <summary>"3 of 5": the racers who are ready, of the racers.</summary>
    [ObservableProperty]
    public partial string ReadyCount { get; set; } = "";

    /// <summary>Who is ready: every racer of the race by their paint, lit when ready.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReadyMark> ReadyMarks { get; set; } = [];

    /// <summary>The lobby's own face (the vote, the racers), not its Garage: a stage with a decision opening.</summary>
    public void ShowLobbyFace() => GarageShown = false;

    /// <summary>The line's button: the Garage, or back to the lobby.</summary>
    [RelayCommand]
    private void TurnPage() => GarageShown = GarageOpen && !GarageShown;

    /// <summary>Ready: the machine selected in the Garage, in its look, is the one raced.</summary>
    [RelayCommand]
    private async Task ReadyAsync()
    {
        if (_session is { ChoiceOpen: true } session && Garage.MachineIndex >= 0)
        {
            Garage.SaveNow();   // a colour changed a moment ago is part of it
            var ready = session.Choice?.Mine is not null;
            // in PREVIEW, READY is the stage's primary: the machine shown is the one raced, and SELECT again (for a
            // player already ready, the Garage's pick sends it: OnPicked)
            if (Garage.Previewing)
            {
                Garage.UseMachineCommand.Execute(null);   // (raced, SELECT again, and the confirm's sound)
            }
            else
            {
                Garage.Confirm();   // (the confirm's sound)
            }
            if (!ready)
            {
                await session.ChooseMachineAsync(Garage.MachineIndex).ConfigureAwait(true);
            }
        }
    }

    /// <summary>Everything this page remembers of a session's matches, forgotten: a session has ended or another has
    /// begun. A server names its matches m1, m2, ... from its start, so the next server's first match has the name
    /// of one this page has seen: nothing of that one may stand for it (its courses, its draw, its last send).</summary>
    private void Forget()
    {
        (_voteMatch, _drawnMatch, _choiceMatch, _raceMatch, _buzzedMatch, _settledMatch) = (null, null, null, null, null, null);
        _draws++;   // a draw still running is the old session's
        CourseOptions.Clear();
        CourseRows = [];
        (VoteShown, DrawFinished, DrawPointer) = (false, false, double.NaN);
        (ChoiceShown, CourseShown, ReadyMachine, Raced) = (false, false, null, null);
        (ChoiceCourseName, ChoiceVenue, ChoiceTrack, LapsText, ReadyCount, ReadyMarks) = ("", "", "", "", "", []);
        PackText = "";
        PackTip = "";
        Chat.Clear();
        ShowGarage(null);
    }

    /// <summary>The Garage's part in this lobby: what its server offers and fixes, and, joining a lobby, the
    /// player's machine selected (the nearest the server allows, when it does not allow theirs). Out of a lobby, and
    /// with the connection gone, the Garage is the page it is by itself, on the player's own machine.</summary>
    private void ShowGarage(QuickPlaySession? session)
    {
        var inLobby = session is { Lobby: not null, ConnectedTo: not null };
        GarageOpen = LobbyScreens.GarageOpen(inLobby);
        if (!GarageOpen || session is null)
        {
            GarageShown = false;
            Garage.InLobby(null, null, null);
            if (_garageFor is not null)
            {
                _garageFor = null;
                Garage.Select(_state.Settings.Racer.Machine);
            }
            return;
        }
        var offer = session.Choice?.Choose.Machines ?? session.Rules?.AllowedMachines;
        // (the stats shown are the lobby's: its stat pack's over the disc's, while its state names a pack)
        Garage.InLobby(offer, session.MachineSetting, session.LobbyStatPack, LobbyScreens.PackLabel(session.Lobby?.StatPack));
        if (_garageFor != session)
        {
            _garageFor = session;
            Garage.Select(QuickPlaySession.DefaultChoice(_state.Settings.Racer.Machine, session.Machine,
                offer ?? [.. Enumerable.Range(0, Machines.Names.Count)]));
        }
        if (Racing && session.Racing)
        {
            GarageShown = false;   // the player's race is on: its page
        }
    }

    /// <summary>The machine stage: the Garage is the page, on the machine the choice starts on; the course and who
    /// is ready stand on the lobby's line. Closed at the server, it is closed here (<see cref="Settle"/>).</summary>
    private void ShowChoice(QuickPlaySession session)
    {
        var lobby = session.Lobby;
        if (session.Choice is { } choice && _choiceMatch != choice.Choose.MatchId)
        {
            _choiceMatch = choice.Choose.MatchId;
            Garage.Select(choice.Default.Machine);
            ChoiceCourse = choice.Choose.Course;
            ChoiceCourseName = Courses.Name(choice.Choose.Course);
            var colon = ChoiceCourseName.IndexOf(": ", StringComparison.Ordinal);
            (ChoiceVenue, ChoiceTrack) = colon < 0 ? ("", ChoiceCourseName) : (ChoiceCourseName[..colon], ChoiceCourseName[(colon + 2)..]);
            StageOpenedAt = DateTimeOffset.UtcNow;
            GarageShown = true;
            Moment?.Invoke(LobbyMoment.Machines);
        }
        if (session.Choice is null && ChoiceShown)
        {
            GarageShown = false;   // the stage is over: the lobby's page (the race) again
        }
        LapsText = session.Rules is { } rules ? $"{rules.Laps} {(rules.Laps == 1 ? "lap" : "laps")}" : "";
        CourseShown = session.Choice is not null;
        ChoiceShown = session.ChoiceOpen;
        ReadyMachine = session.ChoiceOpen ? session.Choice?.Mine?.Machine : null;
        Settle(session, lobby);
        ShowReady(lobby);
        ShowSeatPacks(session);
    }

    /// <summary>The server has spoken: the stage is closed (the lobby waits for its race server) or the player's
    /// race is on. From then on the player races what the server has for them, which may not be what they picked
    /// last (a pick that crossed the last racer's Ready on its way is dropped there): the Garage's selection and
    /// the player's machine are put to it, once a match, and the page goes back to the lobby's.</summary>
    private void Settle(QuickPlaySession session, LobbyState? lobby)
    {
        var me = session.Me;
        var match = me is null ? null
            : lobby is { Stage: LobbyState.Choosing, Waiting: not null } && session.Choice is { } choice ? choice.Choose.MatchId
            : lobby is { Stage: LobbyState.Racing, Race: { } race } && me.Racing ? race.MatchId
            : null;
        Raced = match is null ? null : me;
        if (match is null || me is null || _settledMatch == match)
        {
            return;
        }
        _settledMatch = match;
        GarageShown = false;
        if (Machines.Has(me.Machine))
        {
            Garage.Select(me.Machine);
            if (_state.Settings.Racer.Machine != me.Machine)
            {
                _state.Update(s => s with { Racer = s.Racer.WithMachine(me.Machine) });
            }
        }
    }

    private void ShowReady(LobbyState? lobby)
    {
        if (lobby is not { Stage: LobbyState.Choosing })
        {
            (ReadyCount, ReadyMarks) = ("", []);
            return;
        }
        // (the racers of this race: a member who joined a race ago and is still out in it chooses nothing)
        // (a watcher is in no race: they choose nothing)
        var racers = lobby.Players.Where(p => !p.Racing && !p.Watching).ToList();
        ReadyCount = $"{racers.Count(p => p.Chosen)} of {racers.Count}";
        var marks = racers.Select(p => new ReadyMark(p.Name, Rgb.Hex(p.Color), p.Chosen, p.Id == _session?.PlayerId)).ToList();
        if (!ReadyMarks.SequenceEqual(marks))
        {
            ReadyMarks = marks;
        }
    }

    /// <summary>The player's race starts. No sound: the game's own start is the sound. The moment is kept for the
    /// match's bookkeeping.</summary>
    private void ShowRaceStart(QuickPlaySession session)
    {
        if (session.Race is { } race && _raceMatch != race.Start.MatchId)
        {
            _raceMatch = race.Start.MatchId;
        }
    }

    /// <summary>A machine picked in the Garage (it is the player's machine already: <see cref="GarageViewModel"/>).
    /// A player who is ready stays ready, with that machine: it is sent at once.</summary>
    private void OnPicked(int machine)
    {
        if (_session is { ChoiceOpen: true, Choice.Mine: not null } session)
        {
            _ = session.ChooseMachineAsync(machine);
        }
    }

    /// <summary>The look of the machine being set up changed. For a player who is ready with it, it is kept and
    /// sent at once, not a moment later: the race may start in that moment.</summary>
    private void OnLookChanged()
    {
        if (_session is not { ChoiceOpen: true, Choice.Mine: { } mine } || mine.Machine != Garage.MachineIndex)
        {
            return;
        }
        var wait = LookSendGap - (DateTimeOffset.UtcNow - _lookSent);
        if (wait <= TimeSpan.Zero)
        {
            SendLookNow();
        }
        else if (!_lookWaiting)
        {
            _lookWaiting = true;
            _ = SendLookLaterAsync(wait);
        }
    }

    private async Task SendLookLaterAsync(TimeSpan wait)
    {
        await Task.Delay(wait).ConfigureAwait(true);
        _lookWaiting = false;
        SendLookNow();
    }

    private void SendLookNow()
    {
        _lookSent = DateTimeOffset.UtcNow;
        Garage.SaveNow();   // (kept in the settings, which the session reads the look from; it tells LookSaved)
        SendLook();
    }

    /// <summary>The look of the machine the player is ready with is another than the one sent: the choice again,
    /// in the look as it is now.</summary>
    private void SendLook()
    {
        if (_session is { ChoiceOpen: true, Choice.Mine: { } mine } session && mine.Machine == Garage.MachineIndex
            && _state.Settings.Racer.LookOf(mine.Machine) != mine)
        {
            _ = session.ChooseMachineAsync(mine.Machine);
        }
    }

    /// <summary>The machine stage's clock is about to run out and the player is not ready: the machine selected is
    /// sent now, once, so that what the Garage shows selected is what they race.</summary>
    private void Buzzer(QuickPlaySession session)
    {
        if (session.Choice is not { } choice || !session.ChoiceOpen || _buzzedMatch == choice.Choose.MatchId
            || SecondsExactlyLeft(choice.Ends) > BuzzerLead.TotalSeconds)
        {
            return;
        }
        _buzzedMatch = choice.Choose.MatchId;
        Garage.SaveNow();   // (a look that changed a moment ago; it sends the choice again itself for a player who is ready)
        var machine = Garage.ChosenIndex;   // (what SELECT stands on: a machine only previewed is not raced)
        if (machine >= 0 && (session.Choice?.Mine is not { } mine || mine.Machine != machine || _state.Settings.Racer.LookOf(machine) != mine))
        {
            _ = session.ChooseMachineAsync(machine);
        }
    }
}
