using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The panel of every rule of a custom lobby.</summary>
public sealed partial class RulesSheetView : UserControl
{
    public RulesSheetView() => AvaloniaXamlLoader.Load(this);
}
