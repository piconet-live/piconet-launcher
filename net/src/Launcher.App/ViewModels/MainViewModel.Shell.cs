using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.ViewModels;

// Which of the launcher's two interfaces the window shows (Views/MainWindow.axaml.cs: the classic one, or with
// "Experimental UI" on the redesigned one of Views/NextUi), and whether a developer's data shows. Both are settings
// (Settings, Launcher, "Try something new"); the pages, the state and the settings are the same under either.
public sealed partial class MainViewModel
{
    private ShellViewModel? _shell;

    /// <summary>The redesigned interface shows (<see cref="Launcher.Core.LauncherSettings.ExperimentalUi"/>; off by
    /// default: the classic one).</summary>
    [ObservableProperty]
    public partial bool ExperimentalUi { get; set; }

    /// <summary>A developer's data shows at the places it belongs to
    /// (<see cref="Launcher.Core.LauncherSettings.DeveloperMode"/>), in both interfaces.</summary>
    [ObservableProperty]
    public partial bool DeveloperMode { get; set; }

    /// <summary>The redesigned interface's shell: its areas, the way back, the hints (made when the redesigned
    /// interface first shows).</summary>
    public ShellViewModel Shell => _shell ??= new ShellViewModel(this, _state);

    /// <summary>The two as the settings have them now.</summary>
    private void RefreshShell()
    {
        if (_state.Settings.ExperimentalUi)
        {
            _ = Shell;   // (before the window hears of it: the shell's first page is ready)
        }
        ExperimentalUi = _state.Settings.ExperimentalUi;
        DeveloperMode = _state.Settings.DeveloperMode;
    }
}
