using System.Reflection;

namespace TestSupport;

/// <summary>
/// The repository the tests were built from (this file is compiled into each test project): its root folder,
/// as the project file wrote it into the assembly (<c>AssemblyMetadata</c> "RepositoryFolder", from the project's own folder). The tests do not climb from where
/// they run, which is not inside the repository once the build output is elsewhere (dotnet's
/// <c>--artifacts-path</c>, a build output on another drive).
/// </summary>
public static class RepositoryFolder
{
    /// <summary>The repository's folder.</summary>
    public static string Root { get; } = typeof(RepositoryFolder).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepositoryFolder").Value!;

    /// <summary>A path inside the repository, from its parts (<c>"deploy", "fly"</c>).</summary>
    public static string Of(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>The protocol's checkout (<c>AssemblyMetadata</c> "ProtocolFolder", from <c>ProtocolFolder</c> in
    /// net/Directory.Build.props): the folder that holds <c>protocol/</c> (its generated files, its stat packs) and
    /// the protocol's handbook pages. By default the protocol's submodule,
    /// external/protocol.</summary>
    public static string ProtocolRoot { get; } = typeof(RepositoryFolder).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "ProtocolFolder").Value!;

    /// <summary>A path inside the protocol's checkout, from its parts (<c>"protocol", "packs"</c>).</summary>
    public static string Protocol(params string[] parts) => Path.Combine([ProtocolRoot, .. parts]);
}
