using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The Garage's Trail tab: the flame's two colours. They are the racer's, on whichever machine, and are kept at once.
public sealed partial class GarageViewModel
{
    /// <summary>The flame while not boosting.</summary>
    public TrailRow TrailNormal => _trailNormal ??= new TrailRow("Normal", _state.Settings.Racer.Trail?.Normal, c => SetTrail(t => t with { Normal = c }));

    /// <summary>The flame and the streak while boosting.</summary>
    public TrailRow TrailBoost => _trailBoost ??= new TrailRow("Boost", _state.Settings.Racer.Trail?.Boost, c => SetTrail(t => t with { Boost = c }));

    private TrailRow? _trailNormal, _trailBoost;

    private void SetTrail(Func<BoostTrail, BoostTrail> change) =>
        _state.Update(s =>
        {
            var trail = change(s.Racer.Trail ?? new BoostTrail());
            return s with { Racer = s.Racer with { Trail = trail.IsStock ? null : trail } };
        });
}

/// <summary>One colour of the trail: a row of swatches, the first the machine's own, and a box for any other.</summary>
public sealed partial class TrailRow : ObservableObject
{
    /// <summary>The swatches' colours: bright and plain, as the flame is added to the picture.</summary>
    private static readonly (string Label, string Hex)[] Colours =
    [
        ("Red", "#FF2814"), ("Orange", "#FF8A00"), ("Yellow", "#FFE600"), ("Green", "#28FF3C"), ("Cyan", "#00E0FF"),
        ("Blue", "#2864FF"), ("Purple", "#A040FF"), ("Pink", "#FF28C8"), ("White", "#FFFFFF"),
    ];

    private const string OwnHex = "#5A6478";

    private readonly Action<Rgb?> _changed;
    private bool _showing;

    public TrailRow(string label, Rgb? color, Action<Rgb?> changed)
    {
        Label = label;
        _changed = changed;
        Show(color);
    }

    public string Label { get; }

    [ObservableProperty]
    public partial IReadOnlyList<TrailSwatch> Swatches { get; set; } = [];

    /// <summary>The colour as typed; empty, the machine's own.</summary>
    [ObservableProperty]
    public partial string Hex { get; set; } = "";

    /// <summary>Whether the box holds a colour (or nothing).</summary>
    [ObservableProperty]
    public partial bool Valid { get; set; } = true;

    partial void OnHexChanged(string value)
    {
        if (_showing)
        {
            return;
        }
        var color = string.IsNullOrWhiteSpace(value) ? null : Rgb.Parse(value);
        Valid = color is not null || string.IsNullOrWhiteSpace(value);
        if (Valid)
        {
            Mark(color);
            _changed(color);
        }
    }

    /// <summary>A swatch pressed: its colour, or the machine's own.</summary>
    [RelayCommand]
    private void Pick(TrailSwatch swatch)
    {
        ArgumentNullException.ThrowIfNull(swatch);
        var color = swatch.Own ? null : Rgb.Parse(swatch.Hex);
        Show(color);
        _changed(color);
    }

    private void Show(Rgb? color)
    {
        _showing = true;
        Hex = color is null ? "" : Rgb.Hex(color);
        _showing = false;
        Valid = true;
        Mark(color);
    }

    private void Mark(Rgb? color)
    {
        var hex = color is null ? null : Rgb.Hex(BoostTrail.Lifted(color));
        Swatches = [new TrailSwatch("Its own", OwnHex, color is null, true),
            .. Colours.Select(c => new TrailSwatch(c.Label, c.Hex, c.Hex == hex))];
    }
}

/// <summary>A swatch of <see cref="TrailRow"/>: <paramref name="Own"/> is the machine's own colour.</summary>
public sealed record TrailSwatch(string Label, string Hex, bool Selected, bool Own = false);
