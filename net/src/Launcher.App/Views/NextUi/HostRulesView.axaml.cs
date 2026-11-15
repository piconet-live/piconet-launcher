using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views.NextUi;

/// <summary>The host's own lobby's rules as an option page.</summary>
public sealed partial class HostRulesView : UserControl
{
    public HostRulesView() => AvaloniaXamlLoader.Load(this);
}
