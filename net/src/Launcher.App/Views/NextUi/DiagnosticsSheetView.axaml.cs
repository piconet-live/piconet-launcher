using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>Developer mode's Diagnostics. The view's own part: "Copy diagnostics" puts the panel's text on the
/// clipboard.</summary>
public sealed partial class DiagnosticsSheetView : UserControl
{
    public DiagnosticsSheetView() => AvaloniaXamlLoader.Load(this);

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsSheetViewModel diagnostics && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(diagnostics.Text()).ConfigureAwait(true);
        }
    }
}
