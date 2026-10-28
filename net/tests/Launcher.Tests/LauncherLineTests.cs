using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The launcher's release line (the owner's rule: every build with the same first two version numbers races
/// together): it is its version's first two numbers, it goes in the hello, and players of another line are told by
/// their version rather than by a build's parts.</summary>
public sealed class LauncherLineTests
{
    /// <summary>The line is the first two numbers of the version every project of the launcher carries
    /// (net/Directory.Build.props).</summary>
    [Fact]
    public void TheLineIsTheFirstTwoNumbersOfTheLaunchersVersion()
    {
        var version = typeof(LauncherLine).Assembly.GetName().Version!;
        Assert.Equal($"{version.Major}.{version.Minor}", LauncherLine.Own);
        Assert.True(VersionLine.IsLine(LauncherLine.Own));
    }

    [Fact]
    public async Task TheHelloNamesTheLine()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new LedServer();
        await using var client = await CoordinatorClient.ConnectAsync(server.Address, "ANA", build: "i/m/r", cancel: cancel);
        for (var i = 0; i < 500 && server.Of<Hello>().Count == 0; i++)
        {
            await Task.Delay(20, cancel);
        }
        var hello = Assert.Single(server.Of<Hello>());
        Assert.Equal(("i/m/r", LauncherLine.Own), (hello.Build, hello.Line));
    }

    [Fact]
    public void PlayersOfAnotherLineAreToldByTheirVersion()
    {
        OtherBuild[] others = [new OtherBuild("a/x/c", 3, "0.6"), new OtherBuild("a/y/c", 1)];
        var (label, values) = QuickPlaySession.OtherBuildsValues("a/b/c", others);
        Assert.Equal("4 players online on another version", label);
        Assert.Equal([("theirs", "version 0.6"), ("native build · yours", "b"), ("theirs", "y")], values);
        Assert.Equal("4 players online on another version, not matched with you: version 0.6; the game's native build differs (yours b, theirs y)",
            QuickPlaySession.OtherBuildsText("a/b/c", others));
    }

    /// <summary>A line the server tells of is shown only when it is one: anything else is passed over, and the entry
    /// is said by its build as before lines.</summary>
    [Fact]
    public void AnOtherBuildsLineThatIsNoLineIsPassedOver()
    {
        var lobby = new LobbyState("l1", [], 30, 2, false, 0, 0, OtherBuilds: [new OtherBuild("a/x/c", 2, "0.6"), new OtherBuild("a/y/c", 1, "<b>0.6</b>")]);
        var got = Assert.IsType<LobbyState>(ServerMessages.Checked(ControlProtocol.Encode(lobby)));
        Assert.Equal([new OtherBuild("a/x/c", 2, "0.6"), new OtherBuild("a/y/c", 1)], got.OtherBuilds);
    }
}
