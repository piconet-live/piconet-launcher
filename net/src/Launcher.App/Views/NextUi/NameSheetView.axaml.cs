using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>A view of the redesigned interface's Replays, More and profile (its view model says what it shows).</summary>
public sealed partial class NameSheetView : UserControl
{
    public NameSheetView() => AvaloniaXamlLoader.Load(this);
}
