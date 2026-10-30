using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>A custom lobby as the browser lists it: its name, host, players of its room, a lock when it has a
/// password, its stage in a word, and the rules that differ from Quick Play's in one line.</summary>
public sealed record CustomLobbyRow(string Id, string Name, string Host, string Count, bool Locked, string Stage, bool Racing, string Rules)
{
    public bool HasRules => Rules.Length > 0;
}

/// <summary>
/// Play's Custom lobbies page, with two faces. The browser: every custom lobby the server lists, a row each; a row
/// pressed joins it, asking for the password first when it is locked. Create: the name ("ANA's lobby"), the password,
/// a preset, Edit rules (the rules panel, in its groups), then CREATE. The page is a visit of its own to the server
/// (<see cref="QuickPlaySession.Custom"/>): made when the page shows, the list asked for while it shows
/// (BrowseLobbies on and off), and ended when the player goes elsewhere in no lobby. In a lobby, the lobby page.
/// Refusals (a wrong password, a full or closed lobby) and why the player left the last lobby (the host removed
/// them, it closed) show as one line.
/// </summary>
public sealed partial class CustomLobbiesViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly Action _showLobby;
    private readonly Func<IReadOnlyList<string>> _packs;
    private QuickPlaySession? _session;
    private bool _shown;
    private bool _awaiting;   // a create or a join is on its way: the lobby page once it is taken

    /// <param name="showLobby">Shows the lobby page.</param>
    /// <param name="packs">The stat packs the host's presets offer, by name.</param>
    public CustomLobbiesViewModel(AppState state, Action showLobby, Func<IReadOnlyList<string>> packs)
    {
        ArgumentNullException.ThrowIfNull(state);
        (_state, _showLobby, _packs) = (state, showLobby, packs);
        state.Changed += () => OnUi(Attach);
    }

    public IReadOnlyList<CustomLobbyRow> Rows
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    /// <summary>The list is empty: a line says so.</summary>
    [ObservableProperty]
    public partial bool NoLobbies { get; set; }

    /// <summary>The create face shows (not the browser).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Browsing))]
    public partial bool Creating { get; set; }

    public bool Browsing => !Creating;

    /// <summary>On the create face, every rule shows beside the panel (the preset's toggle).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RulePanelsShown), nameof(PickerShown))]
    public partial bool EditingRules { get; set; }

    /// <summary>The rules show beside the panel, the course picker not over them.</summary>
    public bool RulePanelsShown => EditingRules && Editor is not { Picking: true };

    /// <summary>The fixed course's picker shows in the rules' place.</summary>
    public bool PickerShown => EditingRules && Editor is { Picking: true };

    /// <summary>The new lobby's settings (the create face).</summary>
    [ObservableProperty]
    public partial LobbyRulesEditor? Editor { get; set; }

    /// <summary>The new lobby's rules, in panels side by side (behind Edit rules).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RulePanel> RulePanels { get; set; } = [];

    partial void OnEditingRulesChanged(bool value)
    {
        if (Editor is { } editor)
        {
            editor.PresetRow.RulesShown = value;
        }
    }

    /// <summary>The locked lobby whose password is asked for (null: none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Asking))]
    public partial CustomLobbyRow? AskingFor { get; set; }

    public bool Asking => AskingFor is not null;

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    /// <summary>One line: a refusal, or why the player is out of their last lobby.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The player is in Quick Play: this page has nothing to list until they leave it.</summary>
    [ObservableProperty]
    public partial bool InQuickPlay { get; set; }

    /// <summary>Connecting, or the list has not come yet.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The page shows: the visit made, the list asked for.</summary>
    public void Shown()
    {
        _shown = true;
        Attach();
        if (_state.Session is { Custom: true, ConnectedTo: not null } session)
        {
            _ = session.BrowseAsync(true);
        }
        else if (_state.Session is { ConnectedTo: not null })
        {
            InQuickPlay = true;
        }
        else
        {
            _ = ConnectAsync();
        }
    }

    /// <summary>The page no longer shows: no more list; a visit in no lobby ends.</summary>
    public void Hidden()
    {
        _shown = false;
        (AskingFor, Creating, EditingRules) = (null, false, false);
        if (_state.Session is { Custom: true } session)
        {
            if (session.Lobby is null && !_awaiting)
            {
                _ = _state.LeaveAsync();
            }
            else
            {
                _ = session.BrowseAsync(false);
            }
        }
    }

    private async Task ConnectAsync()
    {
        if (_state.Settings.QuickPlayServer is not { } server || _state.Joining)
        {
            Status = _state.Settings.QuickPlayServer is null ? "No server" : Status;
            return;
        }
        Status = "Connecting";
        try
        {
            var session = await _state.JoinAsync(new Uri(server), custom: true).ConfigureAwait(true);
            if (_shown)
            {
                await session.BrowseAsync(true).ConfigureAwait(true);
            }
            else if (session.Lobby is null && _state.Session == session)
            {
                await _state.LeaveAsync().ConfigureAwait(true);   // (the page was left meanwhile)
            }
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or UriFormatException or IOException)
        {
            Status = "";
            Notice = AppState.CouldNotReachText(e);
            await _state.LeaveAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // left while connecting
        }
    }

    private void Attach()
    {
        if (_state.Session != _session)
        {
            _session = _state.Session;
            if (_session is not null)
            {
                _session.Changed += () => OnUi(Refresh);
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        var session = _session is { ConnectedTo: not null } s ? s : null;
        InQuickPlay = session is { Custom: false };
        if (session is not { Custom: true })
        {
            Rows = [];
            NoLobbies = false;
            Status = session is null && _state.Joining ? "Connecting" : "";
            return;
        }
        Status = session.PlayerId is null ? "Connecting" : "";
        var rows = session.Lobbies.Select(l => Row(l, session.QuickPlayRules)).ToList();
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }
        NoLobbies = session.PlayerId is not null && rows.Count == 0;
        Notice = NoticeOf(session) ?? (session.InCustomLobby ? "" : Notice);
        if (session.LobbyRefusal is not null || session.LeftReason is not null)
        {
            _awaiting = false;
        }
        if (_awaiting && session.InCustomLobby)
        {
            _awaiting = false;
            (AskingFor, Password, Creating, EditingRules, Notice) = (null, "", false, false, "");
            _showLobby();
        }
    }

    /// <summary>A lobby of the list as its row.</summary>
    public static CustomLobbyRow Row(LobbySummary lobby, LobbyRules? quickPlay)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        return new CustomLobbyRow(lobby.Id, lobby.Name, lobby.HostName,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lobby.Players}/{lobby.Capacity}"), lobby.Locked,
            StageText(lobby.Stage), lobby.Stage == LobbyState.Racing, CustomRules.Summary(lobby.Rules, quickPlay));
    }

    /// <summary>A lobby's stage in a word.</summary>
    public static string StageText(string stage) => stage switch
    {
        LobbyState.Racing => "Racing",
        LobbyState.Starting or LobbyState.Choosing => "Starting",
        _ => "Waiting",
    };

    /// <summary>What the one line says of the session's last refusal or of the player's leaving; null: nothing.</summary>
    public static string? NoticeOf(QuickPlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.LobbyRefusal is { } refusal ? RefusalText(refusal.Code)
            : session.LeftReason is { } reason ? LeftText(reason)
            : null;
    }

    /// <summary>A refusal in a few words.</summary>
    public static string RefusalText(string code) => code switch
    {
        CustomLobby.BadPassword => "Wrong password",
        CustomLobby.LobbyFull => "Lobby full",
        CustomLobby.LobbyClosed => "Lobby closed to new players",
        CustomLobby.NoSuchLobby => "Lobby gone",
        CustomLobby.NotHost => "Only the host can do that",
        CustomLobby.BadRules => "Rules not taken",
        CustomLobby.BadMode => "Mode not taken",
        CustomLobby.RulesLocked => "Rules change only while the lobby waits",
        CustomLobby.BadName => "Lobby name not taken",
        _ => "Refused",
    };

    /// <summary>Why the player is out of a lobby, in a few words.</summary>
    public static string LeftText(string reason) => reason switch
    {
        CustomLobby.Kicked => "Removed from the lobby by the host",
        CustomLobby.Closed => "Lobby closed",
        _ => "Left the lobby",
    };

    /// <summary>A row pressed: joined, or its password asked for first.</summary>
    [RelayCommand]
    private Task OpenAsync(CustomLobbyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Locked)
        {
            (AskingFor, Password) = (row, "");
            return Task.CompletedTask;
        }
        AskingFor = null;
        return JoinAsync(row.Id, null);
    }

    [RelayCommand]
    private Task JoinLockedAsync() => AskingFor is { } row ? JoinAsync(row.Id, Password) : Task.CompletedTask;

    [RelayCommand]
    private void CancelPassword() => (AskingFor, Password) = (null, "");

    private Task JoinAsync(string id, string? password)
    {
        if (_session is not { Custom: true, PlayerId: not null } session)
        {
            return Task.CompletedTask;
        }
        _awaiting = true;
        Notice = "";
        return session.JoinLobbyAsync(id, password);
    }

    /// <summary>Create: the create face, on Quick Play's rules and the player's lobby's name.</summary>
    [RelayCommand]
    private void ShowCreate()
    {
        var editor = new LobbyRulesEditor(null, _session?.QuickPlayRules, _packs(), DefaultName(_state.Settings.Racer.Name));
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LobbyRulesEditor.RuleGroups))
            {
                RulePanels = LobbyViewModel.Panels(editor.RuleGroups);   // (a preset chosen)
            }
            if (e.PropertyName == nameof(LobbyRulesEditor.Picking))
            {
                OnPropertyChanged(nameof(RulePanelsShown));
                OnPropertyChanged(nameof(PickerShown));
            }
        };
        editor.PresetRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresetRule.RulesShown))
            {
                EditingRules = editor.PresetRow.RulesShown;
            }
        };
        Editor = editor;
        RulePanels = LobbyViewModel.Panels(editor.RuleGroups);
        (Creating, EditingRules, AskingFor, Notice) = (true, false, null, "");
    }

    /// <summary>"ANA's lobby" (a long name cut so that the whole fits a lobby's name).</summary>
    public static string DefaultName(string player)
    {
        const string Lobby = "'s lobby";
        var name = (player ?? "").Trim();
        return name.Length == 0 ? "My lobby" : name[..Math.Min(name.Length, CustomLobby.MaxName - Lobby.Length)] + Lobby;
    }

    [RelayCommand]
    private void ToggleRules() => EditingRules = !EditingRules;

    [RelayCommand]
    private void Back() => (Creating, EditingRules) = (false, false);

    [RelayCommand]
    private Task CreateAsync()
    {
        if (Editor is not { } editor || _session is not { Custom: true, PlayerId: not null } session)
        {
            return Task.CompletedTask;
        }
        if (!editor.NameValid)
        {
            Notice = RefusalText(CustomLobby.BadName);
            return Task.CompletedTask;
        }
        _awaiting = true;
        Notice = "";
        return session.CreateLobbyAsync(editor.Name, editor.Rules, editor.PasswordRow.Text);
    }

    [RelayCommand]
    private Task LeaveQuickPlayAsync() => _state.LeaveAsync().ContinueWith(_ => OnUi(() =>
    {
        if (_shown)
        {
            Shown();
        }
    }), TaskScheduler.Default);
}
