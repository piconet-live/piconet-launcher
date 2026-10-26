using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public sealed partial class SettingsView : UserControl
{
    private SettingsViewModel? _settings;

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_settings is not null)
            {
                _settings.SoundSet -= Hear;
            }
            _settings = DataContext as SettingsViewModel;
            if (_settings is not null)
            {
                _settings.SoundSet += Hear;
            }
        };
    }

    /// <summary>The launcher's volume was set: one of its tones at that volume, to hear what was set.</summary>
    private void Hear()
    {
        if (_settings is { } settings)
        {
            Attention.Sound(Launcher.Core.LobbyMoment.CourseVote, settings.Sounds);
        }
    }

    /// <summary>Enter in the server's box sets what it holds; while the player types nothing is set, and so
    /// nothing is contacted (the page's view model, <see cref="Launcher.Core.ServerChoice"/>).</summary>
    private void OnServerKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && DataContext is SettingsViewModel vm)
        {
            vm.CommitServerCommand.Execute(null);
        }
    }

    /// <summary>Leaving the box sets nothing: the page is told, and the rule says so (a click on a button under
    /// the box leaves it first).</summary>
    private void OnServerLeft(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.LeftServer();
        }
    }

    /// <summary>"Bring over an older copy": its folder by the system's folder picker (a view concern); the page
    /// checks and copies it (<see cref="DataMoveViewModel"/>).</summary>
    private async void OnBringOver(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && await FolderPicker.PickAsync(this, FolderPicker.OlderCopyTitle).ConfigureAwait(true) is { } folder)
        {
            await vm.Move.BringOverAsync(folder).ConfigureAwait(true);
        }
    }

    /// <summary>The disc image by the system's file picker (a view concern); the page checks what was picked.</summary>
    private async void OnChooseDisc(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && await DiscPicker.PickAsync(this).ConfigureAwait(true) is { } path)
        {
            vm.DiscPath = path;
        }
    }
}
