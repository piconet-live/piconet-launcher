using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// The panel that prepares the game's files from the player's disc image, wherever the launcher finds them missing,
/// damaged or another version's after its first start (the hub's "Prepare the game", Settings' "Game files"): the
/// disc image (another one may be chosen here), "Prepare game files", and while it works the stage, how far, the bar
/// and Cancel; a preparation that was refused or failed says why, for another try. Its work is the first screen's
/// (<see cref="DiscViewModel"/>, one of its own); done, the panel closes and the game can start. The panel closed, or
/// its page left, stops a preparation at work, as Cancel does. A game folder the settings name by hand (a development
/// start's) that holds no game would keep the launcher from ever preparing one: the panel lets it go, and the
/// launcher's own prepared folder is used from then on.
/// </summary>
public sealed class PrepareSheetViewModel : ViewModelBase, IOwnedSheet, IDisposable
{
    public PrepareSheetViewModel(AppState state, ViewModelBase owner, Action done)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
        if (EmptyOwnFolder(state))
        {
            state.Update(s => s with { Recomp = s.Recomp! with { GameFolder = null } });
            _ = state.LookForTheGame();   // (the preparation looks again before it starts: it finds the files wanted)
        }
        Disc = new DiscViewModel(state, done);
    }

    public ViewModelBase Owner { get; }

    /// <summary>The disc image and the preparation.</summary>
    public DiscViewModel Disc { get; }

    /// <summary>Whether the game's files have to be prepared before the game can start: missing, damaged or another
    /// version's (null: they are ready, or what is missing is not theirs to fix, as the game program).</summary>
    public static bool Wanted(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Game.Wanted is not null || EmptyOwnFolder(state);
    }

    /// <summary>The settings name a game folder of their own, and it holds no game: nothing is ever prepared while it
    /// is named (<see cref="RecompLaunch.PreparationWanted"/>), and no game starts from it.</summary>
    private static bool EmptyOwnFolder(AppState state) =>
        state.Settings.Recomp?.GameFolder is not null && state.Game.Problem == DiscPrep.NotPreparedMessage;

    /// <summary>The game's files in a word, for Settings: ready, not prepared, to prepare again, or why the game
    /// cannot start for another reason.</summary>
    public static string Words(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var look = state.Game;
        return look.Wanted switch
        {
            { State: PreparedState.NotPrepared } => "Not prepared",
            { } => "Prepare again",
            null when EmptyOwnFolder(state) => "Not prepared",
            null => look.Problem ?? "Ready",
        };
    }

    public void Dispose() => Disc.Dispose();
}
