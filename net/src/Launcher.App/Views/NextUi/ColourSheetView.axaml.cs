using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Garage's colours panel (its view model says what it shows), in two steps: a step to the mix
/// puts the focus on the colour as hex, a step back on "Mix your own colour".</summary>
public sealed partial class ColourSheetView : UserControl
{
    private ColourSheetViewModel? _sheet;

    public ColourSheetView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_sheet is not null)
            {
                _sheet.PropertyChanged -= OnSheetChanged;
            }
            _sheet = DataContext as ColourSheetViewModel;
            if (_sheet is not null)
            {
                _sheet.PropertyChanged += OnSheetChanged;
            }
        };
    }

    private void OnSheetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ColourSheetViewModel.Mixing) || _sheet is not { } sheet)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Control>(sheet.Mixing ? "HexBox" : "Mix") is { IsEffectivelyVisible: true } to)
            {
                to.Focus(NavigationMethod.Directional);
                to.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }
}
