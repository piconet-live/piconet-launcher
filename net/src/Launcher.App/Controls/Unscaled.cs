using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.App.Controls;

/// <summary>
/// Takes the interface scale (<see cref="ScaledRoot"/>) back off what it holds: a 3D view drawn on the graphics card
/// sizes its picture by its own layout size, so under the scale it would draw a picture of the unscaled size that is
/// then stretched (blurred at 250%, twice too many pixels at 50%). Under this it is laid out at the scaled size in the
/// screen's own units and draws every pixel it shows. What it draws in layout units (the map's dots, a message) it
/// multiplies by <see cref="ScaledRoot.GetFactor"/>, which still tells the scale here.
/// </summary>
public sealed class Unscaled : LayoutTransformControl
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaledRoot.FactorProperty)
        {
            var factor = ScaledRoot.GetFactor(this);
            LayoutTransform = factor == 1 ? null : new ScaleTransform(1 / factor, 1 / factor);
        }
    }
}
