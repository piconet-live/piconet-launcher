using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>
/// "Bring over an older copy" (<see cref="DataMove"/>): a player who played from an unzipped copy (every build up to
/// 0.4.0) chooses its folder, and its settings, replays, game folders and prepared game files are copied into this
/// launcher's data folder; then the launcher starts again on them. Offered on the first screen and on the Settings
/// page's Launcher tab, by a launcher whose data is the player's own folder (an installed one; a player copy keeps its
/// data in itself and is not offered it). Refused while a game runs or the player is in Quick Play. The older copy is
/// left as it was, and the line says so.
/// </summary>
public sealed partial class DataMoveViewModel : ViewModelBase
{
    private readonly AppState _state;

    public DataMoveViewModel(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        Offered = !state.Where.Portable;
    }

    /// <summary>This launcher keeps its data in the player's own folder, so an older copy's can be brought over.</summary>
    public bool Offered { get; }

    /// <summary>A copy is at work.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Moving { get; set; }

    public bool Idle => !Moving;

    /// <summary>Where the copy stands, or what it did; empty: nothing yet.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Copies the older copy at <paramref name="chosen"/> (its folder, or its data folder) into this
    /// launcher's data folder, on another thread; when it is all in, the launcher starts again.</summary>
    public async Task BringOverAsync(string chosen)
    {
        ArgumentException.ThrowIfNullOrEmpty(chosen);
        if (Moving)
        {
            return;
        }
        if (_state.Racing || _state.InQuickPlay || _state.Joining)
        {
            Text = "Close the game and leave Quick Play first";
            return;
        }
        if (DataMove.DataOf(chosen) is not { } from)
        {
            Text = DataMove.NoDataText;
            return;
        }
        Moving = true;
        Text = "Copying";
        var progress = new Progress<DataMoveProgress>(p =>
            Text = p.BytesTotal > 0 ? $"Copying · {p.BytesDone * 100 / p.BytesTotal}%" : "Copying");
        var result = await Task.Run(() => DataMove.Copy(from, _state.DataFolder, progress)).ConfigureAwait(true);
        if (!result.Done)
        {
            (Moving, Text) = (false, result.Message);
            return;
        }
        // the settings are the older copy's now: this run writes none over them, and the launcher starts again on them
        _state.HoldSaves();
        Text = result.Message + " · starting again";
        if (!Relaunch.AgainNow(line => Program.Faults?.Note(line)))
        {
            (Moving, Text) = (false, result.Message + " · close the launcher and start it again");
        }
    }
}
