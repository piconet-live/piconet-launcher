using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A build belongs to a server stack (<see cref="StackStamp"/>): its official server is that stack's, and a
/// test build says so wherever the build is named. A build that names no stack (the public source's) has no official server: its player sees "No server" until they choose one.</summary>
public sealed class StackStampTests
{
    private const string Test = "wss://piconet-test-coordinator.fly.dev/ws";

    [Fact]
    public void ABuildThatNamesNoStackHasNoOfficialServer()
    {
        var none = StackStamp.From(null, null, null);
        Assert.Equal((null, (string?)null, false), (none.Name, none.Server, none.Test));
        Assert.Equal(("no official server", null), (none.ServerLabel, none.Mark));
        // a build that names none (the tests' own runs as the live stack's: TestStack): a blank server is no server, and the
        // box's blank does not mean an official one
        using var unstamped = StackStamp.ForTests(none);
        Assert.Equal(none, StackStamp.Current);
        Assert.Null(LauncherSettings.OnlineServer);
        Assert.Null(new LauncherSettings().QuickPlayServer);
        Assert.False(ServerChoice.BlankIsOfficial(new LauncherSettings()));
        // a server the player chose is used as ever
        Assert.Equal(Test, new LauncherSettings { Server = Test }.QuickPlayServer);
    }

    [Fact]
    public void ATestBuildSaysSoAndAPublicOneNamesItsStack()
    {
        var test = StackStamp.From("test", Test, "test");
        Assert.Equal(("test", Test, true), (test.Name, test.Server, test.Test));
        Assert.Equal(("Test Server", "TEST BUILD"), (test.ServerLabel, test.Mark));
        var players = StackStamp.From(" live ", $" {Test} ", "public");
        Assert.Equal(("live", Test, false), (players.Name, players.Server, players.Test));
        Assert.Equal(("Official Server", null), (players.ServerLabel, players.Mark));
    }

    /// <summary>Only the word "public" makes a stamped build the players' one: a slip is a test build.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Public")]
    [InlineData("release")]
    public void WhatIsNotStampedPublicIsATestBuild(string? kind) => Assert.True(StackStamp.From("test", Test, kind).Test);

    [Theory]
    [InlineData("test", "piconet-test-coordinator.fly.dev")]
    [InlineData("test", "https://piconet-test-coordinator.fly.dev/ws")]
    [InlineData("test", null)]
    [InlineData("", Test)]
    public void AStampThatIsNoneNamesNoStack(string name, string? server) =>
        Assert.Equal(StackStamp.From(null, null, null), StackStamp.From(name, server, "public"));

    /// <summary>The former official servers' addresses in a settings file are what older launchers wrote for "the
    /// official server": they stay that, blank, on a build made for any stack.</summary>
    [Theory]
    [InlineData(StackStamp.FirstServer)]
    [InlineData(StackStamp.SecondServer)]
    public void AFormerOfficialAddressInASettingsFileMeansTheOfficialServer(string address)
    {
        Assert.Contains(address, LauncherSettings.FormerDefaultServers);
        var folder = Path.Combine(Path.GetTempPath(), "stackstamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            store.Save(new LauncherSettings { Server = address, RecordReplays = false });
            Assert.Equal(new LauncherSettings { RecordReplays = false }, store.Load());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}

/// <summary>The build's own check of a stamp (Launcher.Core.csproj, target CheckServerStack), run alone with no
/// compile: a whole and right stamp passes, and one that is not stops the publish. A check that passed <c>wss://</c> or a
/// blank name would give a build that read its stamp as none: a launcher that looked made for its stack and was on the first
/// server. The service's tooling holds the stamps it prints for its stacks to the same patterns.</summary>
public sealed class StackStampBuildCheckTests
{
    private const string Test = "wss://piconet-test-coordinator.fly.dev/ws";

    private static readonly string Project = TestSupport.RepositoryFolder.Of("net", "src", "Launcher.Core", "Launcher.Core.csproj");

    private static async Task<(int Code, string Said)> Check(params string[] properties)
    {
        var cancel = TestContext.Current.CancellationToken;
        var host = Environment.ProcessPath is { } running && Path.GetFileNameWithoutExtension(running) == "dotnet" ? running : "dotnet";
        var start = new System.Diagnostics.ProcessStartInfo(host)
        {
            WorkingDirectory = Path.GetDirectoryName(Project)!, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var word in (string[])["msbuild", Project, "-t:CheckServerStack", "-nologo", "-v:q", "-nodeReuse:false", "--disable-build-servers", .. properties])
        {
            start.ArgumentList.Add(word);
        }
        using var msbuild = System.Diagnostics.Process.Start(start)!;
        var (output, error) = (msbuild.StandardOutput.ReadToEndAsync(cancel), msbuild.StandardError.ReadToEndAsync(cancel));
        try
        {
            await msbuild.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromSeconds(120), cancel);
        }
        catch (TimeoutException)
        {
            msbuild.Kill(true);
            Assert.Fail("the build's stamp check did not end");
        }
        return (msbuild.ExitCode, await output + await error);
    }

    [Theory]
    [InlineData("test", Test, "test")]
    [InlineData("live", "wss://play.piconet.live/ws", "public")]
    public async Task AWholeStampPasses(string stack, string server, string kind)
    {
        var (code, said) = await Check($"-p:ServerStack={stack}", $"-p:StackServer={server}", $"-p:StackKind={kind}");
        Assert.True(code == 0, said);
    }

    [Theory]
    [InlineData("test", "wss://", "test", "StackServer 'wss://' is not a server's address")]
    [InlineData("test", "piconet-test-coordinator.fly.dev", "test", "is not a server's address")]
    [InlineData(" ", Test, "test", "ServerStack ' ' is not a stack's name")]
    [InlineData("test", Test, "release", "StackKind 'release' is neither test nor public")]
    [InlineData("test", Test, "Public", "StackKind 'Public' is neither test nor public")]
    public async Task AStampThatIsNotWholeAndRightStopsTheBuild(string stack, string server, string kind, string said)
    {
        var (code, output) = await Check($"-p:ServerStack={stack}", $"-p:StackServer={server}", $"-p:StackKind={kind}");
        Assert.NotEqual(0, code);
        Assert.Contains(said, output, StringComparison.Ordinal);
    }

    /// <summary>One of the three alone is a stamp too, and not a whole one.</summary>
    [Fact]
    public async Task OneOfTheThreeAloneStopsTheBuild()
    {
        var (code, output) = await Check("-p:StackKind=public");
        Assert.NotEqual(0, code);
        Assert.Contains("ServerStack '' is not a stack's name", output, StringComparison.Ordinal);
    }
}
