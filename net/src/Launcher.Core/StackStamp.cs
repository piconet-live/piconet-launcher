using System.Reflection;

namespace Launcher.Core;

/// <summary>
/// The server stack this build belongs to (two stacks, live and test; a build is made for one
/// of them and its official server is that stack's coordinator). It is stamped when a copy is made, as three build
/// properties of this project: <c>-p:ServerStack=test -p:StackServer=wss://HOST/ws -p:StackKind=test</c> (or
/// <c>public</c>); the private release path passes them for a stack (piconet-runner's <c>package.sh</c>, STACK,
/// STACK_ADDRESS and KIND). A build that names no stack has **no official server** (the
/// public source names none; a build from it connects only where its player points it): its players see the "No
/// server" line and the way to Settings, as after settings that could not be read.
/// </summary>
/// <param name="Name">The stack's name as the operators say it ("live", "test"); null: none stamped.</param>
/// <param name="Server">The stack's coordinator: this build's official server; null: none (no stack stamped).</param>
/// <param name="Test">A test build: made for testers, not for the players. Whatever is not stamped "public" on a
/// stamped build is one, so a slip never passes a test build off as the players' one.</param>
public sealed record StackStamp(string? Name, string? Server, bool Test)
{
    /// <summary>The first official coordinator's address, which launchers up to 0.4.0 had built in and wrote into
    /// their settings as "the official server": a settings file that names it loads as blank, this build's official
    /// server (<see cref="LauncherSettings.FormerDefaultServers"/>). No build takes it as its own. The players' server
    /// now has another address, stamped when a release is made.</summary>
    public const string FirstServer = "wss://piconet-coordinator.fly.dev/ws";

    /// <summary>The second official coordinator's address, of launchers from 0.5.0 to 0.5.3: as
    /// <see cref="FirstServer"/>, it loads as blank. That server no longer exists.</summary>
    public const string SecondServer = "wss://piconet-b-coordinator.fly.dev/ws";

    /// <summary>This build's (in tests: the one they name, <see cref="ForTests"/>, else <see cref="ForAllTests"/>).</summary>
    public static StackStamp Current => InFlow.Value ?? _allTests ?? Built;

    private static readonly StackStamp Built = From(Stamp("ServerStack"), Stamp("StackServer"), Stamp("StackKind"));

    private static volatile StackStamp? _allTests;

    /// <summary>Tests only, once, before any test reads the stamp (a test assembly's module initializer): the stamp
    /// of every flow that names none of its own (<see cref="ForTests"/>). A launcher never calls it.</summary>
    public static void ForAllTests(StackStamp stamp) => _allTests = stamp ?? throw new ArgumentNullException(nameof(stamp));

    private static readonly AsyncLocal<StackStamp?> InFlow = new();

    /// <summary>Tests only: <see cref="Current"/> is <paramref name="stamp"/> in the calling flow (this thread and
    /// what it awaits or starts) until the result is disposed; every other flow keeps its own.</summary>
    public static IDisposable ForTests(StackStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        var before = InFlow.Value;
        InFlow.Value = stamp;
        return new Restore(before);
    }

    private sealed class Restore(StackStamp? before) : IDisposable
    {
        public void Dispose() => InFlow.Value = before;
    }

    /// <summary>The stack of a build stamped so; an address that is none belongs to no stack (the build itself
    /// refuses to be made with one: Launcher.Core.csproj).</summary>
    public static StackStamp From(string? name, string? server, string? kind) =>
        string.IsNullOrWhiteSpace(name) || !SettingsCheck.IsServer(server) ? new(null, null, false)
            : new(name.Trim(), server!.Trim(), !string.Equals(kind?.Trim(), "public", StringComparison.Ordinal));

    /// <summary>The connection box's word for this build's official server, "Test Server" on a
    /// test build; "no official server" on a build that names no stack (whose box shows no server).</summary>
    public string ServerLabel => Name is null ? "no official server" : Test ? "Test Server" : "Official Server";

    /// <summary>What marks a test build wherever the build is named (the connection box, the window's title); null
    /// on any other.</summary>
    public string? Mark => Test ? "TEST BUILD" : null;

    private static string? Stamp(string key) => typeof(StackStamp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value;
}
