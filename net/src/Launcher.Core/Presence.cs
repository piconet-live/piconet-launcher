using System.Globalization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>The game the launcher started and is running, as Discord's status names it (<see cref="PresenceWords"/>).</summary>
public enum GameRun
{
    /// <summary>No game of this launcher runs.</summary>
    None,

    /// <summary>"Play F-Zero GX": the game on its own, to its title screen.</summary>
    Offline,

    /// <summary>A race from a match file (development's <c>--play-match</c>).</summary>
    Match,

    /// <summary>A Quick Play race the player races.</summary>
    Race,

    /// <summary>A Quick Play race the player watches from a spectator's seat.</summary>
    Watch,

    /// <summary>Play's Practice: a solo run.</summary>
    Practice,

    /// <summary>A replay from the Replays page.</summary>
    Replay,
}

/// <summary>What Discord's status shows: two short lines, the small picture and its text, and the time a race
/// started (Discord counts "elapsed" from it). The large picture, its text and the button are the same for every
/// card (<see cref="PresenceBrand"/>).</summary>
/// <param name="Details">The first line ("In a lobby", a course's name).</param>
/// <param name="State">The second line, or none.</param>
/// <param name="SmallImage">The small picture's key in the Discord application's art (<c>race</c>, <c>watch</c>,
/// <c>practice</c>), or none.</param>
public sealed record PresenceCard(string Details, string? State = null, string? SmallImage = null, string? SmallText = null,
    DateTimeOffset? Since = null);

/// <summary>What the launcher knows that the status is made from: the game it runs, the lobby, the race.</summary>
/// <param name="Game">The game running now (<see cref="GameRun.None"/> once it has closed).</param>
/// <param name="Course">A practice's course.</param>
/// <param name="Lobby">The Quick Play lobby the player is in, or none.</param>
/// <param name="PlayerId">The player's id in that lobby.</param>
/// <param name="CourseVote">The lobby's course vote is open or drawn (the lobby's starting stage is the vote).</param>
/// <param name="Race">The race the game runs (a Quick Play race's set-up), or none.</param>
/// <param name="Live">The race's last live snapshot from the player's game, or none yet.</param>
/// <param name="Go">When the player's race started (its clock at zero), or not yet.</param>
public sealed record PresenceInput(GameRun Game = GameRun.None, int? Course = null, LobbyState? Lobby = null,
    string? PlayerId = null, bool CourseVote = false, RaceSetup? Race = null, RaceSnapshot? Live = null,
    DateTimeOffset? Go = null);

/// <summary>
/// The launcher's state as Discord's status shows it: labels and values, short. The app's
/// name above them ("Playing" and a name) is the Discord application's own.
/// </summary>
public static class PresenceWords
{
    public const string Menus = "In the menus", InALobby = "In a lobby", Watching = "Watching", Practising = "Practising",
        Replay = "Watching a replay", Offline = "Playing offline", Racing = "Racing";

    /// <summary>The small pictures' keys in the Discord application's art (uploaded in the Discord developer portal).</summary>
    public const string RaceImage = "race", WatchImage = "watch", PracticeImage = "practice";

    public static PresenceCard Of(PresenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Game switch
        {
            GameRun.Replay => new PresenceCard(Replay, SmallImage: WatchImage, SmallText: Watching),
            GameRun.Practice => new PresenceCard(Practising, CourseOf(input.Course), PracticeImage, Practising),
            GameRun.Offline or GameRun.Match => new PresenceCard(Offline),
            GameRun.Watch => Watched(CourseOf(input.Race?.Course)),
            GameRun.Race => Raced(input),
            _ when input.Lobby is { } lobby => InLobby(lobby, input.CourseVote),
            _ => new PresenceCard(Menus),
        };
    }

    /// <summary>The player's race: the course, then the lap and the place ("Lap 2 / 3 · 5th of 30"); knocked out,
    /// watching it.</summary>
    private static PresenceCard Raced(PresenceInput input)
    {
        var course = CourseOf(input.Race?.Course) ?? Racing;
        var me = input.Live?.Racers.FirstOrDefault(r => r.Seat == input.Live.LocalSeat);
        if (me is null)
        {
            return new PresenceCard(course, "Starting", RaceImage, Racing);
        }
        if (me.State == RacerLive.Out)
        {
            return Watched(course);
        }
        var racers = input.Race?.Seats.Count is > 0 and var seats ? seats : input.Live!.Racers.Count;
        var place = RaceStandings.Places(input.Live!.Racers).TryGetValue(me.Seat, out var p)
            ? $" · {ReplayWords.Ordinal(p)} {ReplayWords.Of(racers)}"
            : "";
        var laps = input.Race?.Laps ?? 0;
        var line = me.State == RacerLive.Finished ? "Finished"
            : laps > 0 ? $"Lap {Number(Math.Min(me.Lap + 1, laps))} / {Number(laps)}"
            : $"Lap {Number(me.Lap + 1)}";
        return new PresenceCard(course, line + place, RaceImage, Racing, input.Go);
    }

    private static PresenceCard Watched(string? course) => new(Watching, course, WatchImage, Watching);

    /// <summary>A lobby between races: its racers and where it is ("14 / 30 · voting"); during its race, watching.</summary>
    private static PresenceCard InLobby(LobbyState lobby, bool courseVote)
    {
        if (lobby.Stage == LobbyState.Racing)
        {
            return Watched(CourseOf(lobby.Race?.Course));
        }
        var step = lobby.Stage switch
        {
            LobbyState.Starting => courseVote ? "voting" : "starting",
            LobbyState.Choosing when lobby.Waiting is null => "choosing machines",
            LobbyState.Choosing => "starting",
            _ => "gathering",
        };
        return new PresenceCard(InALobby, $"{Number(lobby.Players.Count)} / {Number(lobby.Capacity)} · {step}");
    }

    private static string? CourseOf(int? course) => course is { } c ? CourseNames.Name(c) : null;

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
