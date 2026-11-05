using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Launcher.App.Views;

/// <summary>Play's Custom lobbies page: the browser, and Create. A locked lobby's password strip takes the keyboard
/// as it opens, so the password can be typed at once and Enter joins. Create's From file… opens the system's file picker
/// and hands the host's mode file to the mode's row.</summary>
public sealed partial class CustomLobbiesView : UserControl
{
    public CustomLobbiesView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<Control>("PasswordStrip") is { } strip && this.FindControl<TextBox>("PasswordBox") is { } box)
        {
            strip.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && strip.IsVisible)
                {
                    Dispatcher.UIThread.Post(() => box.Focus());
                }
            };
        }
    }

    private async void OnModeFromFile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && DataContext is ViewModels.CustomLobbiesViewModel { Editor: { } editor } &&
            await StatPackPicker.PickModeAsync(button).ConfigureAwait(true) is { } path)
        {
            editor.ModeRow.Load(path);
        }
    }
}
