using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>A panel of the redesigned Garage (its view model says what it shows).</summary>
public sealed partial class SortSheetView : UserControl
{
    public SortSheetView() => AvaloniaXamlLoader.Load(this);
}
