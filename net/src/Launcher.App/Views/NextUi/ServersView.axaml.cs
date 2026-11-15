using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's "Choose a server": the pad lands on the server last used.</summary>
public sealed partial class ServersView : UserControl, IPadLanding
{
    public ServersView() => AvaloniaXamlLoader.Load(this);

    /// <summary>The row of the server set (the one last used), else none (the pad takes the page's first).</summary>
    public Control? Landing() =>
        DataContext is ServersViewModel { Current: { } current }
            ? this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext == current && b.Classes.Contains("line"))
            : null;
}
