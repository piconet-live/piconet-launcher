using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A server's rules and the course vote, as the launcher reads and words them.</summary>
public sealed class RulesTests
{
    [Fact]
    public void ThePlayersMachineUnlessTheServerDoesNotAllowIt()
    {
        var rules = new ServerRules(3, 30, CpuFillMode.Vote, CourseSelectMode.Vote, null, [4, 9], SettingRange.QuickPlay, new RaceRules(3, false, false));
        Assert.Equal(9, QuickPlaySession.QueueMachine(9, rules));
        Assert.Equal(4, QuickPlaySession.QueueMachine(2, rules));              // the nearest allowed, not the server's first
        Assert.Equal(9, QuickPlaySession.QueueMachine(30, rules));
        Assert.Equal(4, QuickPlaySession.QueueMachine(6, rules with { AllowedMachines = [4, 8] }));   // as near: the lower
        Assert.Equal(2, QuickPlaySession.QueueMachine(2, null));
    }

    /// <summary>The mines' rule on the lobby's rules line, only when it is not Quick Play's 0.2 s: the seconds a mine
    /// stays gone, or that it stays gone (0, a server from before the rule too: a welcome without it reads as 0).</summary>
    [Fact]
    public void TheMinesRuleIsSaidOnlyWhenItIsNotQuickPlays()
    {
        Assert.Equal([null, "mines back after 0.05 s", "mines back after 0.5 s", "mines back after 2.25 s", "mines stay gone", "mines stay gone"],
            new[] { 200, 50, 500, 2250, 0, -50 }.Select(Launcher.App.ViewModels.LobbyViewModel.MinesRule));
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<RaceRules>("{\"cpuLevel\":3}", ControlProtocol.Json)!.MineRegenMs);
    }

    private static readonly int[] PingLimits = [250, 150, 300, 30];

    /// <summary>The lobby's latency limit on the rules line, only when it is not Quick Play's 250 ms.</summary>
    [Fact]
    public void ThePingLimitIsSaidOnlyWhenItIsNotQuickPlays()
    {
        Assert.Equal([null, "ping limit 150 ms", "ping limit 300 ms", "ping limit 30 ms"],
            PingLimits.Select(Launcher.App.ViewModels.LobbyViewModel.PingRule));
    }

    /// <summary>The machine setting's rule on the lobby's rules line: said only when it is not Quick Play's (100 for
    /// everyone), as a range, one setting, or each player's own.</summary>
    [Fact]
    public void TheSettingRuleIsSaidAsARangeWhenItIsNotQuickPlays()
    {
        Assert.Equal([null, "setting 80-100", "setting 50", "your own setting"],
            new[] { SettingRange.Only(100), new SettingRange(80, 100), SettingRange.Only(50), SettingRange.Free }
                .Select(Launcher.App.ViewModels.LobbyViewModel.SettingRule));
    }

    /// <summary>A server's machine-setting rule as the launcher reads it: low and high, as sent; one that is no range
    /// (low over high, an end outside 0 to 100, none) is each player's own, which the server clamps.</summary>
    [Theory]
    [InlineData("""{"low":80,"high":100}""", 80, 100)]
    [InlineData("""{"low":100,"high":100}""", 100, 100)]
    [InlineData("""{"low":90,"high":80}""", 0, 100)]
    [InlineData("""{"low":-5,"high":80}""", 0, 100)]
    [InlineData("""{"low":20,"high":101}""", 0, 100)]
    [InlineData("null", 0, 100)]
    public void TheSettingRangeIsReadAsSentOrAsEachPlayersOwn(string range, int low, int high)
    {
        var welcome = """{"type":"welcome","playerId":"p1","serverName":"Den","version":3,"rules":{"laps":3,"racers":30,""" +
            """ "cpuFill":"Vote","courseSelect":"Vote","allowedMachines":[1],"machineSetting":""" + range +
            ""","race":{}}}""";
        var rules = Assert.IsType<Welcome>(ServerMessages.Checked(welcome)).Rules!;
        Assert.Equal(new SettingRange(low, high), rules.MachineSetting);
        Assert.Equal(Math.Clamp(50, low, high), rules.MachineSetting.Clamp(50));
    }

    /// <summary>The lobby's latency limit as the launcher reads it: as sent inside the rule's 30 to 300 ms; a server from
    /// before the rule sends none, which reads as Quick Play's 250.</summary>
    [Theory]
    [InlineData("", 250)]
    [InlineData(""","latencyLimitMs":150""", 150)]
    [InlineData(""","latencyLimitMs":1000""", 300)]
    [InlineData(""","latencyLimitMs":5""", 30)]
    public void TheLatencyLimitIsReadInsideItsRange(string member, int ms)
    {
        var welcome = """{"type":"welcome","playerId":"p1","serverName":"Den","version":3,"rules":{"laps":3,"racers":30,""" +
            """ "cpuFill":"Vote","courseSelect":"Vote","allowedMachines":[1],"machineSetting":null,"race":{}""" +
            member + "}}";
        Assert.Equal(ms, Assert.IsType<Welcome>(ServerMessages.Checked(welcome)).Rules!.LatencyLimitMs);
    }

    /// <summary>The operator changed the server's rules while the player sits in a lobby: the session takes the new
    /// rules at once (what the lobby and home screens show), checked as a welcome's are; one with no rules is dropped.</summary>
    [Fact]
    public async Task ChangedRulesReplaceTheWelcomesAtOnce()
    {
        var cancel = TestContext.Current.CancellationToken;
        var before = new ServerRules(3, 30, CpuFillMode.Vote, CourseSelectMode.Vote, null, [4, 9], SettingRange.QuickPlay, new RaceRules(3, false, false));
        var after = before with { Laps = 5, CpuFill = CpuFillMode.Always, AllowedMachines = [7, 999], MachineSetting = new SettingRange(80, 100) };
        await using var server = new FakeCoordinator(
            ControlProtocol.Encode(new Welcome("p1", "Den", ControlProtocol.Version, before)),
            FakeCoordinator.Lobby,
            """{"type":"rules"}""",
            ControlProtocol.Encode(new RulesChanged(after)),
            FakeCoordinator.Lobby.Replace("\"l1\"", "\"l2\"", StringComparison.Ordinal));
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(Path.GetTempPath(), "rules-tests-" + Guid.NewGuid().ToString("N")), _ => { });
        await session.JoinAsync(server.Address, cancel: cancel);
        while (session.Lobby?.LobbyId != "l2")
        {
            await Task.Delay(10, cancel);
        }

        var rules = session.Rules!;
        Assert.Equal((5, CpuFillMode.Always, new SettingRange(80, 100)), (rules.Laps, rules.CpuFill, rules.MachineSetting));
        Assert.Equal([7], rules.AllowedMachines);   // (a machine the game does not have is left out, as at the welcome)
        Assert.Null(ServerMessages.Checked("""{"type":"rules"}"""));
    }

    [Theory]
    [InlineData("latency", "Connection too slow for this race · watching")]
    [InlineData("no_relay", "No race server · the lobby tries again")]
    [InlineData("relay_failed", "Race server not started · back in the lobby")]
    [InlineData("later", "Not in this race (later)")]
    public void BeingRequeuedIsExplainedInPlainWords(string reason, string text) =>
        Assert.Equal(text, QuickPlaySession.RequeuedText(reason));

    [Theory]
    [InlineData("at_capacity", "Servers at capacity")]
    [InlineData("later", "Taken out of Quick Play (later)")]
    public void BeingSentHomeIsExplainedInPlainWords(string reason, string text) =>
        Assert.Equal(text, QuickPlaySession.RemovedText(reason));

    /// <summary>The column says it as a label (labels and values, no sentences).</summary>
    [Fact]
    public void ADesyncIsSaidAsALabel() => Assert.Equal("Desync detected", QuickPlaySession.DesyncedText);
}
