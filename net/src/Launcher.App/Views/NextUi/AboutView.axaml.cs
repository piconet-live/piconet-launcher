using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>More › About. The view's own part: "What's new" opens the release notes in the browser.</summary>
public sealed partial class AboutView : UserControl
{
    public AboutView() => AvaloniaXamlLoader.Load(this);

    private async void OnWhatsNew(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(new Uri(AboutViewModel.ReleaseNotes)).ConfigureAwait(true);
        }
    }
}
