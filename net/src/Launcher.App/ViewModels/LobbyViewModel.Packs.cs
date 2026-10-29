using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The lobby's mixed packs (the protocol's handbook/stat-pack-constants.md, "A pack a seat"): in a lobby whose server
/// has the rule mixed-packs on (<see cref="LobbyState.SeatPacks"/>), the machine stage lets the player pick the pack
/// their machine races: the lobby's own, the original stats, or one the server offers. The racers' list names each
/// racer's own.
/// </summary>
public sealed partial class LobbyViewModel
{
    private IReadOnlyList<StatPackName> _seatPacks = [];
    private bool _showingPacks;

    /// <summary>The picker's lines: "Lobby's pack", "Original", then the server's packs by name.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> SeatPackNames { get; set; } = [];

    [ObservableProperty]
    public partial int SeatPackIndex { get; set; }

    /// <summary>The lobby offers packs a racer: the picker is on the machine stage.</summary>
    [ObservableProperty]
    public partial bool SeatPacksShown { get; set; }

    /// <summary>The picker's lines and choice, from the lobby's offer and the session's choice.</summary>
    private void ShowSeatPacks(QuickPlaySession session)
    {
        var offered = session.Lobby?.SeatPacks ?? [];
        _showingPacks = true;
        if (!_seatPacks.SequenceEqual(offered))
        {
            _seatPacks = offered;
            SeatPackNames = ["Lobby's pack", "Original", .. offered.Select(p => p.Name)];
        }
        SeatPackIndex = session.SeatPack switch
        {
            null => 0,
            "" => 1,
            var f => _seatPacks.Select(p => p.Fingerprint).ToList().IndexOf(f) is var i and >= 0 ? i + 2 : 0,
        };
        SeatPacksShown = offered.Count > 0 && session.ChoiceOpen;
        _showingPacks = false;
    }

    partial void OnSeatPackIndexChanged(int value)
    {
        if (_showingPacks || _session is not { } session || value < 0)
        {
            return;
        }
        var fingerprint = value switch { 0 => null, 1 => "", var i when i - 2 < _seatPacks.Count => _seatPacks[i - 2].Fingerprint, _ => null };
        _ = session.ChooseSeatPackAsync(fingerprint);
    }

    /// <summary>A racer's machine as the racers' list writes it (<see cref="RacedOf"/>): with their own pack, when
    /// they race one.</summary>
    private string MachineWithPack(PlayerInfo p, QuickPlaySession session) =>
        Theme.GameSkin.NameOf(RacedOf(p, session), () => OnUi(Refresh)) + (p.Pack is { Length: > 0 } pack ? " · " + pack : "");

    /// <summary>The machine a racer races. The player's own row: the custom machine the server was told of
    /// (<see cref="QuickPlaySession.Sent"/>) on the stock id it has. Another racer's: their custom machine as the
    /// lobby's state names it (<see cref="PlayerInfo.Custom"/>; a server from before names none: the stock machine).</summary>
    internal static RacedMachine RacedOf(PlayerInfo p, QuickPlaySession session) =>
        new(Math.Clamp(p.Machine, 0, Machines.Names.Count - 1),
            p.Id == session.PlayerId ? session.Sent?.Custom : p.Custom);
}
