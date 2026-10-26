using CommunityToolkit.Mvvm.ComponentModel;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The Garage's stats lens (a stat pack is a lens on the machines, never extra cards): outside a
// lobby the Stats choice is the disc's own stats or one of the packs the player has (the launcher's own pack files,
// AppState.PracticePackList); in a lobby it is the lobby's, fixed. Every value shown, the order and the chips go
// through it; a value it changes is marked, with the stock value beside it. The builder's custom machines too.
public sealed partial class GarageViewModel
{
    private readonly IReadOnlyList<StatPack> _packs;
    private StatPack? _lobbyPack;
    private string _lobbyPackLabel = "";
    private bool _inLobby;

    /// <summary>The Stats choice: "Stock", then each pack the player has, by name.</summary>
    public IReadOnlyList<string> Lenses { get; private set; } = ["Stock"];

    /// <summary>Which of <see cref="Lenses"/> the stats are seen through outside a lobby.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LensChoice))]
    public partial int LensIndex { get; set; }

    /// <summary>The list's own index: -1 while it is being filled is no choice.</summary>
    public int LensChoice
    {
        get => LensIndex;
        set => LensIndex = value < 0 ? LensIndex : value;
    }

    /// <summary>In a lobby: the stats are the lobby's, and no choice is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LensFree))]
    public partial bool LensFixed { get; set; }

    public bool LensFree => !LensFixed;

    /// <summary>The lobby's stats, named: its pack and author ("GX Unleashed · AKC12"), or "Stock".</summary>
    [ObservableProperty]
    public partial string LensFixedText { get; set; } = "Stock";

    /// <summary>Whose numbers they are when they are a stat pack's, by name and author ("GX Unleashed · AKC12");
    /// "" for the disc's own.</summary>
    [ObservableProperty]
    public partial string PackText { get; set; } = "";

    /// <summary>The pack the stats are seen through; null: the disc's own.</summary>
    public StatPack? LensPack => _inLobby ? _lobbyPack : LensIndex > 0 && LensIndex <= _packs.Count ? _packs[LensIndex - 1] : null;

    partial void OnLensIndexChanged(int value)
    {
        var name = value > 0 && value <= _packs.Count ? _packs[value - 1].Name : null;
        if (_state.Settings.GarageStats != name)
        {
            _state.Update(s => s with { GarageStats = name });   // (the choice the page opens on)
        }
        ShowLens();
    }

    /// <summary>The choice's list, and the pack last chosen (when the launcher still has it).</summary>
    private void LoadLenses()
    {
        Lenses = ["Stock", .. _packs.Select(p => p.Name)];
        var last = _packs.ToList().FindIndex(p => p.Name == _state.Settings.GarageStats);
        LensIndex = last + 1;
    }

    /// <summary>The lens's label and values everywhere: the stats, the order, the chips, the builder.</summary>
    private void ShowLens()
    {
        var pack = LensPack;
        LensFixed = _inLobby;
        LensFixedText = _lobbyPack is null ? "Stock" : _lobbyPackLabel.Length > 0 ? _lobbyPackLabel : _lobbyPack.Name;
        PackText = pack is null ? "" : _inLobby ? LensFixedText : pack.Author is { Length: > 0 } author ? $"{pack.Name} · {author}" : pack.Name;
        Builder.Pack = pack;
        ShowSort();   // the values, and the cells' order and filter when they stand by a stat
    }

    /// <summary>A lobby's pack and its label, or none (out of a lobby, or a lobby on the disc's stats).</summary>
    private void LensOfLobby(bool inLobby, StatPack? pack, string label)
    {
        if (inLobby == _inLobby && pack?.Fingerprint == _lobbyPack?.Fingerprint && label == _lobbyPackLabel)
        {
            return;
        }
        (_inLobby, _lobbyPack, _lobbyPackLabel) = (inLobby, inLobby ? pack : null, inLobby ? label : "");
        ShowLens();
    }
}
