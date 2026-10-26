using System.Reflection;

namespace Launcher.App;

/// <summary>The launcher's version and build, for the sidebar's corner. The version is net/Directory.Build.props's
/// (bumped deliberately, CHANGELOG.md); the build is the git commit stamped into the informational version when it was
/// built (Launcher.App.csproj's StampBuildCommit: "0.1.0+3934d68"). A build made outside git has no build.</summary>
public static class BuildInfo
{
    private static readonly string[] Stamp = (typeof(BuildInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+', 2);

    /// <summary>The launcher's version, e.g. 0.1.0.</summary>
    public static string Version { get; } = Stamp[0];

    /// <summary>The commit it was built from (short), or null.</summary>
    public static string? Build { get; } = Stamp is [_, { Length: > 0 } commit] ? commit : null;

    /// <summary>As players read it: "Version 0.5.0 Beta · build 3934d68" (the stage is
    /// <see cref="Branding.Stage"/>).</summary>
    public static string Text { get; } =
        (Branding.Stage.Length == 0 ? $"Version {Version}" : $"Version {Version} {Branding.Stage}") +
        (Build is null ? "" : $" · build {Build}");
}
