using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>The chat sidebar's tabs.</summary>
public enum ChatTab
{
    /// <summary>The lobby's chat: its players' lines only.</summary>
    Lobby,

    /// <summary>The global chat: lines sent to every connected player.</summary>
    Global,

    /// <summary>The players this launcher knows of: the lobby's.</summary>
    Players,
}

/// <summary>A group of the Players tab: its title with the count ("RACERS 5"), and its players' rows.</summary>
public sealed record PlayerGroup(string Title, IReadOnlyList<SeatRow> Rows);

/// <summary>
/// The chat as a sidebar on every page (the owner, 2026-10-14/15 and 2026-11-08), opened and closed by the button
/// beside the bell. Pinned, it is a column of its own beside the page (the page narrows, nothing is covered) and stays
/// open across pages and starts, as wide as the room the page leaves (<see cref="PageLeast"/>); unpinned, it opens over the page and closes on a press elsewhere, Escape or the
/// button. Its width is dragged at its left edge. Both, and whether a pinned one was open, are kept in the settings
/// (<see cref="LauncherSettings.ChatPinned"/>, <see cref="LauncherSettings.ChatOpen"/>,
/// <see cref="LauncherSettings.ChatWidth"/>). Three tabs: the lobby's chat and the global chat (the lines are the
/// lobby page's: <see cref="LobbyViewModel.Chat"/>, <see cref="LobbyViewModel.GlobalChat"/>) and the players. A
/// player's line on a tab not in view puts a dot on that tab and on the button; never a count. The server's notices
/// put none: the bell has its own.
/// </summary>
public sealed partial class ChatSidebarViewModel : ViewModelBase
{
    private readonly AppState _state;

