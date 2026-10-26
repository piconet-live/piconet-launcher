namespace Launcher.Core;

/// <summary>
/// The stand-in service: the launcher's dev mode (<c>--dev-service</c>), which runs the launcher against a service in
/// the launcher's own process, so that a contributor can click through Quick Play's pages without the real server,
/// a server stack or keys (as Slippi's launcher mocks its services). A lobby with a few racers, chat, the start vote,
/// the course vote and its draw, the machine stage; then no race: the stand-in has no race server, and says so. The
/// connection is <see cref="StandInCoordinator"/>, the status page's answers <see cref="StandInStatus"/>. Never on in
/// a player's launcher: only that command-line switch turns it on.
/// </summary>
public static class StandInService
{
    /// <summary>The address the launcher is pointed at while the stand-in runs. Nothing is ever sent to it: the
    /// connection and the status answers are made in the launcher (a name under <c>.invalid</c> is nobody's).</summary>
    public const string Address = "ws://stand-in.invalid/ws";

    /// <summary>The server's name, as the pages show it.</summary>
    public const string Name = "Stand-in service";

    /// <summary>What the player is told where a race would start.</summary>
    public const string NoRacesText = "No races on the stand-in service";

    /// <summary>How the launcher's sessions reach the stand-in (in place of <see cref="CoordinatorClient.ConnectionAsync"/>).</summary>
    public static CoordinatorConnect Connect { get; } = StandInCoordinator.ConnectAsync;

    /// <summary>The answers of the stand-in's status page and health check, for one asker's HTTP client.</summary>
    public static HttpMessageHandler Answers() => new StandInStatus();
}
