using Online.Protocol;

namespace Launcher.Core;

// A spectator seat in the lobby: the player watches the lobby's
// races instead of racing them. The server sends a spectator's start for each race (at once when the lobby races
// already), and the game is started as a spectator: it receives the race, sends nothing, and its controls are the
// spectator's.
public sealed partial class QuickPlaySession
{
    /// <summary>The player asked to watch the lobby's races instead of racing.</summary>
    public bool Watching { get; private set; }

    /// <summary>The server takes spectators (its welcome said so).</summary>
    public bool CanWatch => _spectators && Lobby is not null;

    private bool _spectators;

    /// <summary>Watch from now on (yes), or race again from the next race.</summary>
    public async Task WatchAsync(bool yes)
    {
        if (!CanWatch || Watching == yes || _client is null)
        {
            return;
        }
        Watching = yes;
        await _client.WatchAsync(yes).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void OnWatchStart(WatchStart w)
    {
        Race = Prepare(new MatchStart(w.MatchId, w.Relay, w.Token, w.Seat, w.Setup), spectate: true);
        Set("Watching");
        startFork(Race);
    }
}
