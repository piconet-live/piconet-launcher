using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The panel that prepares the game's files (<see cref="PrepareSheetViewModel"/>); the disc image is chosen
/// in the system's picker, as the first start chooses it.</summary>
public sealed partial class PrepareSheetView : UserControl
{
    public PrepareSheetView() => AvaloniaXamlLoader.Load(this);

    private async void OnChooseDisc(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PrepareSheetViewModel sheet && await DiscPicker.PickAsync(this).ConfigureAwait(true) is { } path)
        {
            sheet.Disc.Path = path;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<Button>("Prepare")?.Focus(Avalonia.Input.NavigationMethod.Directional),
                Avalonia.Threading.DispatcherPriority.Loaded);   // (the next step: the image taken, prepare)
        }
    }
}
