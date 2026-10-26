using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The Garage's Sort &amp; filter panel: its look only (GarageViewModel.SortFilter.cs is its part).</summary>
public sealed partial class SortFilterPanel : UserControl
{
    public SortFilterPanel() => AvaloniaXamlLoader.Load(this);
}
