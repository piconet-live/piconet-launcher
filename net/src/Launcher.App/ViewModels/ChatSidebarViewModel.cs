using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>
/// The chat as a sidebar on every page (the owner, 2026-10-14/15 and 2026-11-08), opened and closed by the button
/// beside the bell. Pinned, it is a column of its own beside the page (the page narrows, nothing is covered) and stays
/// open across pages and starts, as wide as the room the page leaves (<see cref="PageLeast"/>); unpinned, it opens over
/// the page and closes on a press elsewhere, Escape or the button. Its width is dragged at its left edge. Both, and whether a pinned one was open, are kept in the settings
/// (<see cref="LauncherSettings.ChatPinned"/>, <see cref="LauncherSettings.ChatOpen"/>,
/// <see cref="LauncherSettings.ChatWidth"/>). It shows the lobby's chat (the lines are the lobby page's:
/// <see cref="LobbyViewModel.Chat"/>), and out of a lobby one plain line. Only the lobby's chat for now (the owner,
/// 2026-11-08: "hide the Global channel. And also Players. It's just Lobby for now"). Another player's line while
/// the sidebar is closed puts a dot on its button; never a count. The server's notices put none: the bell has its own.
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
        lobby.Chat.CollectionChanged += (_, e) => Heard(e);
        lobby.PropertyChanged += (_, _) => ShowWhere();
        state.Changed += () => OnUi(ShowWhere);
        ShowWhere();
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

    /// <summary>The button's dot: another player's line came while the sidebar was closed.</summary>
    [ObservableProperty]
    public partial bool HasDot { get; set; }

    /// <summary>The player is in a lobby (connected): the sidebar shows its chat.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Standby))]
    public partial bool InLobby { get; set; }

    /// <summary>What the sidebar says out of a lobby; "" in one.</summary>
    public string Standby => InLobby ? "" : NotInLobbyText;

    public const string NotInLobbyText = "Join a lobby to chat";

    /// <summary>The button beside the bell: open or close.</summary>
    [RelayCommand]
    private void Toggle() => Open = !Open;

    [RelayCommand]
    private void Close() => Open = false;

    /// <summary>The pin: pinned, the sidebar takes a column beside the page; unpinned, it stands over it.</summary>
    [RelayCommand]
    private void TogglePin() => Pinned = !Pinned;

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
        if (value)
        {
            HasDot = false;   // seen
        }
        Keep();
    }

    partial void OnPinnedChanged(bool value) => Keep();

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

    /// <summary>Lines came: another player's line (not the player's own, not a server's notice) while the sidebar is
    /// closed puts the dot on.</summary>
    private void Heard(NotifyCollectionChangedEventArgs e)
    {
        if (!Open && e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems?.OfType<ChatLine>().Any(l => l is { You: false, Server: false }) == true)
        {
            HasDot = true;
        }
    }

    /// <summary>In a lobby: from the session as it is now. Out of one the dot goes with it.</summary>
    private void ShowWhere()
    {
        InLobby = _state.Session is { ConnectedTo: not null, Lobby: not null };
        if (!InLobby)
        {
            HasDot = false;
        }
    }
}
