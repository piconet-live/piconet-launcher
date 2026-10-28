using System.Reflection;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>This launcher's release line (<see cref="Hello.Line"/>): the first two numbers of its version
/// (net/Directory.Build.props, the version every project of the launcher carries): 0.5.2 names "0.5". The
/// coordinator puts players of one line into the same lobbies and races whatever their game's build (the owner's rule:
/// every build with the same first two numbers races together). Null: a launcher built with no version (0.0.x) or a
/// pre-release ("0.6.0-dev"), which names none and is matched by its build alone (<see cref="VersionLine.Of"/>).</summary>
public static class LauncherLine
{
    /// <summary>The line this launcher names in its hello.</summary>
    public static string? Own { get; } = VersionLine.Of(typeof(LauncherLine).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
