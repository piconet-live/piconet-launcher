using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>The first screen, the first thing the launcher asks for: the player's own F-Zero GX (USA) disc image,
/// checked, and the game's files prepared from it (<see cref="DiscPrep"/>); the rest of the launcher opens once both
/// are done. A launcher whose disc is verified and whose game has its files never shows it (<see cref="Passed"/>).
/// CONTINUE prepares when there is something to
/// prepare: the stage, how far it is and Cancel stand where the button was, and a preparation that was refused
/// or stopped leaves the screen as it was, with the preparation's own sentence, for another file or another try.
/// One that failed for a reason that is not the image's (the launcher's own files, the data folder) cannot be put
/// right here: the launcher opens, with the sentence as its notice and the play buttons off, so that the other
/// pages can be reached; its RACE button then reads PREPARE AGAIN and leads back here. It is also where a notice shows before there is a left column to show it (settings that
/// could not be read).</summary>
public sealed partial class DiscViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly Action _done;
    private CancellationTokenSource? _stop;
    private Task? _work;
    private bool _looking;
    private string? _reported;   // a failed preparation's sentence, given to the launcher as its notice

    /// <summary>"Bring over an older copy" (<see cref="DataMoveViewModel"/>), offered under the disc while nothing is
    /// prepared.</summary>
    public DataMoveViewModel Move { get; }

    public DiscViewModel(AppState state, Action done)
    {
        _state = state;
        _done = done;
        Move = new DataMoveViewModel(state);
        Path = state.Settings.DiscPath ?? "";
        Notice = state.Problem ?? "";
        state.Changed += () => OnUi(() => Notice = _state.Problem ?? "");
        // the stored disc still passes the first look: only the game's files keep the launcher from opening, and
        // files that were there and are damaged or another version's are said (none yet is the first start: no
        // fault). The one look the start made (AppState.Game) answers this and the play buttons.
        var verified = Verified;
        var wanted = verified ? state.Game.Wanted : null;
        Passed = verified && wanted is null;
        (Said, SaidLabel) = wanted is { State: PreparedState.Damaged or PreparedState.Stale }
            ? (wanted.Message, DiscWords.Said(wanted)) : ("", "");
    }

    /// <summary>The launcher opens without this screen: the stored disc image is verified and a game start has the
    /// game's files, as the launcher's start found them.</summary>
    public bool Passed { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Verified), nameof(Wrong))]
    public partial string Path { get; set; }

    /// <summary>What the launcher has to tell (<see cref="AppState.Problem"/>).</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    public bool Verified => DiscCheck.Check(Path.Trim()) == DiscStatus.Ok;

    /// <summary>Something is chosen and it is not the game's disc image (nothing chosen yet is no fault).</summary>
    public bool Wrong => Path.Trim().Length > 0 && !Verified;

    public string Status => DiscWords.Of(Path);

    /// <summary>The game's files are being prepared: the path cannot be changed, and the progress stands where
    /// CONTINUE was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Preparing { get; set; }

    public bool Idle => !Preparing;

    /// <summary>The stage, as a label: CHECKING, COPYING, FINISHING, or STOPPING after Cancel.</summary>
    [ObservableProperty]
    public partial string Stage { get; set; } = "";

    /// <summary>How far the preparation is, 0 to 1.</summary>
    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>The same in per cent: "42%".</summary>
    [ObservableProperty]
    public partial string Percent { get; set; } = "";

    /// <summary>The bytes written of the bytes to write: "612 MB / 1.36 GB".</summary>
    [ObservableProperty]
    public partial string Size { get; set; } = "";

    /// <summary>Cancel can be pressed: a preparation is at work and was not told to stop yet.</summary>
    [ObservableProperty]
    public partial bool CanStop { get; set; }

    /// <summary>The preparation's own sentence: why the image was refused, what failed, that it was stopped, or
    /// what is wrong with the files prepared earlier. Empty: nothing to say.</summary>
    [ObservableProperty]
    public partial string Said { get; set; } = "";

    /// <summary>What the screen shows of <see cref="Said"/>: a label; the
    /// whole sentence is its tooltip.</summary>
    [ObservableProperty]
    public partial string SaidLabel { get; set; } = "";

    partial void OnPathChanged(string value) => (Said, SaidLabel) = ("", "");   // another file: what was said of the last one is over

    /// <summary>CONTINUE: with a verified disc image, the game's files are prepared when a game start has none, and
    /// the launcher opens when they are ready. What a start has is looked at now, not as the launcher's start found
    /// it: another window of the launcher may have prepared the game meanwhile, and then nothing is left to do and
    /// the buttons that start a game are on. The path is kept once the image was taken: a refused one is not.</summary>
    [RelayCommand]
    private async Task ContinueAsync()
    {
        var image = Path.Trim();
        if (Preparing || _looking || !Verified)
        {
            return;
        }
        (Said, SaidLabel) = ("", "");
        _looking = true;
        GameLook look;
        try
        {
            look = await _state.LookForTheGame().ConfigureAwait(true);
        }
        finally
        {
            _looking = false;
        }
        string? notice = null;
        if (look.Wanted is not null)
        {
            var result = await PrepareAsync(image).ConfigureAwait(true);
            if (!result.Ready && result.Outcome != DiscPrepOutcome.Failed)
            {
                return;
            }
            // failed, and not for the image's sake: no other file and no second try would end otherwise, and
            // behind this screen are the pages that still work. The launcher opens and says it.
            notice = result.Ready ? null : result.Message;
        }
        _state.Update(s => s with { DiscPath = image });
        if (_reported is not null)
        {
            _state.Unsay(_reported);   // what an earlier try's failure said is over, or said anew
        }
        _reported = notice;
        if (notice is not null)
        {
            _state.Report(notice);
        }
        _done();
    }

    /// <summary>Cancel: the preparation stops at its next file and keeps nothing.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_stop is { } stop && CanStop)
        {
            CanStop = false;
            Stage = "STOPPING";
            stop.Cancel();
        }
    }

    /// <summary>How the preparation ended. Every ending but ready is said in the preparation's words.</summary>
    private async Task<DiscPrepResult> PrepareAsync(string image)
    {
        using var stop = new CancellationTokenSource();
        _stop = stop;
        Show(new DiscPrepProgress(DiscPrepStage.Checking, 0, 0, 0, 0, null));
        (Preparing, CanStop) = (true, true);
        try
        {
            var work = _state.PrepareDiscAsync(image, new Latest(this), stop.Token);
            _work = work;
            var result = await work.ConfigureAwait(true);
            (Said, SaidLabel) = result.Ready ? ("", "") : (result.Message, DiscWords.Said(result, image));
            return result;
        }
        finally
        {
            (_stop, _work) = (null, null);
            (Preparing, CanStop) = (false, false);
        }
    }

    private void Show(DiscPrepProgress progress)
    {
        if (!CanStop && Preparing)
        {
            return;   // told to stop: the label says so until the preparation has let go
        }
        Stage = progress.Stage switch
        {
            DiscPrepStage.Checking => "CHECKING",
            DiscPrepStage.Extracting => "COPYING",
            _ => "FINISHING",
        };
        Fraction = progress.Fraction;
        Percent = string.Create(CultureInfo.InvariantCulture, $"{(int)(progress.Fraction * 100)}%");
        Size = progress.BytesTotal <= 0 ? ""
            : $"{(progress.BytesDone < 1 << 20 ? "0 MB" : DiscPrep.Size(progress.BytesDone))} / {DiscPrep.Size(progress.BytesTotal)}";
    }

    /// <summary>The preparation's progress brought to the window's thread, the newest only: the preparation reports
    /// at every file and every block (thousands of times), and the window takes one report at a time.</summary>
    private sealed class Latest(DiscViewModel screen) : IProgress<DiscPrepProgress>
    {
        private readonly Lock _gate = new();
        private DiscPrepProgress _newest;
        private bool _waiting;

        public void Report(DiscPrepProgress value)
        {
            lock (_gate)
            {
                _newest = value;
                if (_waiting)
                {
                    return;
                }
                _waiting = true;
            }
            screen.OnUi(() =>
            {
                DiscPrepProgress newest;
                lock (_gate)
                {
                    (newest, _waiting) = (_newest, false);
                }
                if (screen.Preparing)
                {
                    screen.Show(newest);
                }
            });
        }
    }

    /// <summary>The launcher closes: a preparation at work is stopped and given a moment to remove what it had
    /// written (what it leaves never looks ready, and the next preparation clears it).</summary>
    public void Dispose()
    {
        var (stop, work) = (_stop, _work);
        try
        {
            stop?.Cancel();
            work?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception e) when (e is ObjectDisposedException or AggregateException)
        {
            // it ended in this very moment
        }
    }
}
