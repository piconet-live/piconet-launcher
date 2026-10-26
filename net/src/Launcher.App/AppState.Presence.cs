using Launcher.Core;
using Online.Protocol;

namespace Launcher.App;

// Discord's status (DiscordPresence): what the player does, made from this state on every change, and from the live
// race once a second. Only the window's launcher shows one (App.axaml.cs); the modes without a window do not.
public sealed partial class AppState
{
    /// <summary>The status's connection; ended at the launcher's end (<see cref="Shutdown"/>).</summary>
    private DiscordPresence? Presence { get; set; }

    /// <summary>The game started last and what it is for the status (read while <see cref="Racing"/>).</summary>
    private GameRun _run;

    /// <summary>A practice's course, for the status.</summary>
    private int? _runCourse;

    /// <summary>When the player's race started, and which race: set once a race, from the game's race clock.</summary>
    private DateTimeOffset? _go;

    private string? _goMatch;

    private QuickPlaySession? _heardSession;

    private DateTimeOffset PresenceAt;

    /// <summary>The status on Discord from now on, while the player's setting is on; null: the build's own
    /// application (<see cref="Branding.Discord"/>).</summary>
    public void StartPresence(PresenceBrand? brand = null, Func<PresenceBrand, Action<PresenceLinkEvent>, IPresenceLink>? link = null)
    {
        if (Presence is not null)
        {
            return;
        }
        Presence = new DiscordPresence(brand ?? Branding.Discord, line => Noted?.Invoke(line), link);
        Changed += ShowPresence;
        LiveReceived += snapshot =>
        {
            HeardGo(snapshot);
            var now = DateTimeOffset.UtcNow;
            if (now - PresenceAt >= TimeSpan.FromSeconds(1))
            {
                ShowPresence();
            }
        };
        ShowPresence();
    }

    /// <summary>The status as it stands (none while the presence is not started): for the tests.</summary>
    public PresenceCard? PresenceShown => Presence?.Shown;

    /// <summary>What the status says now.</summary>
    public PresenceInput PresenceNow()
    {
        var session = Session;
        var setup = session?.Race?.Start.Setup;
        return new PresenceInput(
            Game: Racing ? _run : GameRun.None,
            Course: _runCourse,
            Lobby: session?.Lobby,
            PlayerId: session?.PlayerId,
            CourseVote: session?.CourseVote is not null,
            Race: _run is GameRun.Race or GameRun.Watch ? setup : null,
            Live: _run == GameRun.Race ? Board?.Latest : null,
            Go: _go);
    }

    /// <summary>A game was started: what it is for the status.</summary>
    private void Running(GameRun run, int? course = null)
    {
        _run = run;
        _runCourse = course;
        _go = null;
        _goMatch = null;
    }

    private void ShowPresence()
    {
        if (Presence is not { } presence)
        {
            return;
        }
        if (Session is { } session && session != _heardSession)
        {
            // a new Quick Play session: its lobby's changes change the status too
            _heardSession = session;
            session.Changed += ShowPresence;
        }
        PresenceAt = DateTimeOffset.UtcNow;
        presence.SetOn(Settings.ShowOnDiscord);
        presence.Show(PresenceWords.Of(PresenceNow()));
    }

    /// <summary>The race's start, once a race: the game's race clock at its first count, back from now.</summary>
    private void HeardGo(RaceSnapshot snapshot)
    {
        var match = Session?.Race?.Start.MatchId;
        if (match is null || match == _goMatch)
        {
            return;
        }
        if (snapshot.Racers.FirstOrDefault(r => r.Seat == snapshot.LocalSeat) is { TotalMs: > 0 } me)
        {
            _goMatch = match;
            _go = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(me.TotalMs);
        }
    }
}
