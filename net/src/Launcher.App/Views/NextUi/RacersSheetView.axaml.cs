using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The race's panel: every racer, or every result.</summary>
public sealed partial class RacersSheetView : UserControl
{
    public RacersSheetView() => AvaloniaXamlLoader.Load(this);
}
