namespace Launcher.App;

/// <summary>
/// Parts of the launcher that are built and do nothing useful for a player yet. They are hidden, not removed: each
/// shows again when its line here says true, and nothing else hides it.
/// </summary>
public static class NotYet
{
    /// <summary>"Update automatically", the Settings tab's Launcher page (Views/SettingsView.axaml), with where the
    /// update stands: shown, as 0.5.0 is the first launcher that updates itself (settings.json "autoUpdate",
    /// <c>LauncherSettings.AutoUpdate</c>, read by <see cref="ViewModels.UpdateViewModel"/>).</summary>
    public static bool Updates => true;
}
