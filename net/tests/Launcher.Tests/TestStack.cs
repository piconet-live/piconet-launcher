using System.Runtime.CompilerServices;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The tests' build names no server stack, and such a build has no official server (<see cref="StackStamp"/>).
/// The tests were written for a launcher with one, as every player's is: so the whole test run is
/// a build made for the live stack, public, on the first official server's address (<see cref="StackStamp.ForAllTests"/>).
/// What a build without a stack does is tested in its own flow (<see cref="StackStamp.ForTests"/>:
/// <c>StackStampTests</c>, <c>ConnectionBoxTests</c>).</summary>
internal static class TestStack
{
    public const string Server = StackStamp.FirstServer;

    public static readonly StackStamp Live = StackStamp.From("live", Server, "public");

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "a test assembly: every test runs as a build made for a stack, before the first one reads the stamp")]
    internal static void Stamp() => StackStamp.ForAllTests(Live);
}
