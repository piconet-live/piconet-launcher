using Online.Protocol;

namespace Launcher.Core;

// The player's name on the server, kept the one the launcher has saved.
public sealed partial class QuickPlaySession
{
    /// <summary>The least time between two renames sent: the server takes one a second.</summary>
    internal static readonly TimeSpan RenameSpacing = TimeSpan.FromMilliseconds(1100);

    /// <summary>How often one name is sent before the launcher lets it be (a server that never shows it).</summary>
    internal const int RenameTries = 5;

    /// <summary>How long the server's word on a name just sent is waited for (the lobby that carries it, or the
    /// refusal) before a lobby with another name counts as that word.</summary>
    internal static readonly TimeSpan RenameAnswerWait = TimeSpan.FromSeconds(5);

    private readonly object _naming = new();
    private bool _renames;            // the server takes a name changed while connected (its welcome said so)
    private string? _serverName;      // the name the server has for this player, as far as the launcher knows
    private string? _refusedName;     // a name the server refused as a name: not sent again
    private (string Name, int Count) _tried;
    private DateTimeOffset _renameSent = DateTimeOffset.MinValue;
    private bool _renameDue;          // a later look is on its way
    private (string Name, DateTimeOffset Until)? _awaited;   // the name last sent, until the server has said its word on it

    /// <summary>The player's name was saved, or the server said something about it: where the server's name for this
    /// player is not the saved one, the saved one is sent (to a server whose welcome says that it takes a name:
    /// <see cref="Welcome.Renames"/>; an older one keeps the hello's name until the next join, as it always did). It
    /// is sent again until the two agree: a name saved between RACE and the welcome went with no message at all, and
    /// of two names saved within a second the server refuses the second (rename_too_soon), which left the first one
    /// on the server for the whole visit. The lobby's own list is the server's word on the name.</summary>
    public void NameChanged()
    {
        string? send = null;
        TimeSpan? later = null;
        var client = _client;
        lock (_naming)
        {
            var saved = Racer.Name.Trim();
            if (client is null || ConnectedTo is null || !_renames || _serverName is null || saved == _serverName || saved == _refusedName)
            {
                return;
            }
            if (_tried.Name != saved)
            {
                _tried = (saved, 0);
            }
            var since = DateTimeOffset.UtcNow - _renameSent;
            if (_tried.Count >= RenameTries)
            {
                return;
            }
            if (since < RenameSpacing)
            {
                later = _renameDue ? null : RenameSpacing - since;
                _renameDue = true;
            }
            else
            {
                (send, _renameSent, _serverName, _tried) = (saved, DateTimeOffset.UtcNow, saved, (saved, _tried.Count + 1));
                _awaited = (saved, _renameSent + RenameAnswerWait);
            }
        }
        if (send is not null)
        {
            _ = client.SetNameAsync(send);
        }
        else if (later is { } wait)
        {
            _ = Task.Delay(wait).ContinueWith(_ =>
            {
                lock (_naming)
                {
                    _renameDue = false;
                }
                NameChanged();
            }, TaskScheduler.Default);
        }
    }

    /// <summary>The server's word on this player's name: the hello's at the welcome, then the lobby's list. A lobby
    /// that still carries another name while the word on a name just sent is out is not that word: the server sent
    /// it before it read the name (for the rename before, or for another player's change), and taking it as the
    /// answer sent the same name a second time to a server that had it.</summary>
    private void ServerHasName(string? name)
    {
        if (name is null)
        {
            return;
        }
        lock (_naming)
        {
            if (_awaited is { } sent && name != sent.Name && DateTimeOffset.UtcNow < sent.Until)
            {
                return;
            }
            _awaited = null;
            _serverName = name;
        }
        NameChanged();
    }

    /// <summary>The server did not take the name just sent: too soon after the last (sent again in a moment), or
    /// not a name to it (let be). Neither is the player's trouble: nothing is shown.</summary>
    private void OnRenameRefused(ErrorMessage e)
    {
        lock (_naming)
        {
            if (e.Code == "bad_name")
            {
                _refusedName = _serverName;
            }
            _serverName = "";   // not known until the lobby says it; and not the saved one
            _awaited = null;    // this is the server's word on the name sent
        }
        NameChanged();
    }
}