    public ChatSidebarViewModel(AppState state, LobbyViewModel lobby)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lobby);
        _state = state;
        Lobby = lobby;
        Pinned = state.Settings.ChatPinned;
        Open = Pinned && state.Settings.ChatOpen;
        Width = LauncherSettings.ChatWidthKept(state.Settings.ChatWidth);
        lobby.Chat.CollectionChanged += (_, e) => Heard(e, ChatTab.Lobby);
        lobby.GlobalChat.CollectionChanged += (_, e) => Heard(e, ChatTab.Global);
        lobby.PropertyChanged += OnLobbyChanged;
        state.Changed += () => OnUi(ShowWhere);
        ShowWhere();
        ShowPlayers();
        _ready = true;
    }

    private readonly bool _ready;   // (the settings are not written back while the constructor reads them)

    /// <summary>The lobby page's model, which keeps the session's chat lines, the message and its sending.</summary>
    public LobbyViewModel Lobby { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Overlay), nameof(Column), nameof(ColumnWidth))]
    public partial bool Open { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Overlay), nameof(Column), nameof(ColumnWidth), nameof(ShownWidth))]
    public partial bool Pinned { get; set; }

    /// <summary>The sidebar's width at 100% scale, as the player set it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColumnWidth), nameof(ShownWidth))]
    public partial double Width { get; set; }

    /// <summary>The most a pinned sidebar may take beside the page (the window says, at 100% scale): the window's width
    /// less the left column and <see cref="PageLeast"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColumnWidth), nameof(ShownWidth))]
    public partial double Room { get; set; } = double.PositiveInfinity;

    /// <summary>What the page keeps beside a pinned sidebar: the lobby's head (its title, the four pages' names, the
    /// chat's button and the bell) and its racers fit in it. At the least window (1280) that leaves the sidebar
    /// its default width.</summary>
    public const double PageLeast = 740;

    /// <summary>The width the sidebar is drawn at: pinned, no more than the room beside the page (never under its
    /// least: a window made smaller than that scrolls, as the launcher does); unpinned, over the page, the width set.</summary>
    public double ShownWidth => Pinned ? Math.Max(LauncherSettings.LeastChatWidth, Math.Min(Width, Room)) : Width;

    /// <summary>Open over the page (unpinned).</summary>
    public bool Overlay => Open && !Pinned;

    /// <summary>Open in a column of its own (pinned).</summary>
    public bool Column => Open && Pinned;

    /// <summary>The room the page gives the sidebar: its width while it is pinned open, else none.</summary>
    public double ColumnWidth => Column ? ShownWidth : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnLobby), nameof(OnGlobal), nameof(OnPlayers), nameof(Standby), nameof(StandbyShown),
        nameof(ChatShown), nameof(PlayersShown), nameof(LobbyChatShown), nameof(GlobalChatShown))]
    public partial ChatTab Tab { get; set; }

    public bool OnLobby => Tab == ChatTab.Lobby;

    public bool OnGlobal => Tab == ChatTab.Global;

    public bool OnPlayers => Tab == ChatTab.Players;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial bool LobbyDot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial bool GlobalDot { get; set; }

    /// <summary>The button's dot: a tab has a line the player has not had in view.</summary>
    public bool HasDot => LobbyDot || GlobalDot;

    /// <summary>The launcher has a connection to a server (Quick Play, a custom lobby or its browser).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Standby), nameof(StandbyShown), nameof(ChatShown), nameof(PlayersShown),
        nameof(LobbyChatShown), nameof(GlobalChatShown))]
    public partial bool Connected { get; set; }

    /// <summary>The player is in a lobby.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Standby), nameof(StandbyShown), nameof(ChatShown), nameof(PlayersShown),
        nameof(LobbyChatShown), nameof(GlobalChatShown))]
    public partial bool InLobby { get; set; }

    /// <summary>What the tab says when it has nothing it can show: "Not connected" (no server to chat on), "Not in
    /// a lobby" (the Lobby and Players tabs); "" when it shows its lines or players.</summary>
    public string Standby => (Tab, Connected, InLobby) switch
    {
        (_, false, _) => NotConnectedText,
        (ChatTab.Lobby or ChatTab.Players, true, false) => NotInLobbyText,
        _ => "",
    };

    public const string NotConnectedText = "Not connected", NotInLobbyText = "Not in a lobby";

    public bool StandbyShown => Standby.Length > 0;

    /// <summary>The chat's lines and message box show (a chat tab with something to show).</summary>
    public bool ChatShown => !StandbyShown && Tab != ChatTab.Players;

    /// <summary>The lobby's chat shows (each chat keeps its own place in its lines while the other shows).</summary>
    public bool LobbyChatShown => ChatShown && Tab == ChatTab.Lobby;

    public bool GlobalChatShown => ChatShown && Tab == ChatTab.Global;

    public bool PlayersShown => !StandbyShown && Tab == ChatTab.Players;

    /// <summary>The Players tab: the lobby's racers, then its watchers, each a group with its count.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PlayerGroup> PlayerGroups { get; set; } = [];

    /// <summary>The button beside the bell: open or close.</summary>
    [RelayCommand]
    private void Toggle() => Open = !Open;

    [RelayCommand]
    private void Close() => Open = false;

    /// <summary>The pin: pinned, the sidebar takes a column beside the page; unpinned, it stands over it.</summary>
    [RelayCommand]
    private void TogglePin() => Pinned = !Pinned;

    [RelayCommand]
    private void ShowTab(ChatTab tab) => Tab = tab;

    /// <summary>The width while the edge is dragged (at 100% scale), held to its least and most; kept with
    /// <see cref="KeepWidth"/> when the drag ends.</summary>
    public void Drag(double width) => Width = Math.Min(LauncherSettings.ChatWidthKept((int)Math.Round(width)),
        Pinned ? Math.Max(LauncherSettings.LeastChatWidth, Math.Floor(Room)) : double.PositiveInfinity);

    /// <summary>The width as it stands, kept in the settings.</summary>
    public void KeepWidth()
    {
        var width = (int)Width;
        if (_state.Settings.ChatWidth != width)
        {
            _state.Update(s => s with { ChatWidth = width });
        }
    }

    partial void OnOpenChanged(bool value)
    {
        Seen();
        Keep();
    }

    partial void OnPinnedChanged(bool value) => Keep();

    partial void OnTabChanged(ChatTab value) => Seen();

    /// <summary>The pin, and whether a pinned sidebar is open, kept (an unpinned one starts closed whatever it was).</summary>
    private void Keep()
    {
        if (!_ready)
        {
            return;
        }
        var (pinned, open) = (Pinned, Pinned && Open);
        if (_state.Settings.ChatPinned != pinned || _state.Settings.ChatOpen != open)
        {
            _state.Update(s => s with { ChatPinned = pinned, ChatOpen = open });
        }
    }

    /// <summary>The tab in view has been seen: its dot goes.</summary>
    private void Seen()
    {
        if (!Open)
        {
            return;
        }
        if (Tab == ChatTab.Lobby)
        {
            LobbyDot = false;
        }
        else if (Tab == ChatTab.Global)
        {
            GlobalDot = false;
        }
    }

    /// <summary>Lines came to a tab: a player's line (not the player's own, not a server's notice) that is not in view
    /// puts the tab's dot on.</summary>
    private void Heard(NotifyCollectionChangedEventArgs e, ChatTab tab)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null
            || !e.NewItems.OfType<ChatLine>().Any(l => l is { You: false, Server: false }) || (Open && Tab == tab))
        {
            return;
        }
        if (tab == ChatTab.Lobby)
        {
            LobbyDot = true;
        }
        else
        {
            GlobalDot = true;
        }
    }

    private void OnLobbyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LobbyViewModel.SeatColumns) or nameof(LobbyViewModel.WatchingRows))
        {
            ShowPlayers();
        }
        ShowWhere();
    }

    /// <summary>Connected, in a lobby: from the session as it is now. Out of a lobby the lobby's dot goes with it.</summary>
    private void ShowWhere()
    {
        var session = _state.Session;
        Connected = session is { ConnectedTo: not null };
        InLobby = Connected && session?.Lobby is not null;
        if (!InLobby)
        {
            LobbyDot = false;
        }
        if (!Connected)
        {
            GlobalDot = false;
        }
    }

    private void ShowPlayers()
    {
        var racers = Lobby.SeatColumns.SelectMany(c => c.Seats).Where(s => s.Id is not null).ToList();
        var watching = Lobby.WatchingRows;
        var groups = new List<PlayerGroup>();
        if (racers.Count > 0)
        {
            groups.Add(new PlayerGroup($"RACERS {racers.Count}", racers));
        }
        if (watching.Count > 0)
        {
            groups.Add(new PlayerGroup($"WATCHING {watching.Count}", watching));
        }
        PlayerGroups = groups;
    }
}
