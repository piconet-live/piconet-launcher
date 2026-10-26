namespace Launcher.App;

/// <summary>The names players see: the launcher is "Piconet Launcher", and inside it the name is "Piconet". They
/// live only here; code and files use neutral names.</summary>
public static class Branding
{
    /// <summary>The name inside the launcher (the sidebar's lettering).</summary>
    public const string DisplayName = "Piconet";

    /// <summary>The application's name: the window title (and the program's file name in the package).</summary>
    public const string AppName = "Piconet Launcher";

    /// <summary>The release's stage, shown after the version in the sidebar's corner ("Version 0.5.0 Beta"); empty for
    /// none. The version itself stays three numbers (net/Directory.Build.props): the runner's packaging reads it from
    /// the program.</summary>
    public const string Stage = "Beta";

    /// <summary>The application's id for Windows (its AppUserModelID): the windows that carry it share one button on
    /// the taskbar, the launcher's and the window of a game it starts. Changing it parts them from a button a
    /// player pinned before.</summary>
    public const string AppId = "Piconet.Launcher";

    /// <summary>The launcher's own folder under the user's local application data.</summary>
    public const string DataFolder = "fzgx-launcher";

    /// <summary>The Discord application the launcher's status is shown as ("Playing Piconet": the application's name
    /// is the one in Discord's developer portal). Its id is public, no secret; the launcher has no token and no client
    /// secret. A value that is no application id (an empty one) turns the status off
    /// (<see cref="Launcher.Core.DiscordPresence.Available"/>).</summary>
    /// <summary>Where the launcher's updates are (Updates.cs): the newest release of the launcher's public repository,
    /// through GitHub's own "latest release" download link, which is no API call and has no rate limit. The release
    /// holds the feed (<c>releases.CHANNEL.json</c>, the channel the release was packed for) and its packages.</summary>
    public const string UpdateFeed = "https://github.com/piconet-live/piconet-launcher/releases/latest/download";

    /// <summary>The page a player gets the launcher from by hand (the installer, Setup.exe).</summary>
    public const string ReleasesPage = "https://github.com/piconet-live/piconet-launcher/releases/latest";

    public const string DiscordApplicationId = "1561510653779255416";

    /// <summary>The status's large picture (the key of the application's art, uploaded in the Discord developer portal:
    /// our own art, never the game's) and its text; the small pictures' keys are
    /// <see cref="Launcher.Core.PresenceWords"/>'.</summary>
    public const string DiscordLargeImage = "logo";

    /// <summary>The status's one button and where it leads.</summary>
    public const string DiscordButton = "Get Piconet", DiscordButtonUrl = "https://piconet.live";

    /// <summary>Everything Discord's status carries besides what the player does.</summary>
    public static Launcher.Core.PresenceBrand Discord { get; } =
        new(DiscordApplicationId, DiscordLargeImage, DisplayName, DiscordButton, DiscordButtonUrl);
}
