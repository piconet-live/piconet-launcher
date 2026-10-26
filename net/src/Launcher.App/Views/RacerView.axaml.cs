using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The player at the top of the left column. The name's menu is <see cref="NameMenu"/>; opening and closing
/// it is this view's part.</summary>
public sealed partial class RacerView : UserControl
{
    public RacerView() => AvaloniaXamlLoader.Load(this);

    private void OnMenuOpened(object? sender, EventArgs e) => this.FindControl<NameMenu>("Menu")?.Begin();

    private void OnMenuDone(object? sender, EventArgs e) => this.FindControl<Button>("NameButton")?.Flyout?.Hide();
}
