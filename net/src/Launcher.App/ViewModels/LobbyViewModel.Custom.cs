using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>A rules panel's groups, one panel of a row of them.</summary>
public sealed record RulePanel(IReadOnlyList<RuleGroup> Groups);

// A custom lobby on the lobby page: the host with their mark, the rules that differ from Quick Play's in one line
// with every rule behind a click, and the host's own controls (their rules while the lobby waits, the start, a
// player removed). Leaving goes back to the browser.
public sealed partial class LobbyViewModel
{
    /// <summary>Where leaving a custom lobby goes: the browser (the window sets it).</summary>
    public Action? ToBrowser { get; set; }

    /// <summary>The stat packs the host's presets offer, by name (the window sets it).</summary>
    public Func<IReadOnlyList<string>> PackNames { get; set; } = () => [];

    /// <summary>The lobby is a custom lobby.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomBarShown), nameof(HostCanEdit))]
    public partial bool Custom { get; set; }

    /// <summary>This player is its host.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HostCanEdit))]
    public partial bool IsHost { get; set; }

    /// <summary>The host's name, beside the host's mark.</summary>
    [ObservableProperty]
    public partial string HostName { get; set; } = "";

    /// <summary>The lobby's name.</summary>
    [ObservableProperty]
    public partial string LobbyName { get; set; } = "";

    /// <summary>The lobby has a password, as its state says: a lock beside its name.</summary>
    [ObservableProperty]
    public partial bool LobbyLocked { get; set; }

    /// <summary>The rules that differ from Quick Play's, in one line; "Quick Play rules" when none does.</summary>
    [ObservableProperty]
    public partial string RulesSummary { get; set; } = "";

    /// <summary>Every rule, in its panels (behind the summary's click).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RulePanel> AllRulePanels { get; set; } = [];

    /// <summary>The whole list of rules shows in place of the racers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RulesOpen), nameof(RosterShown), nameof(AllRulesText))]
    public partial bool AllRulesShown { get; set; }

    public string AllRulesText => AllRulesShown ? "Hide rules" : "All rules";

    /// <summary>The host's rules panel shows in place of the racers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RulesOpen), nameof(RosterShown))]
    public partial bool HostEditing { get; set; }

    /// <summary>The host's settings while they edit them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HostPicking))]
    public partial LobbyRulesEditor? HostEditor { get; set; }

    /// <summary>The host's fixed course is being picked: the course picker in the rules' place.</summary>
    public bool HostPicking => HostEditor is { Picking: true };

    /// <summary>The host's rules' panels, beside the name and password's.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RulePanel> HostRulePanels { get; set; } = [];

    /// <summary>A rules list shows where the racers' list stands.</summary>
    public bool RulesOpen => AllRulesShown || HostEditing;

    /// <summary>The custom lobby's bar (host, summary, the host's buttons) stands over the page.</summary>
    public bool CustomBarShown => Custom && !Racing && !GarageShown;

    /// <summary>The host's Edit rules and Start race: the lobby waits.</summary>
    public bool HostCanEdit => Custom && IsHost && Gathering;

    private void ShowCustom(QuickPlaySession session, LobbyState? lobby)
    {
        Custom = lobby is { Custom: true };
        IsHost = session.IsHost;
        if (lobby is not { Custom: true })
        {
            (HostName, LobbyName, LobbyLocked, RulesSummary, AllRulesShown, HostEditing) = ("", "", false, "", false, false);
            return;
        }
        HostName = lobby.Players.FirstOrDefault(p => p.Id == lobby.HostId)?.Name ?? "";
        (LobbyName, LobbyLocked) = (lobby.Name ?? "", lobby.Locked);
        var summary = CustomRules.Summary(lobby.Rules, session.QuickPlayRules);
        RulesSummary = summary.Length > 0 ? summary : "Quick Play rules";
        if (AllRulesShown)
        {
            AllRulePanels = Panels(CustomRules.AllRules(lobby.Rules, session.QuickPlayRules));
        }
        if (HostEditing && !HostCanEdit)
        {
            HostEditing = false;   // (no longer the host, or the lobby moved on)
        }
        OnPropertyChanged(nameof(CustomBarShown));
        OnPropertyChanged(nameof(HostCanEdit));
    }

    /// <summary>The rules' groups as panels side by side: the first group alone, the rest together.</summary>
    public static IReadOnlyList<RulePanel> Panels(IReadOnlyList<RuleGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Count == 0 ? []
            : [new RulePanel([groups[0] with { Later = false }]), new RulePanel([.. groups.Skip(1).Select((g, i) => g with { Later = i > 0 })])];
    }

    [RelayCommand]
    private void ToggleAllRules()
    {
        HostEditing = false;
        AllRulesShown = !AllRulesShown;
        if (AllRulesShown && _session?.Lobby is { } lobby)
        {
            AllRulePanels = Panels(CustomRules.AllRules(lobby.Rules, _session.QuickPlayRules));
        }
    }

    /// <summary>Edit rules: the host's panel, on the lobby's rules as they are.</summary>
    [RelayCommand]
    private void EditRules()
    {
        if (_session is not { IsHost: true, Lobby: { } lobby } session)
        {
            return;
        }
        HostEditor = new LobbyRulesEditor(lobby.Rules, session.QuickPlayRules, PackNames(), LobbyName, session.CustomLocked, editing: true);
        HostRulePanels = Panels(HostEditor.RuleGroups);
        HostEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LobbyRulesEditor.RuleGroups) && HostEditor is { } editor)
            {
                HostRulePanels = Panels(editor.RuleGroups);   // (a preset chosen)
            }
            if (e.PropertyName == nameof(LobbyRulesEditor.Picking))
            {
                OnPropertyChanged(nameof(HostPicking));
            }
        };
        AllRulesShown = false;
        HostEditing = true;
    }

    [RelayCommand]
    private void CancelRules() => HostEditing = false;

    [RelayCommand]
    private async Task SaveRulesAsync()
    {
        if (_session is not { IsHost: true } session || HostEditor is not { } editor)
        {
            return;
        }
        var name = editor.NameValid && editor.Name != LobbyName ? editor.Name : null;
        var password = editor.PasswordRow.Text;
        await session.SetLobbyRulesAsync(name, editor.Rules, password.Length > 0 ? password : null,
            clearPassword: editor.Editing && !editor.LockedRow.On && password.Length == 0).ConfigureAwait(true);
        HostEditing = false;   // (the name and the lock come back in the lobby's state)
    }

    [RelayCommand]
    private Task StartRaceAsync() => _session is { IsHost: true } session ? session.StartLobbyRaceAsync() : Task.CompletedTask;

    /// <summary>The host's kick on a player's row.</summary>
    [RelayCommand]
    private Task KickAsync(SeatRow row) =>
        _session is { IsHost: true } session && row is { Id: { } id } && id != session.PlayerId ? session.KickAsync(id) : Task.CompletedTask;

    /// <summary>A refusal of the host's or of the lobby's (rules locked, not the host), in the notice's place.</summary>
    private static (string, IReadOnlyList<NoticeValue>, string)? CustomNotice(QuickPlaySession session) =>
        session.InCustomLobby && session.LobbyRefusal is { } refusal
            ? (CustomLobbiesViewModel.RefusalText(refusal.Code), [], refusal.Message)
            : null;
}
