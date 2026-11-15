using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// More › About: the launcher's version (and a test build's mark), the update as it stands with the one action that
/// fits, "Update automatically", "Developer mode", "What's new" (this version's release notes in the browser) and
/// "Licences" (a panel). "Experimental UI" is Settings' alone, Launcher (the owner, 2026-11-15). The switches are the Settings page's own
/// (<see cref="SettingsViewModel"/>), the update the left column's (<see cref="UpdateViewModel"/>): nothing is kept
/// twice.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly ToggleRow[] _switches;

    public AboutViewModel(MainViewModel main, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        (_main, _shell) = (main, shell);
        var settings = main.Settings;
        _switches =
        [
            new ToggleRow("Update automatically", () => settings.AutoUpdate, on => settings.AutoUpdate = on, note: () => "Never while a game runs"),
            new ToggleRow("Developer mode", () => settings.DeveloperMode, on => settings.DeveloperMode = on,
                note: () => "Build, connection, input and file details"),
        ];
        Switches = [new OptionGroup("", [.. _switches.Select(r => new OptionItem(r, "About"))])];
        ShowSwitches();
        settings.PropertyChanged += OnSettings;
    }

    /// <summary>"Piconet launcher, Version 0.6.5 · build 3934d68 · for F-Zero GX (USA)".</summary>
    public static string VersionLine => $"Piconet launcher · {BuildInfo.Text} · for F-Zero GX (USA)";

    /// <summary>The mark of a test build ("" on any other).</summary>
    public string BuildMark => _main.Account.BuildMark;

    public bool HasBuildMark => _main.Account.HasBuildMark;

    public UpdateViewModel Update => _main.Update;

    /// <summary>The two switches, as option rows.</summary>
    public IReadOnlyList<OptionGroup> Switches { get; }

    /// <summary>The release notes of this version, on the launcher's releases page.</summary>
    public static string ReleaseNotes => Branding.ReleaseNotes(BuildInfo.Version);

    /// <summary>"Licences": a panel with them.</summary>
    [RelayCommand]
    private void OpenLicences() => _shell.Sheet = new LicencesSheetViewModel(this);

    public void Dispose() => _main.Settings.PropertyChanged -= OnSettings;

    private void OnSettings(object? sender, PropertyChangedEventArgs e) => ShowSwitches();

    /// <summary>The switches read again: one changed elsewhere (the Settings page, the question before the new
    /// interface), and the row follows.</summary>
    private void ShowSwitches()
    {
        foreach (var row in _switches)
        {
            row.Show();
        }
    }
}
