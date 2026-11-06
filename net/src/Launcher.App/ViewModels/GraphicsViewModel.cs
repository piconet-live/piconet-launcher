using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>The graphics cache as the window shows it (<see cref="AppState.Pipelines"/>): while it is built, a block in
/// the left column on every page (the words, the share built and a bar), and the question before
/// ray tracing is turned on with its cache not built yet (over the page: Build or Cancel). Labels and values only.</summary>
public sealed partial class GraphicsViewModel : ViewModelBase
{
    private readonly AppState _state;

    public GraphicsViewModel(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        state.PipelinesChanged += () => OnUi(Show);
        Show();
    }

    /// <summary>The cache is built: no game starts until it is done.</summary>
    [ObservableProperty]
    public partial bool Building { get; set; }

    /// <summary>The share built, 0 to 1.</summary>
    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>"Preparing graphics · 42%".</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "";

    /// <summary>The question before ray tracing is turned on.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    /// <summary>The question's words.</summary>
    public static string AskTitle => "Turn on ray tracing?";

    public static string AskLine =>
        "Ray tracing needs its graphics prepared first. It takes a few minutes, and games can't start until it's done.";

    private void Show()
    {
        var status = _state.Pipelines;
        Building = status.Phase == PipelinePhase.Building;
        Fraction = Math.Clamp(status.Fraction, 0, 1);
        Line = string.Create(CultureInfo.InvariantCulture, $"{AppState.PreparingText} · {(int)Math.Floor(Fraction * 100)}%");
        Asking = _state.RaysQuestion;
    }

    /// <summary>Build: ray tracing turned on, and its cache built.</summary>
    [RelayCommand]
    private void Build() => _state.AnswerRays(true);

    /// <summary>Cancel: ray tracing stays off.</summary>
    [RelayCommand]
    private void Cancel() => _state.AnswerRays(false);
}
